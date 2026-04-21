"""
Website implementation analyzer.

Uses the Claude Agent SDK with three specialized subagents:
  - requirement-verifier : validates each functional requirement
  - wireframe-verifier   : validates each wireframe screen
  - synthesis            : merges partial results into the final JSON report

Playwright MCP is provided to the verifier subagents so they can drive the
site. The agents may create up to 10 users and up to 20 chats if needed
to cover the requirement / wireframe matrix.
"""

from __future__ import annotations

import anyio
import json
import os
import sys
from pathlib import Path

from dotenv import load_dotenv

from claude_agent_sdk import (
    AgentDefinition,
    AssistantMessage,
    ClaudeAgentOptions,
    ClaudeSDKClient,
    ResultMessage,
    TextBlock,
    ThinkingBlock,
    ToolUseBlock,
)


# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

def load_config() -> dict:
    load_dotenv()

    def req(name: str) -> str:
        v = os.getenv(name)
        if not v:
            sys.exit(f"Missing required env var: {name}")
        return v

    cfg = {
        "website_url":     req("WEBSITE_URL"),
        "documentation":   Path(req("DOCUMENTATION_PATH")).resolve(),
        "wireframes":      Path(req("WIREFRAMES_PATH")).resolve(),
        "backend":         Path(req("BACKEND_PATH")).resolve(),
        "frontend":        Path(req("FRONTEND_PATH")).resolve(),
        "user_email":      req("TEST_USER_EMAIL"),
        "user_password":   req("TEST_USER_PASSWORD"),
        "report_path":     Path(os.getenv("REPORT_PATH", "./report.json")).resolve(),
        "max_users":       int(os.getenv("MAX_USERS", "10")),
        "max_chats":       int(os.getenv("MAX_CHATS", "20")),
    }

    for key in ("documentation", "wireframes", "backend", "frontend"):
        if not cfg[key].exists():
            sys.exit(f"Path for {key} does not exist: {cfg[key]}")

    return cfg


# ---------------------------------------------------------------------------
# Subagent definitions
# ---------------------------------------------------------------------------

# Playwright MCP exposes many tools; we allow the whole namespace via wildcard.
PLAYWRIGHT_TOOLS = ["mcp__playwright"]

REQUIREMENT_VERIFIER_PROMPT = """
You are the REQUIREMENT VERIFIER subagent.

Given a single functional requirement (or small batch), determine whether the
live website and its source code satisfy it. You MUST:

1. Read the relevant section of the requirements file for exact wording.
2. Inspect backend (C#/.NET) and frontend (React/TS) source code with Grep/Glob/Read
   to confirm the feature is actually implemented, not just wired in the UI.
3. Drive the live site with Playwright:
     - Sign in with the provided test user, OR
     - Register new users if the scenario requires additional participants.
     - Create chats / send messages as needed.
   Stay within the global budgets: at most 10 registered users and at most
   20 created chats across the ENTIRE analysis run.
4. Decide a status:
     - "Implemented"          - feature is fully present and works end-to-end.
     - "PartiallyImplemented" - feature exists but has gaps, bugs, or missing
                                sub-capabilities.
     - "NotImplemented"       - feature is absent or non-functional.
5. Return ONLY a JSON object of the shape:
   {
     "id": "<stable requirement id, e.g. 2.1>",
     "title": "<short requirement title>",
     "status": "Implemented|PartiallyImplemented|NotImplemented",
     "comment": "<free-form evidence: files inspected, UI actions taken, bugs found>",
     "evidence": {
       "code_refs": ["path:line", ...],
       "ui_actions": ["<short description>", ...]
     }
   }

Do not include any prose outside the JSON.
""".strip()


WIREFRAME_VERIFIER_PROMPT = """
You are the WIREFRAME VERIFIER subagent.

You are given ONE ASCII wireframe block describing an intended screen.
Your job is to compare it against the real rendered UI.

Steps:
1. Read the wireframe carefully. Identify its screen purpose, key regions,
   and named controls.
2. Use Playwright to navigate to the matching page on the live site.
   Sign in with the test user when authentication is needed. If the screen
   requires multiple users or chats, you may create them (budget: 10 users,
   20 chats shared across the whole run).
3. For each labelled element in the wireframe, check whether a visually
   equivalent element exists in the live UI. Minor styling differences are OK;
   missing regions, missing controls, or wrong information architecture are not.
4. Optionally cross-check the React source under the frontend path to confirm
   component structure.
5. Emit ONLY a JSON object:
   {
     "id": "<wireframe id or screen name>",
     "screen": "<short screen description>",
     "status": "Implemented|PartiallyImplemented|NotImplemented",
     "missing_elements": ["<label>", ...],
     "extra_elements":   ["<label>", ...],
     "comment": "<free-form notes, URL visited, screenshots taken>"
   }
""".strip()


SYNTHESIS_PROMPT = """
You are the SYNTHESIS subagent.

You receive two JSON arrays:
  - "requirements": list of requirement verifier results
  - "wireframes":   list of wireframe verifier results

Produce a single final JSON report with this exact shape:
{
  "summary": {
    "total_requirements": <int>,
    "implemented": <int>,
    "partially_implemented": <int>,
    "not_implemented": <int>,
    "total_wireframes": <int>,
    "wireframes_implemented": <int>,
    "wireframes_partially_implemented": <int>,
    "wireframes_not_implemented": <int>,
    "overall_comment": "<2-4 sentence executive summary>"
  },
  "requirements": [ ...verbatim items from input... ],
  "wireframes":   [ ...verbatim items from input... ]
}

Write the report to the path supplied in the orchestrator prompt using the
Write tool, then reply with the absolute path of the file you wrote.
Do not invent new requirements or wireframes; only aggregate what was given.
""".strip()


