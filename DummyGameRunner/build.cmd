@echo off
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
cl.exe /nologo /O2 /MT /W4 /Fe:%~dp0DummyGameRunner.exe /Fo:%TEMP%\DummyGameRunner_main.obj %~dp0main.cpp /link /SUBSYSTEM:WINDOWS

