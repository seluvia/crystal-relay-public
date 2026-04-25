# AGENTS.md

## Project Identity
- Product name: `Crystal Relay`
- Legacy source/project name may still appear as `VrcTwitchOscBridge`
- Current release version: `v2.6.2`
- Platform: Windows desktop app
- Primary purpose: Twitch-to-OSC / OSCQuery control for VRChat

## Tech Stack
- Language: `C#`
- UI: `WPF` + `XAML`
- Runtime: `.NET`
- Scripts: `PowerShell`
- Supporting library: local OSCQuery library project in `oscquery-lib`

## Core Product Areas
- Twitch broadcaster login
- Optional Twitch bot login
- Twitch EventSub integration
- VRChat avatar login with 2FA
- Avatar cache and OSC parameter cache
- OSC / OSCQuery bridge and status monitoring
- Avatar Sets
- Avatar Change redeems
- Movement Redeems
- Bits + Subs override rules
- Twitch Chatbox window
- Theme system
- About page live-status cards
- Hidden easter eggs and media playback
- Crash logging and recovery cleanup

## Canonical Repository Paths
Use these locations only unless a change is explicitly required.

- Repo root:
  `C:\Users\screm\Documents\New project`
- Main app:
  `C:\Users\screm\Documents\New project\VrcTwitchOscBridge`
- OSCQuery library:
  `C:\Users\screm\Documents\New project\oscquery-lib`
- Solution:
  `C:\Users\screm\Documents\New project\VrcTwitchOscBridge.slnx`
- Release script:
  `C:\Users\screm\Documents\New project\Build-Crystal-Relay-Release.ps1`
- Test build script:
  `C:\Users\screm\Documents\New project\Build-Crystal-Relay-Test.ps1`
- Backup script:
  `C:\Users\screm\Documents\New project\Backup-Crystal-Relay-Project.ps1`
- Source launcher:
  `C:\Users\screm\Documents\New project\Run-Crystal-Relay-Source.ps1`
- Root docs:
  `C:\Users\screm\Documents\New project\README.md`
  `C:\Users\screm\Documents\New project\CHANGELOG.txt`

## Canonical Output Paths
Keep generated outputs in these folders only.

- Releases:
  `C:\Users\screm\Documents\New project\Releases`
- Test builds:
  `C:\Users\screm\Documents\New project\TestBuilds`
- Raw source backups:
  `C:\Users\screm\Documents\New project\Backups`
- Source review package:
  `C:\Users\screm\Documents\New project\Code Review`

## Runtime Data Paths
Do not store runtime data in the repo.

- App data root:
  `C:\Users\screm\AppData\Local\CrystalRelay`
- Crash logs:
  `C:\Users\screm\AppData\Local\CrystalRelay\CrashLogs`

## Naming Rules
- UI, release packages, docs, and user-facing text must use `Crystal Relay`
- Do not reintroduce `Void Hub` branding
- Legacy internal identifiers may remain if changing them would be noisy or risky

## Versioning Rules
- Version format: `major.minor.patch`
- Rollover rule is decimal-per-segment:
  - `1.0.9 -> 1.1.0`
  - `1.9.9 -> 2.0.0`
- Update version in project metadata before release packaging
- Update `CHANGELOG.txt` for every release

## Build and Release Rules
- Test build first when a change is visual, risky, or user-requested
- Release builds go in a version folder:
  - Example: `Releases\v2.6.2`
- Test builds go in a version folder:
  - Example: `TestBuilds\v2.6.2`
- Raw backups go in a version folder:
  - Example: `Backups\v2.6.2`
- Release zips should be kept; loose old release folders may be cleaned up if the user requests it
- Do not leave temporary developer-only controls in release builds
- Use `Build-Crystal-Relay-Test.ps1` for organized test packages
- Current test package layout should stay:
  - root shortcut: `Crystal Relay Test.lnk`
  - top-level docs: `README.md`, `CHANGELOG.txt`
  - runtime files inside `App`
- Release packages should stay flat at the top level so the real versioned `.exe` is visible without a launcher script

## Security Rules
- Do not write Twitch OAuth tokens, VRChat auth data, cookies, or secrets into repo files
- Do not expose secrets in `README.md`, `CHANGELOG.txt`, test data, or examples
- Keep account/session data under `AppData\Local\CrystalRelay`
- Crash logs may include stack traces but should not intentionally dump secrets

## UI Rules
- Preserve custom themed window chrome
- Preserve theme-specific fonts and theme-specific visuals
- Keep the interface centered, readable, and uncluttered
- Prefer stable layouts across all themes
- Avoid default Windows-looking controls when a custom themed version already exists
- Keep user-facing descriptions concise and user-friendly
- Use user-facing wording in docs and changelog, not dev-note wording

## Current Themes
- `Void Crystal`
- `Dream Scape`
- `Bubblegum`
- `Cosmic Puppy Girl`
- `Peaches & Cream`
- `Moon Bunny Wink`
- `Dread Night Bar`

## Current Stability Priorities
- Keep Rule Library cards from clipping in any theme
- Keep OSC / OSCQuery connection reliable
- Keep Twitch reward activation and cleanup predictable
- Keep VRChat avatar and parameter caches stable
- Keep Twitch Chatbox theme handling consistent
- Keep About-page live indicators accurate while the app stays open
- Keep managed reward color/state changes aligned with cooldown and disable-pairing logic
- Keep crash logs reliably written to disk

## Agent Workflow Rules
- Read existing scripts and services before adding new ones
- Reuse existing output folders; do not invent new storage roots
- Prefer minimal, targeted edits over broad renames
- Build after code changes that affect runtime behavior or XAML
- Refresh test package after test-build changes
- Update release package only after the change is confirmed good
- If a temporary test tool is added, remove it before release unless explicitly requested
- Keep only the newest test-build version unless the user explicitly asks to keep older test packages
- Keep changelog wording user-facing and generic for hidden easter eggs

## Do Not
- Do not create stray runtime files in the repo root
- Do not add new brand names without user approval
- Do not store user login/session data in source-controlled files
- Do not revert unrelated user changes
- Do not delete backups or release zips unless explicitly requested
