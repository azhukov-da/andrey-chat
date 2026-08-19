@echo off
setlocal enabledelayedexpansion

rem ============================================================================
rem  Gated verification: all three test layers, both coverage reports, and the
rem  scenario traceability report. See CLAUDE.md for what each layer covers and
rem  for the per-layer commands to use during development.
rem
rem  Prerequisites are checked first and named if missing, before any test runs.
rem  Each layer's failure is recorded and reported in the summary rather than
rem  aborting the run, so one broken layer does not hide the others' results.
rem ============================================================================

pushd "%~dp0"

set "RAW=docs\test-coverage\raw"
set "BE_COVERAGE_RAW=%RAW%\backend-coverage"
set "BE_REPORT=docs\test-coverage\backend"
set "FE_REPORT=docs\test-coverage\frontend"
set "TRX_DIR=BE\Tests.Integration\TestResults"

echo ============================================================
echo  Prerequisites
echo ============================================================

set "MISSING="

where docker >nul 2>&1
if errorlevel 1 (
    echo   [X] docker is not on PATH.
    set "MISSING=1"
) else (
    echo   [OK] docker
)

rem The backend integration layer talks to the db container over the host port that
rem docker-compose.tests.yml publishes. docker-compose.yml deliberately does not, so this
rem script brings the container up itself with the test overlay applied.
call :ensure_db
if errorlevel 1 set "MISSING=1"

rem The end-to-end layer drives the assembled stack through the frontend's nginx.
powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri http://localhost:3000 -UseBasicParsing -TimeoutSec 10; if ($r.StatusCode -eq 200) { exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>&1
if errorlevel 1 (
    echo   [X] The frontend is not serving on http://localhost:3000.
    echo       Start the full stack with:
    echo         start.bat
    set "MISSING=1"
) else (
    echo   [OK] Frontend on http://localhost:3000
)

if not exist "FE\node_modules" (
    echo   [X] FE\node_modules is missing. Run: cd FE ^&^& npm install
    set "MISSING=1"
) else (
    echo   [OK] FE dependencies installed
)

if defined MISSING (
    echo.
    echo Prerequisites are not satisfied. Nothing was run.
    popd
    exit /b 1
)

echo.
echo Cleaning previous results...
if exist "%RAW%" rmdir /s /q "%RAW%"
if exist "%TRX_DIR%" rmdir /s /q "%TRX_DIR%"
if exist "%BE_REPORT%" rmdir /s /q "%BE_REPORT%"
mkdir "%RAW%" >nul 2>&1

echo.
echo ============================================================
echo  1/4  Backend integration  (BE\Tests.Integration)
echo ============================================================
set "BE_RESULT=passed"
dotnet test BE\Tests.Integration\Tests.Integration.csproj ^
    --nologo ^
    --settings BE\coverlet.runsettings ^
    --collect:"XPlat Code Coverage" ^
    --logger "trx;LogFileName=integration.trx" ^
    --results-directory "%TRX_DIR%"
if errorlevel 1 set "BE_RESULT=FAILED"

echo.
echo ============================================================
echo  2/4  Frontend unit and component  (FE)
echo ============================================================
set "FE_RESULT=passed"
pushd FE
call npm run test:coverage
if errorlevel 1 set "FE_RESULT=FAILED"
popd

echo.
echo ============================================================
echo  3/4  End-to-end  (FE\e2e)
echo ============================================================
set "E2E_RESULT=passed"
pushd FE
call npx playwright test
if errorlevel 1 set "E2E_RESULT=FAILED"
popd

echo.
echo ============================================================
echo  Backend coverage report
echo ============================================================
call dotnet tool restore >nul 2>&1
call dotnet reportgenerator ^
    "-reports:%TRX_DIR%\**\coverage.cobertura.xml" ^
    "-targetdir:%BE_REPORT%" ^
    "-reporttypes:Html;TextSummary;Cobertura" ^
    "-title:Andrey Chat backend"
if errorlevel 1 echo   [!] ReportGenerator failed; the backend coverage figure will read as unavailable.

echo.
echo ============================================================
echo  4/4  Scenario traceability
echo ============================================================
set "SPEC_RESULT=passed"
node tools\spec-coverage
if errorlevel 1 set "SPEC_RESULT=FAILED"

echo.
echo ============================================================
echo  Coverage gate
echo ============================================================
rem Reported, not enforced. See tools\coverage-gate and design D7.
node tools\coverage-gate
set "GATE_EXIT=%errorlevel%"

echo.
echo ============================================================
echo  Summary
echo ============================================================
echo   Backend integration ....... !BE_RESULT!
echo   Frontend unit ............. !FE_RESULT!
echo   End-to-end ................ !E2E_RESULT!
echo   Scenario claims ........... !SPEC_RESULT!
echo.
echo   Reports:
echo     Scenario coverage ....... docs\test-coverage\scenarios.md
echo     Backend line coverage ... %BE_REPORT%\index.html
echo     Frontend line coverage .. %FE_REPORT%\index.html
echo     End-to-end run .......... docs\test-coverage\e2e-report\index.html
echo     Raw results ............. %RAW%
echo.

set "EXIT=0"
if not "!BE_RESULT!"=="passed" set "EXIT=1"
if not "!FE_RESULT!"=="passed" set "EXIT=1"
if not "!E2E_RESULT!"=="passed" set "EXIT=1"
if not "!SPEC_RESULT!"=="passed" set "EXIT=1"

if "!EXIT!"=="0" (
    echo   All layers passed.
) else (
    echo   One or more layers failed. See the sections above.
)

popd
exit /b !EXIT!

rem ----------------------------------------------------------------------------
rem  Makes PostgreSQL reachable on localhost:55432, starting the db container with
rem  the test overlay if it is not already up. Returns 1 when it cannot be reached.
rem ----------------------------------------------------------------------------
:ensure_db
call :probe_db
if not errorlevel 1 (
    echo   [OK] PostgreSQL on localhost:55432
    exit /b 0
)

echo   [..] PostgreSQL is not reachable on localhost:55432; starting the db container...
rem --wait blocks until the container's own healthcheck passes, so the migrations the
rem test harness runs on startup do not race an accepting-but-not-ready server.
docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d --wait db
if errorlevel 1 (
    echo   [X] Could not start the db container. Start it by hand with:
    echo         docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db
    exit /b 1
)

call :probe_db
if errorlevel 1 (
    echo   [X] The db container is up but localhost:55432 is still not reachable.
    echo       Check that docker-compose.tests.yml is publishing the port:
    echo         docker compose -f docker-compose.yml -f docker-compose.tests.yml ps db
    exit /b 1
)
echo   [OK] PostgreSQL on localhost:55432 ^(started by this script^)
exit /b 0

:probe_db
powershell -NoProfile -Command "try { $c = New-Object Net.Sockets.TcpClient; $c.Connect('localhost', 55432); $c.Close(); exit 0 } catch { exit 1 }" >nul 2>&1
exit /b %errorlevel%
