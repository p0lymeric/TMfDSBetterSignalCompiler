## Release checklist
### Remember to update all these fun numbers!
1. `Source/Directory.Build.props`:
  - Version field: MODVERSION
  - Description field: "A mod assembly for TMfDS vGAMEVERSION"
2. `Collateral/Thunderstore/manifest.json`:
  - version_number field: MODVERSION
3. `CHANGELOG.md`
  - [vMODVERSION] (for TMfDS vGAMEVERSION) - DATEOFRELEASE
4. Commit message: "MODVERSION release"
5. Argument to release.bat/release.sh: "MODVERSION"
