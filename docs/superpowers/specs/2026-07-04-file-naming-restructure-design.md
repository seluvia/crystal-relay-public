# Crystal Relay File Naming Restructure

## Objective

Simplify the Crystal Relay package file naming so the install folder and executable use clean, static names while version information is conveyed through a dummy version file. The self-updater must remain fully compatible.

## Scope

- Release and beta packages only
- Test builds remain unchanged (internal testing format)

## Package Structure

### Release

```
CrystalRelay-v3.1.9-win-x64.zip                  (GitHub release asset)
  └── Crystal Relay\                              (always static)
        ├── Crystal Relay.exe                     (always static)
        ├── CrystalRelayUpdater.exe               (unchanged)
        ├── 3.1.9.txt                             (NEW: empty file, filename = version)
        ├── crystal-relay-update.json             (unchanged layout)
        ├── README.md                             (unchanged)
        └── CHANGELOG.txt                         (unchanged)
```

### Beta

```
CrystalRelay-v3.1.9-beta4-win-x64.zip            (GitHub release asset)
  └── Crystal Relay\                              (always static)
        ├── Crystal Relay.exe                     (always static)
        ├── CrystalRelayUpdater.exe               (unchanged)
        ├── 3.1.9-beta4.txt                       (NEW: empty file, filename = version)
        ├── crystal-relay-update.json             (unchanged layout)
        ├── README.md                             (unchanged)
        └── CHANGELOG.txt                         (unchanged)
```

### Key Differences from Current Layout

| Aspect | Current | New |
|--------|---------|-----|
| Folder name | `CrystalRelayTwitchOsc-v<ver>-win-x64` | `Crystal Relay` |
| .exe name | `CrystalRelayTwitchOsc-v<ver>.exe` | `Crystal Relay.exe` |
| Version indication | In folder/.exe name | `<ver>.txt` dummy file |
| Beta flag | `beta-build.flag` file | `<ver>-beta<N>.txt` filename |
| ZIP asset name | `CrystalRelayTwitchOsc-v<ver>-win-x64.zip` | `CrystalRelay-v<ver>-win-x64.zip` |

## Manifest Changes (`crystal-relay-update.json`)

- `entryExecutableName` changes from `CrystalRelayTwitchOsc-v<ver>.exe` to `Crystal Relay.exe`
- All other fields (`productName`, `version`, `channel`, `runtime`) remain unchanged

## Updater Changes

### `ApplicationUpdateService.cs`

- **Line 192-193** (ZIP asset URL pattern):
  - From: `$"CrystalRelayTwitchOsc-v{version.ToDisplayString()}-{RuntimeName}.zip"`
  - To:   `$"CrystalRelay-v{version.ToDisplayString()}-{RuntimeName}.zip"`

### `ApplicationSelfUpdateService.cs`

- `PackageFolderPrefix` constant:
  - From: `"CrystalRelayTwitchOsc-v"`
  - To:   `"CrystalRelay-v"` (matches new ZIP asset name prefix)
- `ExecutableSearchPattern` constant:
  - From: `"CrystalRelayTwitchOsc-v*.exe"`
  - To:   `"Crystal Relay.exe"` (static name, no wildcard needed)
- Package extraction/manifest resolution: look for `crystal-relay-update.json` inside the known `Crystal Relay\` subfolder under extraction root
- Folder relocation logic (renaming install folder per version): remove or disable, since the install folder is always `Crystal Relay`

## Build Script Changes

### `Build-Crystal-Relay-Release.ps1`

- Output folder: `Crystal Relay` instead of `CrystalRelayTwitchOsc-v<ver>-win-x64`
- .exe rename: `CrystalRelayTwitchOsc.exe -> Crystal Relay.exe`
- Create empty `<ver>.txt` in package root
- ZIP asset: `CrystalRelay-v<ver>-win-x64.zip`
- Manifest `entryExecutableName`: `Crystal Relay.exe`

### `Build-Crystal-Relay-Beta.ps1`

- Same folder/.exe naming as release
- Version file: `<ver>-beta<N>.txt` (no separate `beta-build.flag`)
- ZIP asset: `CrystalRelay-v<ver>-beta<N>-win-x64.zip`
- Manifest `entryExecutableName`: `Crystal Relay.exe`

### `Build-Crystal-Relay-Test.ps1`

- No changes. Test builds stay in their current format.

## Files to Modify

1. `Build-Crystal-Relay-Release.ps1` — folder, exe name, version file, ZIP name, manifest
2. `Build-Crystal-Relay-Beta.ps1` — same changes as release + beta-specific adjustments
3. `VrcTwitchOscBridge\ApplicationUpdateService.cs` — ZIP asset URL pattern
4. `VrcTwitchOscBridge\ApplicationSelfUpdateService.cs` — package constants, executable search, folder relocation
5. *(Possibly)* `CrystalRelayUpdater\Program.cs` — if folder relocation logic references the old prefix

## Not Changed

- Test builds
- Source launcher (`Run-Crystal-Relay-Source.ps1`)
- Debug launcher (`Launch-Crystal-Relay-Debug.bat`)
- CHANGELOG.txt format (version text entries stay the same)
- AGENTS.md version rules
- App data paths
- GitHub release/publication workflow

## Self-Review Checklist

- [ ] No placeholders, TBDs, or incomplete sections
- [ ] All sections consistent: package structure, manifest, updater, build scripts all agree
- [ ] Scope focused: only release/beta packages, test builds untouched
- [ ] No ambiguity: each naming pattern has exact before/after
- [ ] Updater compatibility explicitly addressed
