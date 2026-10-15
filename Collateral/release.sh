#!/bin/sh

test -z "$(git status --porcelain)" || exit 1
[ $# -eq 0 ] && exit 1

git checkout --detach

git reset --hard
git clean -fdx :/

cd ../Source

dotnet publish BetterSignalCompiler.slnx -c Release

git add -f ../polymeric.BetterSignalCompiler.dll
git add -f ../polymeric.BetterSignalCompiler.pdb

git commit -m "Build $(git show -s --format="'%s' (%h)" HEAD)"

git tag -a "v$1" -m "$1 release"

git -C ../ archive --prefix=BepInEx/plugins/BetterSignalCompiler/ -o BetterSignalCompiler-$1.zip HEAD
git -C ../ archive --add-file=Collateral/Thunderstore/manifest.json --add-file=Collateral/Thunderstore/icon.png --add-file=README.md --add-file=CHANGELOG.md --prefix=BepInEx/plugins/BetterSignalCompiler/ -o BetterSignalCompiler-Thunderstore-$1.zip HEAD
