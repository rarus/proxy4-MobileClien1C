@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set ROOT=%~dp0..
if not exist "%ROOT%\bin" mkdir "%ROOT%\bin"
%CSC% /nologo /out:"%ROOT%\bin\proxy4_MobileClien1C.exe" "%ROOT%\src\*.cs" "%ROOT%\tests\*.cs"
if errorlevel 1 exit /b 1
endlocal
