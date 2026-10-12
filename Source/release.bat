@echo off

git status --porcelain | findstr . && set DIRTY=1 || set DIRTY=0
if %DIRTY% equ 1 (
pause
exit
)

set /p TAGVERSION="Version: "

git checkout --detach

git reset --hard
git clean -fdx :/

dotnet publish BetterSignalCompiler.slnx -c Release

git add -f ..\Assemblies\*.dll
git add -f ..\Assemblies\*.pdb

for /f "delims=" %%i in ('git show -s --format^="'%%s' (%%h)" HEAD') do set COMMITLINE=%%i

git commit -m "Build %COMMITLINE%"

git tag -a "v%TAGVERSION%" -m "%TAGVERSION% release"

git -C ..\ archive --prefix=BetterSignalCompiler\ -o BetterSignalCompiler-%TAGVERSION%.zip HEAD

pause
