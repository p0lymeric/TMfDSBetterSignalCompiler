.PHONY: all build clean distclean

all: build

build:
	dotnet publish BetterSignalCompiler.slnx -c Release

clean:
	rm -rf .vs Artifacts

distclean:
	git clean -fdX :/
