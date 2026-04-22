# The Story

## 1. Project Setup

1. Created `FE` and `BE` folders to separate the two parts of the system's source code.
2. Created the solution and projects for the BE using Visual Studio. Set up some project relationships. Added ASP.NET Core Identity (user management).

## 2. Planning

3. Asked Claude Code to generate implementation plans for FE and BE using Plan Mode and High Effort. Loaded the spec in `.docx` format. Explained a few details regarding the tech stacks for both sides, something like: *"The frontend should be a React app that uses Vite, TypeScript strict, and DaisyUI. The backend should be an ASP.NET Core Web API that uses Entity Framework Core, SignalR, EF migrations, and connects to PostgreSQL."* Gave instructions to ask questions and suggest alternatives where necessary.
4. Reviewed the implementation plans very briefly due to lack of time. The plans can be found in the repo in their respective folders. They evolved slightly during implementation.

## 3. Initial Implementation

5. Started implementation of the BE by feeding the plan part by part into GitHub Copilot in Visual Studio, then asked it to implement the rest of the plan.
6. In parallel, asked Claude Code to implement the FE. I use Claude Code from various places: the VS Code plugin and the Claude Code Windows app.
7. Asked GitHub Copilot to add Swagger.
8. Provided the Swagger link to Claude Code so it could adjust the FE accordingly.

## 4. First Run and Fixes

9. Got a somewhat working BE and FE. The FE started with an empty screen.
10. Spent multiple turns asking Claude Code to fix the issue above. Succeeded after a few attempts (my lack of deep FE understanding did not help).
11. Performed manual testing.
12. Added Docker support to the FE via Claude Code. Added `docker-compose`.

## 5. SignalR Debugging

13. Found an issue with auto-refresh of chat messages. Tried to fix it by prompting, but it did not help.
14. Started investigating how the SignalR logic was implemented. Asked both Claude Code and GitHub Copilot to explain it.
15. Started debugging to find out where the problem was.
16. Had to fix a few issues on the FE and BE to get it working (by prompting).
17. Fixed a few additional issues manually by prompting.

## 6. Automated Bug Fixing

18. Realized I needed to restructure the `.docx` requirements into a format better suited for an agent. Created two separate files: `chat_requirement.md` and `wireframes.txt`.
19. Decided to automate the bug-fixing process with agent(s), located in the `analyzer` folder.
20. First, created a Claude Agent SDK agent in Python. Refactored it a little by asking Claude Code.
21. Started the agent and wasted $6.99 on it (it should have used the Anthropic API key, not the subscription).
22. Had to switch to a CLI agent.
23. Created the first CLI agent based on the original one, but without executing Claude Agent SDK Python code.
24. Generated `response.json` with the analysis results.
25. Created two agents, `restart` and `fix`, to automate bug fixes based on `response.json`.
26. Asked Claude Code to fix bugs using the agents in a semi-automatic way (asked it to fix the first critical bug, verified manually, then asked it to fix five more, and so on). Fixed all critical and high bugs (verified by Claude Code, and the first few also verified manually).

## 7. Final State

27. Performed a smoke test of the app. Applied a few tiny fixes by prompting.
28. Ran out of time. The app is much more stable and has more functionality implemented thanks to the agents, but it is still not complete.
29. The agents can be found in the respective Claude folder for agents: `.claude\agents`.
30. More details can probably be found in the git history 🙂
