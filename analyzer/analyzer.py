"""
Website implementation analyzer.

Uses the Claude Agent SDK with three specialized subagents:
  - requirement-verifier : validates each functional requirement
  - wireframe-verifier   : validates each wireframe screen
  - synthesis            : merges partial results into the final JSON report

Playwright MCP is provided to the verifier subagents so they can drive the
site. The agents may create up to 10 users and up to 20 chats if needed
to cover the requirement / wireframe matrix.

Structured output is enforced via SDK MCP tools — each subagent must call
a typed submit tool rather than return free-form JSON text.
"""

from __future__ import annotations

import anyio
import json
import os
import sys
from pathlib import Path

from dotenv import load_dotenv

from models import AnalysisReport, RequirementResult, WireframeResult
from claude_agent_sdk import (
    AgentDefinition,
    AssistantMessage,
    ClaudeAgentOptions,
    ClaudeSDKClient,
    ResultMessage,
    TextBlock,
    ThinkingBlock,
    ToolUseBlock,
    create_sdk_mcp_server,
    tool,
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
# Result store
# ---------------------------------------------------------------------------

class ResultStore:
    """Holds all subagent results in memory for the duration of a run."""

    def __init__(self, report_path: Path) -> None:
        self.requirements: list[RequirementResult] = []
        self.wireframes:   list[WireframeResult]   = []
        self.report:       AnalysisReport | None   = None
        self.report_path   = report_path


# ---------------------------------------------------------------------------
# SDK MCP result-submission tools
# ---------------------------------------------------------------------------

def build_result_server(store: ResultStore):
    """
    Creates an SDK MCP server that exposes four tools used by subagents to
    submit structured results. The Pydantic model schemas are passed as
    input_schema, so the SDK validates arguments before the handler runs.
    """

    @tool(
        "submit_requirement_result",
        "Submit the structured verification result for a single requirement. Call exactly once.",
        RequirementResult.model_json_schema(),
    )
    async def _submit_requirement(args):
        result = RequirementResult.model_validate(args)
        store.requirements.append(result)
        return {"content": [{"type": "text", "text": f"Accepted result for requirement {result.id}."}]}

    @tool(
        "submit_wireframe_result",
        "Submit the structured verification result for a single wireframe. Call exactly once.",
        WireframeResult.model_json_schema(),
    )
    async def _submit_wireframe(args):
        result = WireframeResult.model_validate(args)
        store.wireframes.append(result)
        return {"content": [{"type": "text", "text": f"Accepted result for wireframe {result.id}."}]}

    @tool(
        "get_all_results",
        "Retrieve all submitted requirement and wireframe results for synthesis.",
        {},
    )
    async def _get_all_results(_):
        data = {
            "requirements": [r.model_dump() for r in store.requirements],
            "wireframes":   [w.model_dump() for w in store.wireframes],
        }
        return {"content": [{"type": "text", "text": json.dumps(data)}]}

    @tool(
        "submit_analysis_report",
        "Validate and write the final analysis report to disk. Call exactly once.",
        AnalysisReport.model_json_schema(),
    )
    async def _submit_report(args):
        report = AnalysisReport.model_validate(args)
        store.report = report
        store.report_path.write_text(report.model_dump_json(indent=2), encoding="utf-8")
        return {"content": [{"type": "text", "text": f"REPORT_WRITTEN: {store.report_path}"}]}

    return create_sdk_mcp_server(
        name="results",
        tools=[_submit_requirement, _submit_wireframe, _get_all_results, _submit_report],
    )


# ---------------------------------------------------------------------------
# Allowed tool lists
# ---------------------------------------------------------------------------

PLAYWRIGHT_TOOLS = ["mcp__playwright"]

REQUIREMENT_VERIFIER_TOOLS = ["Read", "Grep", "Glob", "Bash", *PLAYWRIGHT_TOOLS,
                               "mcp__results__submit_requirement_result"]
WIREFRAME_VERIFIER_TOOLS   = ["Read", "Glob", *PLAYWRIGHT_TOOLS,
                               "mcp__results__submit_wireframe_result"]
SYNTHESIS_TOOLS            = ["Read",
                               "mcp__results__get_all_results",
                               "mcp__results__submit_analysis_report"]
ORCHESTRATOR_TOOLS         = ["Read", "Grep", "Glob", "Bash", "Write", "Agent", *PLAYWRIGHT_TOOLS]


# ---------------------------------------------------------------------------
# Subagent prompts
# ---------------------------------------------------------------------------

_SEVERITY_GUIDE = """
Severity guide:
  Critical - data loss, security issue, complete feature failure, crash
  High     - major feature broken, no workaround
  Medium   - partial malfunction, workaround exists
  Low      - cosmetic, minor UX issue
""".strip()

_WIREFRAME_SEVERITY_GUIDE = """
Severity guide:
  Critical - completely wrong screen or severe data issue
  High     - key control missing or non-functional
  Medium   - control present but behaves incorrectly, workaround exists
  Low      - cosmetic / minor layout deviation
""".strip()

REQUIREMENT_VERIFIER_PROMPT = f"""
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
     - "PartiallyImplemented" - feature exists but has gaps, bugs, or missing sub-capabilities.
     - "NotImplemented"       - feature is absent or non-functional.
5. Populate the "bugs" array with every defect observed.

{_SEVERITY_GUIDE}

OUTPUT:
Call `submit_requirement_result` exactly once with your findings.
The tool enforces the schema — do not output JSON text yourself.
""".strip()


WIREFRAME_VERIFIER_PROMPT = f"""
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
5. Populate the "bugs" array with every defect observed.

{_WIREFRAME_SEVERITY_GUIDE}

OUTPUT:
Call `submit_wireframe_result` exactly once with your findings.
The tool enforces the schema — do not output JSON text yourself.
""".strip()


SYNTHESIS_PROMPT = """
You are the SYNTHESIS subagent.

Steps:
1. Call `get_all_results` to retrieve all requirement and wireframe verifications.
2. Count totals for the summary statistics.
3. Collect every bug from every result. For each bug add a "source" field:
   "requirement:<id>" or "wireframe:<id>".
   Deduplicate near-identical bugs by keeping the higher-severity copy.
   Sort bugs: Critical first, then High, Medium, Low.
4. Call `submit_analysis_report` exactly once with the complete report.
   The tool validates and writes the file — do not write files yourself.
5. Reply with the path returned by the tool.

Do not invent new requirements, wireframes, or bugs; only aggregate what was given.
""".strip()


# ---------------------------------------------------------------------------
# Agent wiring
# ---------------------------------------------------------------------------

def build_options(cfg: dict, result_server) -> ClaudeAgentOptions:
    agents = {
        "requirement-verifier": AgentDefinition(
            description="Verifies a single functional requirement against code and live UI.",
            prompt=REQUIREMENT_VERIFIER_PROMPT,
            tools=REQUIREMENT_VERIFIER_TOOLS,
            model="sonnet",
        ),
        "wireframe-verifier": AgentDefinition(
            description="Verifies a single ASCII wireframe against the rendered UI.",
            prompt=WIREFRAME_VERIFIER_PROMPT,
            tools=WIREFRAME_VERIFIER_TOOLS,
            model="sonnet",
        ),
        "synthesis": AgentDefinition(
            description="Merges verification results into the final JSON report.",
            prompt=SYNTHESIS_PROMPT,
            tools=SYNTHESIS_TOOLS,
            model="sonnet",
        ),
    }

    mcp_servers = {
        "playwright": {
            "type": "stdio",
            "command": "npx",
            "args": ["-y", "@playwright/mcp@latest"],
        },
        "results": result_server,
    }

    return ClaudeAgentOptions(
        agents=agents,
        mcp_servers=mcp_servers,
        allowed_tools=ORCHESTRATOR_TOOLS,
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
   (paths, URL, credentials, budgets). The subagent submits its result via
   the `submit_requirement_result` tool — you do not need to parse its output.

   For EACH wireframe, dispatch the `wireframe-verifier` subagent via the
   Task tool in the same way.

   You may dispatch subagents in parallel when independent. Respect the
   global user/chat budgets - when near the cap, instruct subagents to
   reuse existing accounts and chats.

4. After all subagents return, dispatch the `synthesis` subagent via Task.
   It will read all submitted results via `get_all_results`, aggregate them,
   and write the final report via `submit_analysis_report`.

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
    store = ResultStore(cfg["report_path"])
    result_server = build_result_server(store)
    options = build_options(cfg, result_server)
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
                        pass
                    elif isinstance(block, ToolUseBlock):
                        print(f"  [tool] {block.name}")
            elif isinstance(message, ResultMessage):
                print(f"\n[analyzer] finished. cost=${message.total_cost_usd or 0:.4f}")

    report = store.report
    if report is None and cfg["report_path"].exists():
        try:
            report = AnalysisReport.model_validate_json(
                cfg["report_path"].read_text(encoding="utf-8")
            )
        except Exception as exc:
            print(f"[analyzer] WARNING: could not parse report — {exc}")

    if report:
        print("\n[analyzer] summary:")
        print(report.summary.model_dump_json(indent=2))
        print(f"\n[analyzer] bugs found: {report.summary.total_bugs} "
              f"(critical={report.summary.bugs_critical} "
              f"high={report.summary.bugs_high} "
              f"medium={report.summary.bugs_medium} "
              f"low={report.summary.bugs_low})")
    else:
        print("[analyzer] WARNING: no report produced.")


if __name__ == "__main__":
    anyio.run(run)
