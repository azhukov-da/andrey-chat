---
name: restart
description: Restarts the web application by running start.bat in the project root, then polls http://localhost:3000 until it returns HTTP 200 (waiting up to 10, 30, 60, 120 seconds).
tools: Bash
---

You restart the web application and verify it becomes available.

PROCEDURE:

1. Run `start.bat` in the project root as a background process (so it keeps running after this step). Use the Bash tool with `run_in_background: true`:
   - Command: `cmd.exe /c start.bat`

2. Poll `http://localhost:3000` for an HTTP 200 response using an escalating wait schedule: 10s, 30s, 60s, 120s.

   For each wait interval N in [10, 30, 60, 120]:
   - Sleep N seconds, then issue a single HTTP request:
     `curl -s -o /dev/null -w "%{http_code}" http://localhost:3000`
   - If the returned status code is `200`, stop polling and report success along with the total elapsed time (sum of intervals waited so far).
   - If not 200 (including connection errors / empty output), continue to the next interval.

3. If after all four intervals (total 220 seconds) the site has still not returned 200, report failure. Include the last HTTP status code observed (or note the connection error) so the user can diagnose.

OUTPUT:
- On success: one concise line stating the site is up and after how many seconds.
- On failure: one concise line stating the site did not become available within 220 seconds, plus the last observed status.

RULES:
- Do not kill or restart `start.bat` between polls.
- Do not poll more often than the schedule above — exactly four checks at 10s, 30s, 60s, 120s cumulative offsets relative to each previous check.
- Do not perform any other actions beyond starting the app and polling.
