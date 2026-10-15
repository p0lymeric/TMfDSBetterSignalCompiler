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

cd ..\Source

dotnet publish BetterSignalCompiler.slnx -c Release

git add -f ..\polymeric.BetterSignalCompiler.dll
git add -f ..\polymeric.BetterSignalCompiler.pdb

for /f "delims=" %%i in ('git show -s --format^="'%%s' (%%h)" HEAD') do set COMMITLINE=%%i

git commit -m "Build %COMMITLINE%"

git tag -a "v%TAGVERSION%" -m "%TAGVERSION% release"

rem git archive will not normalize the slashes given to it for the --prefix argument, even if they violate ZIP file format specification.
rem Use forward slashes (/) for --prefix.

git -C ..\ archive --prefix=BepInEx/plugins/BetterSignalCompiler/ -o BetterSignalCompiler-%TAGVERSION%.zip HEAD

git -C ..\ archive --add-file=Collateral\Thunderstore\manifest.json --add-file=Collateral\Thunderstore\icon.png --add-file=README.md --add-file=CHANGELOG.md --prefix=BepInEx/plugins/BetterSignalCompiler/ -o BetterSignalCompiler-Thunderstore-%TAGVERSION%.zip HEAD polymeric.BetterSignalCompiler.dll polymeric.BetterSignalCompiler.pdb README.md LICENSE.md CHANGELOG.md

pause
