# Website Implementation Analyzer

Python agent that audits the chat application against its written requirements
and ASCII wireframes, using the Claude Agent SDK and Playwright MCP.

## Architecture

```
           +--------------------+
           |    Orchestrator    |   extracts requirements + wireframes,
           +---------+----------+   dispatches Tasks in parallel
                     |
    +----------------+-----------------+-------------------+
    |                                  |                   |
+---v----------------+  +--------------v------+  +---------v--------+
| requirement-       |  | wireframe-          |  | synthesis        |
| verifier (x N)     |  | verifier (x M)      |  | (final JSON)     |
| Read/Grep/Glob/    |  | Read/Glob/          |  | Read/Write       |
| Bash/Playwright    |  | Playwright          |  |                  |
+--------------------+  +---------------------+  +------------------+
```

Each subagent returns strict JSON. Synthesis aggregates everything and
writes `report.json` with typed status (`Implemented`,
`PartiallyImplemented`, `NotImplemented`) and free-form comments for
every requirement and every wireframe.

## Setup

```bash
cd analyzer
python -m venv .venv
.venv\Scripts\activate          # Windows
pip install -r requirements.txt

# Playwright MCP is launched via `npx` - Node.js must be installed.
cp .env.example .env
# then edit .env
```

## Run

```bash
python analyzer.py
```

The final report is written to the path in `REPORT_PATH` (default
`./report.json`).

## Budgets

The agents are instructed to create at most **10 users** and **20 chats**
across the entire run; adjust via `MAX_USERS` / `MAX_CHATS` in `.env`.
