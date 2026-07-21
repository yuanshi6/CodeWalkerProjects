@echo off
setlocal EnableDelayedExpansion
rem ============================================================================
rem  commit-push.bat - stage everything, commit, and push to origin.
rem
rem  Usage:
rem    commit-push.bat                    (prompts for a commit message)
rem    commit-push.bat Your message here  (uses the arguments as the message)
rem
rem  Always runs against the repo this script sits in, on the current branch.
rem ============================================================================
cd /d "%~dp0"

rem --- current branch -----------------------------------------------------
set "BRANCH="
for /f "delims=" %%b in ('git branch --show-current 2^>nul') do set "BRANCH=%%b"
if not defined BRANCH (
    echo ERROR: this doesn't look like a git repository.
    pause
    exit /b 1
)

rem --- anything to commit? -------------------------------------------------
set "HASCHANGES="
for /f "delims=" %%i in ('git status --porcelain') do set "HASCHANGES=1"
if not defined HASCHANGES (
    echo Working tree is clean - nothing to commit.
    echo Checking for unpushed commits...
    git push origin %BRANCH%
    pause
    exit /b 0
)

echo.
echo  Branch: %BRANCH%
echo  ---------------- pending changes ----------------
git status --short
echo  --------------------------------------------------
echo.

rem --- commit message: arguments, or prompt, or dated default ---------------
set "MSG=%*"
if not defined MSG set /p "MSG=Commit message (Enter = dated default): "
if not defined MSG set "MSG=Update %DATE% %TIME:~0,5%"

echo.
choice /c YN /m "Commit and push ALL changes above as '%MSG%'"
if errorlevel 2 (
    echo Aborted - nothing was committed.
    pause
    exit /b 0
)

rem --- stage, commit, push ---------------------------------------------------
git add -A
git commit -m "%MSG%"
if errorlevel 1 (
    echo.
    echo ERROR: commit failed.
    pause
    exit /b 1
)

git push origin %BRANCH%
if errorlevel 1 (
    echo.
    echo ERROR: push failed. If the remote has new commits, run:
    echo    git pull --rebase origin %BRANCH%
    echo then run this script again.
    pause
    exit /b 1
)

echo.
echo Done - committed and pushed to origin/%BRANCH%.
pause
exit /b 0
