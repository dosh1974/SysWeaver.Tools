@echo off
if not "%1%"=="" goto skip1
echo All projects will be cleaned is this folder!
echo A full rebuild of everything will be required!
echo Press Ctrl+C now to abort!
pause
:skip1

cd /D %~dp0
..\SysWeaver\_tools\VsClean\VsClean.exe .

if "%1%"=="" pause