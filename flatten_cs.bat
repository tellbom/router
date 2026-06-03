@echo off
setlocal EnableDelayedExpansion

if not exist temp mkdir temp

for /r %%F in (*.cs) do (

    echo %%F | findstr /i "\\bin\\ \\obj\\ \\temp\\ \\node_modules\\" >nul
    if errorlevel 1 (

        set "rel=%%F"
        set "rel=!rel:%CD%\=!"
        set "newname=!rel:\=_!"

        copy "%%F" "temp\!newname!" >nul
    )
)

echo Export completed.
pause