def build_options(cfg: dict) -> ClaudeAgentOptions:
    agents = {
        "requirement-verifier": AgentDefinition(
            description="Verifies a single functional requirement against code and live UI.",
            prompt=REQUIREMENT_VERIFIER_PROMPT,
            tools=["Read", "Grep", "Glob", "Bash", *PLAYWRIGHT_TOOLS],
            model="sonnet",
        ),
        "wireframe-verifier": AgentDefinition(
            description="Verifies a single ASCII wireframe against the rendered UI.",
            prompt=WIREFRAME_VERIFIER_PROMPT,
            tools=["Read", "Glob", *PLAYWRIGHT_TOOLS],
            model="sonnet",
        ),
        "synthesis": AgentDefinition(
            description="Merges verification results into the final JSON report.",
            prompt=SYNTHESIS_PROMPT,
            tools=["Read", "Write"],
            model="sonnet",
        ),
    }

    mcp_servers = {
        "playwright": {
            "type": "stdio",
            "command": "npx",
            "args": ["-y", "@playwright/mcp@latest"],
        }
    }

    # Tools the orchestrator itself may call. Subagents get their own lists above.
    allowed_tools = [
        "Read", "Grep", "Glob", "Bash", "Write", "Task",
        *PLAYWRIGHT_TOOLS,
    ]

    return ClaudeAgentOptions(
        agents=agents,
        mcp_servers=mcp_servers,
        allowed_tools=allowed_tools,
        permission_mode="acceptEdits",
        cwd=str(Path.cwd()),
        model="sonnet",
    )


# ---------------------------------------------------------------------------
# Orchestrator prompt
# ---------------------------------------------------------------------------

def build_orchestrator_prompt(cfg: dict) -> str:
    return f"""
You are the ORCHESTRATOR of a website implementation audit.

INPUTS (absolute paths, read them yourself):
  - Requirements document : {cfg['documentation']}
  - Wireframes (ASCII)    : {cfg['wireframes']}
  - Backend source tree   : {cfg['backend']}
  - Frontend source tree  : {cfg['frontend']}
  - Website URL           : {cfg['website_url']}
  - Test user email       : {cfg['user_email']}
  - Test user password    : {cfg['user_password']}
  - Output report path    : {cfg['report_path']}

RESOURCE BUDGETS for the whole run (shared across all subagents):
  - At most {cfg['max_users']} registered users (including the test user).
  - At most {cfg['max_chats']} chats created.

PROCEDURE:

1. Read the requirements document and extract every distinct functional
   requirement. Assign each a stable id (e.g. "2.1", "2.1.a") and short title.

2. Read the wireframes file and split it into individual wireframe blocks.
   Assign each an id and a screen name.

3. For EACH requirement, dispatch the `requirement-verifier` subagent via the
   Task tool. Pass the requirement id, full text, and the shared context
   (paths, URL, credentials, budgets). Collect its JSON result.

   For EACH wireframe, dispatch the `wireframe-verifier` subagent via the
   Task tool. Pass the wireframe id and the raw ASCII block plus shared
   context. Collect its JSON result.

   You may dispatch subagents in parallel when independent. Respect the
   global user/chat budgets - when near the cap, instruct subagents to
   reuse existing accounts and chats.

4. After all subagents return, call the `synthesis` subagent via Task. Give
   it the two arrays plus the output path `{cfg['report_path']}`. It will
   write the final JSON file.

5. When synthesis confirms the file is written, reply with a single line:
   REPORT_WRITTEN: <absolute path>

Rules:
  - Never fabricate results; if a subagent fails, re-dispatch with a tighter
    scope or mark the item as NotImplemented with an explanatory comment.
  - Keep your own prose minimal - most work happens inside subagents.
""".strip()


# ---------------------------------------------------------------------------
# Runner
# ---------------------------------------------------------------------------

async def run() -> None:
    cfg = load_config()
    options = build_options(cfg)
    prompt = build_orchestrator_prompt(cfg)

    print(f"[analyzer] website  : {cfg['website_url']}")
    print(f"[analyzer] report   : {cfg['report_path']}")
    print(f"[analyzer] budgets  : {cfg['max_users']} users / {cfg['max_chats']} chats")
    print("[analyzer] starting orchestrator...\n")

    async with ClaudeSDKClient(options=options) as client:
        await client.query(prompt)

        async for message in client.receive_response():
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        print(block.text)
                    elif isinstance(block, ThinkingBlock):
                        pass  # skip internal thinking
                    elif isinstance(block, ToolUseBlock):
                        print(f"  [tool] {block.name}")
            elif isinstance(message, ResultMessage):
                print(f"\n[analyzer] finished. cost=${message.total_cost_usd or 0:.4f}")

    if cfg["report_path"].exists():
        try:
            data = json.loads(cfg["report_path"].read_text(encoding="utf-8"))
            summary = data.get("summary", {})
            print("\n[analyzer] summary:")
            print(json.dumps(summary, indent=2))
        except json.JSONDecodeError:
            print("[analyzer] WARNING: report file is not valid JSON.")
    else:
        print("[analyzer] WARNING: no report file produced.")


if __name__ == "__main__":
    anyio.run(run)
