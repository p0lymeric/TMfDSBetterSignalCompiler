#!/bin/sh

test -z "$(git status --porcelain)" || exit 1
[ $# -eq 0 ] && exit 1

git checkout --detach

git reset --hard
git clean -fdx :/

dotnet publish BetterSignalCompiler.slnx -c Release

git add -f ../Assemblies/*.dll
git add -f ../Assemblies/*.pdb

git commit -m "Build $(git show -s --format="'%s' (%h)" HEAD)"

git tag -a "v$1" -m "$1 release"

git -C ../ archive --prefix=BetterSignalCompiler/ -o BetterSignalCompiler-$1.zip HEAD
