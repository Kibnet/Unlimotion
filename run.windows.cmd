@echo off
setlocal

set "PROJECT_PATH=%~dp0src\Unlimotion.Desktop\Unlimotion.Desktop.csproj"
pushd "%~dp0" || exit /b 1
dotnet run --project "%PROJECT_PATH%" -- %*
set "EXIT_CODE=%ERRORLEVEL%"
popd

endlocal & exit /b %EXIT_CODE%
