@echo off
echo All projects will be cleaned is this folder!
echo A full rebuild of everything will be required!
echo Press Ctrl+C now to abort!
pause
cd /D %~dp0
..\SysWeaver\_tools\VsClean\VsClean.exe .
pause