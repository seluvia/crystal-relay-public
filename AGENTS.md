# AGENTS.md

## Project Identity
- Product name: `Crystal Relay`
- Legacy source/project name may still appear as `VrcTwitchOscBridge`
- Last stable release: `v3.1.8`
- Current source version: `v3.1.8`
- Next post-release development version: `v3.1.9`
- Active development build: `v3.1.9`
- Active build lane: `none`
- Platform: Windows desktop app
- Primary purpose: Twitch-to-OSC / OSCQuery control for VRChat
- Public GitHub repo: `seluvia/crystal-relay-public`

## Tech Stack
- Language: `C#`
- UI: `WPF` + `XAML`
- Runtime target: `.NET 10` / `net10.0-windows`
- Runtime publish target: `win-x64`
- Scripts: `PowerShell`
- Supporting library: local OSCQuery library project in `oscquery-lib`
- Localization audit project: `LocalizationAudit`

## Core Product Areas
- Twitch broadcaster login through Crystal Relay's built-in Twitch app
- Optional Twitch bot login
- Twitch EventSub integration for channel points, chat commands, bits, subs, gift subs, follows, stream state, and chatbox messages
- Twitch Custom Power-up (Bits) Redeem Library: linked-existing and Crystal Relay-managed Power-up rules, Bits-paid triggers, avatar scope, fixed float add, OSC, Set Trigger, Avatar Change, Avatar Roulette, movement, and Avatar Scaling actions
- Twitch Power-up Bits count toward Reward Fire Sale progress when Bits counting is enabled, while linked Power-ups stay listen-only
- GitHub latest-release update notifications for `seluvia/crystal-relay-public`
- Dedicated self-updater helper and update package manifest validation
- VRChat login with 2FA
- VRChat avatar cache and OSC parameter cache
- LocalLow VRChat OSC avatar JSON scanning through `VrChatLocalOscCacheService`
- OSC / OSCQuery bridge and status monitoring
- Managed Twitch channel-point reward sync: create, adopt, update, disable/hide, delete when opted in, and stale cleanup
- Avatar Sets and grouped avatar-set redeems
- Avatar Change and Avatar Roulette redeems
- Movement Redeems, including jump pulse, Random Movement, and Glitchy Movement behavior
- Bits + Subs override rules with paid override priority
- Universal Triggers for commands, rewards, bits, subs, gift subs, and follows
- Fooma Twitch Interaction JSON import and command/reward fusion
- Avatar Scaling Scale Sets using VRChat OSC Avatar Scaling `/avatar/eyeheight`
- Avatar Scaling Master Reward, Supporter Growth, relative min/max limits, cooldowns, disable pairing, optional avatar-change blocker, Glitchy Random Height mode, and Bits/Subs Add for float supporter rules
- Cash Payments for StreamElements tips, Streamlabs donations, and Ko-fi hosted/local webhook payments
- Reward Fire Sale funding and temporary/permanent managed-reward discounts
- Desktop-mode stop-input soft lock and optional hard lock with emergency unlock
- Twitch Chatbox window with themed settings, emote rendering support, Activity + Moderation drawer, and suspicious/restricted chatter badges
- App-wide built-in/custom theme system with optional main-window custom background image
- About page live-status cards
- Cloudflare-backed live feedback heartbeat, bug report intake, hosted Ko-fi relay, and always-on World Guard
- Always-on World Guard service that fetches the shared guarded VRChat world/creator list from Cloudflare on startup and checks every `!world` lookup before sharing world info
- Hidden easter eggs and media playback
- Crash logging and recovery cleanup

## Canonical Repository Paths
Use these locations only unless a change is explicitly required.

- Repo root:
  `E:\!!!Program to work on\Proper Crystal Relay`
- Main app:
  `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge`
- OSCQuery library:
  `E:\!!!Program to work on\Proper Crystal Relay\oscquery-lib`
- Localization audit:
  `E:\!!!Program to work on\Proper Crystal Relay\LocalizationAudit`
- Dedicated updater:
  `E:\!!!Program to work on\Proper Crystal Relay\CrystalRelayUpdater`
- Solution:
  `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.slnx`
- Release script:
  `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Release.ps1`
- Beta release script:
  `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Beta.ps1`
- Test build script:
  `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Test.ps1`
- Raw source backup script:
  `E:\!!!Program to work on\Proper Crystal Relay\Backup-Crystal-Relay-Project.ps1`
- App-data backup script:
  `E:\!!!Program to work on\Proper Crystal Relay\Backup-Crystal-Relay-AppData.ps1`
- GitHub sync script:
  `E:\!!!Program to work on\Proper Crystal Relay\Sync-Crystal-Relay-GitHub-Repos.ps1`
- GitHub upload prep script:
  `E:\!!!Program to work on\Proper Crystal Relay\tools\github\Export-Crystal-Relay-Public.ps1`
- Public safety preflight:
  `E:\!!!Program to work on\Proper Crystal Relay\tools\github\Test-Crystal-Relay-PublicSafety.ps1`
- GitHub Desktop workflow:
  `E:\!!!Program to work on\Proper Crystal Relay\Open-Crystal-Relay-GitHub-Desktop-Workflow.ps1`
- Dependency vulnerability scan:
  `E:\!!!Program to work on\Proper Crystal Relay\Check-Crystal-Relay-Dependencies.ps1`
- Cloudflare workers:
  `E:\!!!Program to work on\Proper Crystal Relay\cloudflare`
- Dev tooling root:
  `E:\!!!Program to work on\Proper Crystal Relay\tools`
- Private local tooling:
  `E:\!!!Program to work on\Proper Crystal Relay\tools\private`
- Source launcher:
  `E:\!!!Program to work on\Proper Crystal Relay\Run-Crystal-Relay-Source.ps1`
- Release launcher:
  `E:\!!!Program to work on\Proper Crystal Relay\Launch-Crystal-Relay.bat`
- Root docs:
  `E:\!!!Program to work on\Proper Crystal Relay\README.md`
  `E:\!!!Program to work on\Proper Crystal Relay\CHANGELOG.txt`
  `E:\!!!Program to work on\Proper Crystal Relay\RELEASE-CHANGE-RECORD.txt`
  `E:\!!!Program to work on\Proper Crystal Relay\TRANSLATING.md`

## GitHub Working Copies
- Private repo folder:
  `C:\Users\screm\Documents\GitHub\crystal-relay-private`
- Public repo folder:
  `C:\Users\screm\Documents\GitHub\crystal-relay-public`
- For publish/release pushes, update and verify the private repo first, then update the public repo only after the private copy is confirmed good.
- When pushing a public release build, upload the release package/file with the public GitHub release after the public repo update is verified.
- Public sync must not include secrets, private workflow notes, app data, tokens, auth cookies, or local runtime state.
- `Sync-Crystal-Relay-GitHub-Repos.ps1` calls the public export script directly:
  `E:\!!!Program to work on\Proper Crystal Relay\tools\github\Export-Crystal-Relay-Public.ps1`

## Canonical Output Paths
Keep generated outputs in these folders only.

- Releases:
  `E:\!!!Program to work on\Proper Crystal Relay\Releases`
- Test builds:
  `E:\!!!Program to work on\Proper Crystal Relay\TestBuilds`
- Raw source backups:
  `E:\!!!Program to work on\Proper Crystal Relay\Backups`
- Test-only raw backups:
  `E:\!!!Program to work on\Proper Crystal Relay\Backups\Test`
- Source review / GitHub upload staging:
  `E:\!!!Program to work on\Proper Crystal Relay\Code Review`

## Runtime Data Paths
Do not store runtime data in the repo.

- App data root:
  `C:\Users\screm\AppData\Local\CrystalRelay`
- Portable save / transfer folder:
  `C:\Users\screm\AppData\Local\CrystalRelay\Crystal Relay Save Transfer`
- Custom theme assets:
  `C:\Users\screm\AppData\Local\CrystalRelay\Crystal Relay Save Transfer\ThemeAssets`
- Secure metadata folder:
  `C:\Users\screm\AppData\Local\CrystalRelay\Secure`
- Crash logs:
  `C:\Users\screm\AppData\Local\CrystalRelay\CrashLogs`
- Update staging:
  `C:\Users\screm\AppData\Local\CrystalRelay\Updates`
- Update backups:
  `C:\Users\screm\AppData\Local\CrystalRelay\UpdateBackups`
- Runtime config:
  `C:\Users\screm\AppData\Local\CrystalRelay\bridge.runtime.json`
- Saved-login recovery backups:
  `C:\Users\screm\AppData\Local\CrystalRelay-RecoveryBackups`
- Legacy app data may still be migrated from:
  `C:\Users\screm\AppData\Local\VrcTwitchOscBridge`

## Security Rules
- Twitch OAuth tokens and VRChat auth cookies are stored in Windows Credential Manager through `WindowsCredentialStore`.
- StreamElements, Streamlabs, and Ko-fi payment secrets are stored in Windows Credential Manager through `SettingsStore` / secure credential helpers, not in portable profile files.
- Secure metadata, avatar cache, and OSC parameter cache live under `AppData\Local\CrystalRelay\Secure`.
- Never write Twitch OAuth tokens, VRChat auth data, cookies, or secrets into repo files.
- Never expose secrets in `README.md`, `CHANGELOG.txt`, release notes, examples, test data, or public repo files.
- Cloudflare worker secrets, GitHub issue tokens, Ko-fi relay client secrets, and hosted relay credentials must stay in Cloudflare secrets or local credentials and must never be committed.
- Do not copy user-local files such as downloaded configs, LocalLow avatar JSON files, screenshots, or videos into the repo unless the user explicitly asks and confirms they are public-safe.
- VRChat LocalLow OSC JSON files are read-only input. Do not delete, move, or modify them from Crystal Relay code.

## Naming Rules
- UI, release packages, docs, and user-facing text must use `Crystal Relay`.
- Do not reintroduce `Void Hub` branding.
- Legacy internal identifiers such as `VrcTwitchOscBridge` may remain if changing them would be noisy or risky.
- Keep public-facing docs factual, streamer-friendly, and free of internal workflow notes.

## Versioning Rules
- Version format: `major.minor.patch`.
- Rollover rule is decimal-per-segment:
  - `1.0.9 -> 1.1.0`
  - `1.9.9 -> 2.0.0`
- Before choosing a test-build version, check all three sources:
  - `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` `<Version>`
  - highest semantic version folder under `Releases`
  - highest semantic version folder under `TestBuilds`
- Sort version folders by semantic version, not by folder modified time. A freshly rebuilt older test package can have a newer timestamp.
- If the current stable release version is the same as the latest test-build version, treat active development as the next patch version above stable unless the user explicitly asks to rebuild that exact existing test package.
- After a stable release is done, any new code change should move active development to the next patch version before making a test or beta build. Example: after stable `3.1.5`, the next changed build is `3.1.6`.
- Do not keep producing new changed test/beta packages under the last stable version unless the user explicitly asks to rebuild that exact stable package.
- Use the new post-release version for either test or beta builds depending on what the user asks: test builds stay in `TestBuilds\v<version>`, beta builds use the beta script and `-beta<N>` suffix.
- Before running any test or beta build, update this file so `Active development build` and `Active build lane` describe exactly what is about to be built. Keep those fields current until the user asks for a full release, new release build, or equivalent release-promotion wording.
- If the user asks for more edits while an active test/beta build is in progress, keep using that active development version unless the user explicitly asks to start a newer version.
- After the user confirms a test or beta build is good and asks to release it, promote that active version to the new stable release, then update `Last stable release`, `Current source version`, and `Next post-release development version` in this file.
- After a full release is published, reset `Active build lane` to `none` and set `Active development build` to the next patch version that future test/beta work should use.
- When reporting a completed test, beta, or release build, include a short reminder of the last stable release and the active/new build version. Example: `Last stable: 3.1.5; active test/beta: 3.1.6`.
- If `AGENTS.md` disagrees with the project file or latest semantic test-build folder, trust the project file and version folders, then update `AGENTS.md` as part of the housekeeping.
- Update version in `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` before release packaging.
- Update `CHANGELOG.txt` for official releases.
- Use `RELEASE-CHANGE-RECORD.txt` for in-progress release notes before finalizing `CHANGELOG.txt`.
- Follow the Changelog and Release Notes Workflow below for what goes into beta vs. stable entries and how betas roll up into the next stable.

## Changelog and Release Notes Workflow

Two files drive release notes:
- `CHANGELOG.txt` (public): the final user-facing changelog that ships in every package and mirrors the public GitHub release notes.
- `RELEASE-CHANGE-RECORD.txt` (private/internal): a working scratchpad for in-progress release notes and the release checklist. Do not sync it to the public repo.

### Beta cycle
- During each beta, append a new section to `CHANGELOG.txt` at the top using the format:
  `v<version> beta <N>`
  followed by short, user-facing bullet points describing what changed since the previous beta.
- Keep the previous `v<version> beta N` entries in `CHANGELOG.txt` as a public record. Do not rewrite or delete them while the beta cycle is in progress.
- Beta notes focus on what a beta tester needs to know: new features, behavior changes, and any known rough edges. Drop dev-only wording, internal workflow notes, and references to internal repo names or tooling.
- Mirror each beta's items into `RELEASE-CHANGE-RECORD.txt` under `Added` / `Changed` / `Removed` so the next stable cut can pull from a single internal source.
- Beta release packages and beta GitHub release entries reuse the matching `v<version> beta <N>` section from `CHANGELOG.txt` as their release notes. Do not publish a separate "what's new" doc for betas.

### Stable release rollup
- The stable release is the moment the beta cycle closes. Add a single new `v<version>` section to `CHANGELOG.txt` at the top, above all `v<version> beta N` entries for the same version.
- The stable entry is a fresh, user-facing summary of the full cycle. It is not a verbatim copy of the beta notes:
  - Include every user-visible addition, change, and fix from the betas that is still in the final build.
  - Drop dev-only wording, internal workflow notes, and items that did not survive the beta cycle.
  - Rewrite for a streamer who has been on the last stable release, not for a beta tester.
- The full set of `v<version> beta N` entries stays in `CHANGELOG.txt` above the stable entry as a public record of the beta cycle. Do not delete or rewrite the beta entries when the stable ships.
- Update `RELEASE-CHANGE-RECORD.txt` alongside the stable cut:
  - Move the final user-facing bullets into the "Pending Release Draft" section as the canonical source for that version.
  - Update the "Current Baseline" block (`Last published version` and `Current working source version`) to match the new stable.
  - Clear the draft sections that are now represented by the stable entry, but keep the release checklist.

### GitHub release publication
- Public GitHub release notes mirror the matching `CHANGELOG.txt` entry. For a beta, use the `v<version> beta <N>` section. For a stable, use the `v<version>` section.
- Do not paste internal workflow notes, dev-only wording, or `RELEASE-CHANGE-RECORD.txt` content into GitHub release notes.
- Asset names on the public GitHub release must follow the existing patterns:
  - Stable: `CrystalRelayTwitchOsc-v<version>-win-x64.zip`
  - Beta:   `CrystalRelayTwitchOsc-v<version>-beta<N>-win-x64.zip`

### README highlights
- Update the `Current Release Highlights` section of `README.md` to mirror the new stable entry. Beta releases do not normally update the README highlights; only the most recent stable release is highlighted.
- Public-safe wording only. No internal repo names, tokens, or workflow notes.

### Housekeeping at release time
- When promoting an active test/beta build to stable, update these together:
  - `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` (and the updater project) `<Version>` is already at the target version.
  - `CHANGELOG.txt` has a new `v<version>` entry above the `v<version> beta N` entries.
  - `RELEASE-CHANGE-RECORD.txt` baseline and draft sections are updated.
  - `README.md` `Current Release Highlights` reflects the new stable.
  - `AGENTS.md` "Project Identity" updates `Last stable release`, `Current source version`, and `Next post-release development version`, and resets `Active build lane` to `none` with `Active development build` set to the next patch version.

## Build and Release Rules
- Test build first when a change is visual, risky, runtime-affecting, or user-requested.
- All three build scripts enforce pre-flight gates before publishing. A build refuses to run if:
  - `CHANGELOG.txt` does not contain the expected `v<version>` section (release), `v<version> beta <N>` section (beta), or either form (test).
  - `RELEASE-CHANGE-RECORD.txt` "Current working source version" does not match the csproj (warning for test/beta, throw for release).
  - The working tree has uncommitted changes (opt-out: `CR_SKIP_GIT_CHECK=1`).
  - A `Remove-Item -Recurse -Force` targets a path that is not a Crystal Relay-shaped package path under the expected parent. Guarded by `Assert-SafeBuildPath` in each script.
  Set `CR_SKIP_GIT_CHECK=1` to override the working tree check, but never disable the changelog or record gates.
- Release builds go in a version folder:
  - Example: `Releases\v2.9.2`
- Beta builds for the same stable version go in the same version folder, alongside the eventual stable. Example: `Releases\v3.1.8\` holds `CrystalRelayTwitchOsc-v3.1.8-beta1-win-x64`, `CrystalRelayTwitchOsc-v3.1.8-beta2-win-x64`, and the eventual `CrystalRelayTwitchOsc-v3.1.8-win-x64` stable.
- Test builds go in a version folder:
  - Example: `TestBuilds\v2.9.2`
- Raw backups go in a version folder:
  - Example: `Backups\v2.9.2`
- Test backup lane uses:
  - `Backups\Test\v2.9.2`
- Use `Build-Crystal-Relay-Test.ps1` for organized test packages.
- Use `Build-Crystal-Relay-Release.ps1` for official release packages.
- Use `Build-Crystal-Relay-Beta.ps1` for beta packages; beta packages use a `-beta<N>` version suffix, prerelease channel manifest, and `beta-build.flag`.
- Release, beta, and test build scripts run the localization audit before publishing.
- Release and beta packages should include `CrystalRelayUpdater.exe` and a valid `crystal-relay-update.json` manifest.
- Test packages should include `CrystalRelayUpdater.exe` and `test-build.flag`; do not treat test packages as public self-update assets unless the user explicitly asks.
- Current test package layout should stay:
  - root shortcut: `Crystal Relay Test.lnk`
  - top-level docs: `README.md`, `CHANGELOG.txt`, optional `docs`
  - runtime files inside `App`
  - `test-build.flag` and `crystal-relay-update.json` live inside `App`
  - `crystal-relay-update.json` uses `channel: test` and `entryExecutableName: CrystalRelayTwitchOsc.exe`
  - test packages are multi-file (no `PublishSingleFile`); they are not zipped
- Release packages should stay flat at the top level so the real versioned `.exe` is visible without a launcher script.
  - Release packages use `PublishSingleFile=true` for the main app and are zipped
  - `crystal-relay-update.json` lives at the package root with `channel: stable` and `entryExecutableName: CrystalRelayTwitchOsc-v<version>.exe`
- Beta packages mirror the release layout but append `-beta<N>` to the version, the executable name, and the JSON `version` field, use `channel: beta`, and add a `beta-build.flag` at the package root.
- Stable release ZIP assets must keep the self-update naming pattern:
  `CrystalRelayTwitchOsc-v<version>-win-x64.zip`
- Beta release ZIP assets must keep the beta naming pattern:
  `CrystalRelayTwitchOsc-v<version>-beta<N>-win-x64.zip`
- The release script accepts `-Bump major|minor|patch|mid|small` (`mid` is an alias for `minor`, `small` is an alias for `patch`) or `-Version <ver>` to set the target version explicitly. The beta and test scripts accept `-Version <ver>`. When `-Version` is not supplied, the scripts normalize the version already in the project file.
- All three build scripts rewrite the `<Version>` / `<AssemblyVersion>` / `<FileVersion>` / `<InformationalVersion>` fields of `VrcTwitchOscBridge.csproj` and `CrystalRelayUpdater.csproj` in place so the project file stays the single source of truth.
- Do not change update asset names, manifest fields, or ZIP layout without updating `ApplicationUpdateService`, `ApplicationSelfUpdateService`, and `CrystalRelayUpdater` together.
- Release zips should be kept; loose old release folders may be cleaned up only if the user requests it.
- Do not leave temporary developer-only controls in release builds.
- `Check-Crystal-Relay-Dependencies.ps1` runs `dotnet list package --vulnerable --include-transitive` against every reachable `*.csproj` (excluding `Backups`, `Releases`, `TestBuilds`, `Code Review`, and `temp-build`) to flag known vulnerable NuGet packages before release prep.

## Project File Rules
- `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` has default item inclusion disabled:
  - `EnableDefaultItems=false`
  - `EnableDefaultCompileItems=false`
  - `EnableDefaultApplicationDefinition=false`
  - `EnableDefaultPageItems=false`
- New `.cs`, `.xaml`, resources, windows, and related project files must be explicitly included in the app project file or they will not build/package. `App.xaml` must be listed under `<ApplicationDefinition>` and every other XAML window/page under `<Page>`.
- The app project enables `<UseWPF>true</UseWPF>` for the WPF UI and `<UseWindowsForms>true</UseWindowsForms>` for the low-level Windows input hooks used by `DesktopInputLockService` and `Launch-Crystal-Relay.bat`-style helpers. Do not flip either one off without checking the desktop stop-input flow.
- `VrcTwitchOscBridge.slnx` is not a reliable validation target right now; build the app project directly unless the solution file is intentionally fixed.
- The app references the vendored OSCQuery library from `oscquery-lib` with a `net6.0` target selection. Preserve that compatibility unless the whole app/library relationship is being updated.
- If `oscquery-lib` is changed, run its tests in addition to the app build.

## Self-Update Rules
- The updater is a separate executable: `CrystalRelayUpdater.exe`.
- Update packages must include exactly one visible versioned Crystal Relay executable matching `CrystalRelayTwitchOsc-v*.exe`.
- Update packages must include `crystal-relay-update.json` with the expected product, runtime, channel, version, and entry executable.
- Stable self-update expects GitHub release assets named `CrystalRelayTwitchOsc-v<LatestVersion>-win-x64.zip`.
- The app validates update downloads through HTTPS GitHub asset URLs, SHA-256 digests, safe ZIP extraction, package manifest checks, and dedicated-updater presence.
- Keep updater path-safety checks strict. The updater must not apply packages into filesystem roots, AppData runtime folders, or targets outside the expected package parent.
- `AppDataPaths.UpdatesFolder` and `AppDataPaths.UpdateBackupsFolder` are updater-owned; portable saves, secure metadata, crash logs, and runtime config are not updater cleanup targets.

## Public Export Rules
- Public sync/export runs through:
  `E:\!!!Program to work on\Proper Crystal Relay\tools\github\Export-Crystal-Relay-Public.ps1`
- Public safety preflight runs through:
  `E:\!!!Program to work on\Proper Crystal Relay\tools\github\Test-Crystal-Relay-PublicSafety.ps1`
- Public export intentionally excludes private/local folders such as `cloudflare`, `tools/private`, `Releases`, `TestBuilds`, `Backups`, `Code Review`, `.appdata`, `.codex`, local runtime state, and `AGENTS.md`.
- The internal release scratchpad `RELEASE-CHANGE-RECORD.txt` is private and must not be synced to the public repo. Even if the working source copy tracks it, do not commit it into the public export.
- Do not weaken blocked-path or blocked-content checks without explicit user approval.
- Public export must stay free of local paths, private repo names, Codex/OpenAI/internal workflow wording, tokens, secrets, credentials, and runtime state.

## Backup Rules
- Run `Backup-Crystal-Relay-Project.ps1` before broad runtime changes, save-format changes, risky refactors, or release prep when the user asks for a raw backup.
- Use normal raw-source backups for real restore points.
- Use `-TestBackup` only for test-only safeguards that must not touch the normal restore lane.
- Do not delete backups unless the user explicitly requests it.
- `Backup-Crystal-Relay-AppData.ps1` is the separate local app-data backup lane. It zips `%LOCALAPPDATA%\CrystalRelay` (settings, runtime config, avatar cache, OSC parameter cache, save-transfer files, crash logs, recovery files) into `Backups\v<ver>\...-appdata-<timestamp>.zip`, with a `BACKUP-NOTES.txt` that lists what was and was not included. App-data backups must not be synced to the public repo.

## Current Themes
- `Void Crystal`
- `Custom`
- `Dream Scape`
- `Bubblegum`
- `Cosmic Puppy Girl`
- `Peaches & Cream`
- `Moon Bunny Wink`
- `Dread Night Bar`
- `Baked`
- `MainFrame`
- `Trash Kitty`
- `Carrot Patch`
- `Treetender's Arm`
- `Bratwurst`
- `Neon Borb`
- `Stinky Online`

## Theme System Notes
- Built-in themes resolve through `ThemeManager` and `ThemePaletteFactory`.
- `Custom` is one app-wide custom theme slot with editable palette fields and installed Windows font families.
- Custom main-window background images are copied into `AppDataPaths.ThemeAssetsFolder` and saved as relative paths.
- Custom background images apply only to the main window.
- Existing themed secondary windows should use the shared palette/applicator flow where possible.

## Managed Reward Rules
- Crystal Relay-owned rewards use the `VRC:` presentation prefix.
- Managed rewards may be created, adopted, updated, disabled/hidden, deleted when opted in, or recycled.
- `Delete Twitch reward when inactive` defaults off and must remain opt-in.
- Do not delete rewards just because Twitch permissions are missing or Twitch reward management is unavailable.
- Cooldown and temporary in-use states should not churn reward deletion.
- Bits + Subs override avatar changes have paid priority and must not be blocked by Avatar Scaling's optional avatar-change blocker.

## Reward Fire Sale Rules
- Reward Fire Sale discounts only mutate Crystal Relay-owned `CreateOrManage` Twitch rewards.
- Linked existing Twitch rewards remain listen-only during fire sales; do not discount, rename, hide, delete, or otherwise mutate them.
- Fire Sale funding rewards are managed rewards and should follow the same Twitch API gating, coalescing, and opt-in delete rules as other Crystal Relay-owned rewards.
- Twitch Power-up Bits can also feed Fire Sale progress when Bits counting is enabled, while linked Power-ups stay listen-only and are never discounted, repriced, hidden, deleted, or otherwise mutated by the sale.
- Starting, stopping, expiring, or stream-end resetting a fire sale should queue managed reward sync only when Twitch-visible prices actually need to change.
- Always restore normal reward prices when a fire sale ends, expires, or is reset by stream-end handling.

## Twitch API Safety Rules
- Treat Twitch API rate limits and ownership restrictions as hard product constraints when editing Crystal Relay.
- Prefer EventSub/redemption events over polling. Do not add reward polling loops or repeated catalog refreshes for normal runtime actions.
- Coalesce, debounce, and fingerprint managed reward sync work before calling Twitch. Passive runtime events such as timed resets, local active state, scale status, or unchanged cooldown/lockout state must not call `Get Custom Rewards` or patch rewards unless the desired Twitch-visible state actually changed.
- Respect Twitch `429` responses, `Retry-After`, and `Ratelimit-Reset`. Back off until Twitch says it is safe instead of retrying in a tight loop.
- Do not call reward-management APIs when the broadcaster token is missing `channel:manage:redemptions`; show reconnect/permission guidance and preserve saved reward IDs.
- Linked existing Twitch rewards are listen-only in Crystal Relay. Do not rename, recolor, recost, cooldown-edit, hide, delete, or recreate linked rewards. Use their redemption events only, and read catalog data only for display/help text when available.
- Only Crystal Relay-owned `CreateOrManage` rewards may be created, updated, disabled/hidden, recycled, or deleted, and delete remains opt-in.
- Do not recreate rewards because of casing, whitespace, ordering, prompt text, startup timing, or transient catalog failures. Prefer stable Twitch reward IDs, then normalized title fallback only when necessary.
- Keep Twitch API diagnostics throttled and safe: log sync reason, skipped unchanged syncs, API call counts, and rate-limit backoff, but never log OAuth tokens, auth headers, cookies, or secrets.
- When changing Twitch API/reward sync behavior, verify that reward IDs are preserved, linked rewards remain listen-only, API calls are gated, and build/localization checks still pass.

## Twitch API Limitation Rules
- Treat Twitch API limits as a hard product constraint, not an afterthought.
- Do not add reward-sync behavior that repeatedly calls Twitch APIs for normal runtime events unless Twitch-visible reward state truly changed.
- Coalesce, debounce, or skip redundant reward sync requests before calling Twitch.
- Respect Twitch `429 Too Many Requests` responses and pause API calls using `Retry-After` or `Ratelimit-Reset` when available.
- Keep linked existing Twitch rewards listen-only. Crystal Relay may read linked reward metadata and listen for redemptions, but must not rename, recolor, delete, hide, edit cooldowns, edit prompts, or otherwise mutate linked rewards.
- Only Crystal Relay-owned / app-managed rewards may be created, updated, hidden, cooldown-synced, or deleted through Twitch APIs.
- Do not depend on Twitch error message text for behavior when a status code or typed response is available.
- Avoid full reward-catalog refreshes as a side effect of tests, local timers, active-time resets, OSC sends, or UI selection changes.
- Manual refresh, broadcaster reconnect, startup recovery, emergency stop, test mode changes, current-avatar changes, and user-edited reward settings may still trigger reward sync when needed.
- Add throttled diagnostics for reward API decisions when debugging, but never log OAuth tokens, authorization headers, cookies, or private account secrets.

## Cash Payment Rules
- Cash Payment rules support StreamElements tips, Streamlabs donations, and Ko-fi payments.
- Cash payment secrets, access tokens, verification tokens, and client secrets must stay in Windows Credential Manager or Cloudflare/local secret storage, never in portable saves or repo files.
- Ko-fi can use the hosted relay or a local webhook listener. Keep both paths explicit and do not assume one replaces the other.
- The hosted Ko-fi relay default is:
  `https://crystal-relay-kofi-relay.screminpal-animation.workers.dev`
- Cash Payment rules can trigger direct OSC actions, avatar changes, avatar set behavior, and avatar scaling behavior, but they do not create Twitch channel-point rewards.
- Do not add currency conversion behavior unless it is implemented deliberately and documented in the UI.
- Sanitize and throttle diagnostics for payment listeners; never log raw secrets, auth headers, client secrets, tokens, or private payment payloads.

## Movement Redeem Rules
- Movement Redeems are global behavior and not tied to one VRChat avatar unless the existing model explicitly does so.
- Movement Sets are organization-only folders; runtime movement behavior should flatten or resolve the contained rules consistently.
- Supported movement directions include Forward, Backward, Left, Right, Jump, SpinLeft, SpinRight, Stop Movement, Stop Turning, Stop All, Random Movement (one direction per trigger), and Glitchy Movement (continuously rolls random movement directions until Active Time ends).
- Stop Movement, Stop Turning, and Stop All behavior must preserve soft-lock behavior and optional desktop hard-lock behavior.
- Desktop hard lock uses low-level Windows input hooks only while the hard lock is active.
- Preserve the emergency unlock path and user-facing display:
  `Ctrl+Alt+Shift+F12`
- Stop-input cleanup should preserve the key-up release burst so movement and turning do not remain stuck.

## Universal Trigger Rules
- Universal Triggers are separate from Avatar Sets, Avatar Change, Movement Redeems, and Bits + Subs overrides.
- Supported Universal trigger types:
  - Chat Command
  - Channel Point Reward
  - Bits
  - Subscription
  - Gift Subscription
  - Follow
- Fooma import must append/import configs without copying downloaded config files into the repo.
- Matching command/reward pairs may be fused so the channel-point reward owns the optional chat-command fallback.
- Universal reward IDs are persisted internally but should not be the normal setup focus in the UI.
- Universal rewards can be managed by Twitch like Avatar Sets, but their runtime action list stays direct OSC.

## Avatar Scaling Rules
- Avatar Scaling is a separate Redeem Library section.
- Scale Sets are organization-only folders; runtime behavior flattens all scale rules.
- Write endpoint for scaling is `/avatar/eyeheight`.
- Read/status endpoints include:
  - `/avatar/eyeheight`
  - `/avatar/eyeheightmin`
  - `/avatar/eyeheightmax`
  - `/avatar/eyeheightscalingallowed`
- Supported scale trigger types:
  - Channel Point Reward
  - Chat Command
  - Bits
  - Subscription
  - Gift Subscription
  - Follow
  - Supporter Growth
- Supported one-shot scale modes:
  - Set Height
  - Random Height
  - Relative Height
  - Multiplier
  - Preset
  - Glitchy Random Height (rapidly varies height between configured minimum and maximum for the active time, then restores to the configured height)
- Supporter Growth is event-driven by bits/subs/gift subs and does not create a channel-point reward.
- Bits/Subs Add is a float-only supporter rule option that adds an amount-based value to the current float avatar parameter and clamps the result to a configured maximum, so paid support can grow an existing float without resetting or replacing it.
- Avatar Scaling Master Reward can temporarily unlock scale channel-point rewards to reduce Twitch reward-slot clutter.
- `Free child reward slots while locked` defaults on for the master reward.
- Avatar Scaling has its own cooldowns and disable-pairing behavior.
- Optional `Prevent avatar-change rewards while scaling is active` blocks normal Avatar Change / Avatar Roulette reward actions only; Bits + Subs override avatar changes bypass it.

## Twitch Chatbox Rules
- Keep Twitch Chatbox UI compact, readable, and themed.
- Chatbox text-only, mixed text/emote, native Twitch emote, and third-party emote paths must remain safe.
- Failed emote image loads should fall back to readable text.
- VRChat relay text must not be changed just to support visual chatbox rendering.
- Chatbox message cap is `250`; activity log cap is `200`.
- Custom role-card behavior is split between identity/classification/name-brush logic in `MainWindowViewModel.cs` and visual resources/triggers in `TwitchChatboxWindow.xaml`.
- When adding or editing a Twitch role card, update all matching surfaces together: main card, rail, badge, muted text, name text, and inset panels.
- The Twitch Chatbox includes an Activity + Moderation drawer with recent Twitch activity, quick timeouts, ban, purge, and message delete, plus suspicious-user controls when the broadcaster account has the required Twitch scopes.
- Suspicious and restricted chatter badges show alongside chat and activity entries for follows, support events, rewards, chat clears, deleted messages, and moderation results.

## UI Rules
- Preserve custom themed window chrome.
- Preserve theme-specific fonts and theme-specific visuals.
- Avoid default Windows-looking controls when a custom themed version already exists.
- Keep layouts centered, readable, and uncluttered.
- Prefer compact cards, grouped/collapsible sections, and workspace editors over long flat lists.
- Avoid clipping in small windows and across longer localized labels.
- Main UI scrollbars should keep clean gutter spacing from cards.
- Use user-facing wording in docs, tooltips, changelog, and activity logs.
- Add localization entries for new UI text and run the localization audit.

## Localization Rules
- User-facing UI text should use the existing localization flow instead of hardcoded XAML or code strings where practical.
- Localization files can use base `.json` files plus matching `.extra.json` files; the audit merges both.
- Add new `en-US` source keys for new UI text and keep placeholder names consistent across languages.
- Avoid empty localized values and accidental English copies in translated files unless the existing pattern clearly allows it.
- Run the localization audit after adding or changing UI text, XAML labels, tooltips, or user-facing activity messages.
- The world guard message `"This world is currently guarded. Reason: {0}"` is used in `BridgeCoordinator.cs` and must be present in all `.extra.json` localization files.

## Localization Translation Quality Rules
- All non-English translations must sound natural and conversational in the target language, not stiff or machine-translated.
- Use informal/friendly register across all languages: `du` for de-DE, `tú` for es-ES, `tu` for fr-FR, informal equivalents for other languages.
- Keep brand and technical terms in English across all languages: `Bits`, `Subs`, `OSC`, `OSCQuery`, `VRChat`, `Twitch`, `Crystal Relay`, `StreamElements`, `Streamlabs`, `Ko-fi`.
- Do not translate product names, feature brand terms, or UI identifiers that are always displayed in English (e.g., `VRC:` prefix, `!world`, `Cheer`).
- Preserve all format placeholders exactly: `{0}`, `{1}`, `{2}`, `{0:N0}`, `{0:0.##}`, etc. Never modify, reorder, or rename placeholders.
- Use terminology consistent within each language. Pick one natural term for recurring concepts (e.g., "redeem", "override", "avatar set") and stick to it across every key in that language file.
- For gaming/streaming vocabulary, use the natural terms that native speakers in that language community actually use, not literal translations.
- Translations should feel like a native speaker wrote them for a friendly desktop app, not like a translation exercise.
- When new UI text is added to `en-US`, translate it into all non-English languages before merging. Do not leave untranslated English values in non-English files unless the key is a brand name or technical term that stays in English by design.
- After any translation change, run the localization audit to verify key coverage, placeholder integrity, and no empty values.

## Cloudflare And Private Tooling Rules
- Cloudflare worker folders are private infrastructure and are excluded from public export by default.
- The live feedback worker should only handle temporary live heartbeat/status entries and should not store Twitch OAuth data, VRChat credentials, OSC payloads, chat messages, or permanent live state.
- The bug report worker creates GitHub issues for `seluvia/crystal-relay-public`; its GitHub token must stay in Cloudflare secrets.
- The Ko-fi relay worker should relay hosted Ko-fi events without storing payment history, emails, messages, tokens, client secrets, or raw payloads.
- The world guard worker maintains a shared list of guarded VRChat worlds/users. The desktop app checks that guard on every `!world` lookup before sharing world info. The worker endpoints are hardcoded in `WorldCommandBlacklistSettings.cs` and the service is always-on.
- `tools/private` is local/private tooling. Do not sync it to the public repo or depend on it for public builds.
- When the user says `dev tool`, treat the work as scoped to `E:\!!!Program to work on\Proper Crystal Relay\tools` by default. Do not modify the main program, updater, release scripts, or public docs unless that change is required to make the dev tool work.
- If a dev-tool request truly needs a main-program change, explain why, keep the edit minimal, and verify the main app still builds without changing user-facing runtime behavior beyond what the user requested.

## Saved Login Recovery Rules
- Saved-login recovery may quarantine app data and clear Credential Manager secrets to recover from broken login/session state.
- Recovery should preserve portable redeem saves, save backups, and theme assets when possible.
- Recovery must not restore Twitch OAuth tokens, VRChat auth cookies, secure metadata, avatar cache, OSC parameter cache, or other credential-derived state.
- Keep recovery result files and diagnostics sanitized before showing or syncing anything publicly.

## Current Stability Priorities
- Keep Twitch reward activation, visibility, cooldown color, delete-when-inactive behavior, and cleanup predictable.
- Keep Avatar Sets and Avatar Change correctly tied to the current VRChat avatar.
- Keep Bits + Subs override priority independent from normal reward blockers.
- Keep Avatar Scaling transitions, cooldowns, disable pairing, master reward gating, and height carryover stable.
- Keep Universal Triggers import, command fusion, reward sync, and direct OSC execution stable.
- Keep Cash Payment listener behavior, secret storage, hosted Ko-fi relay, and payment-trigger execution stable.
- Keep Reward Fire Sale discounts limited to managed rewards and restore prices reliably, including Power-up Bits progress and listen-only linked Power-ups.
- Keep Power Up Redeem rules, link-existing vs create-or-managed, Bits amounts, avatar scope, fixed float add, and reward sync behavior stable.
- Keep desktop stop-input locks and emergency unlock behavior reliable.
- Keep OSC / OSCQuery connection reliable.
- Keep VRChat avatar and parameter caches stable.
- Keep Twitch Chatbox emote rendering, Activity + Moderation drawer, and suspicious/restricted chatter badges reliable.
- Keep About-page live indicators accurate while the app stays open.
- Keep self-update package discovery, updater launch, manifest validation, and update cleanup reliable.
- Keep crash logs reliably written to disk.
- Keep the always-on World Guard service reachable from Cloudflare on startup and per-lookup, and keep the `!world` command guard check predictable.

## Agent Workflow Rules
- Read existing scripts and services before adding new ones.
- Reuse existing output folders; do not invent new storage roots.
- Prefer minimal, targeted edits over broad renames.
- Use `apply_patch` for manual file edits.
- Do not revert unrelated user changes.
- Build after code changes that affect runtime behavior or XAML:
  `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
- Refresh a test package after test-build changes, using the active development build discovered from the Versioning Rules:
  `powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Test.ps1" -Version <active-version>`
- Before running a test or beta build script, update `AGENTS.md` first so the active build version and lane are recorded.
- Update release packages only after the change is confirmed good.
- If a temporary test tool is added, remove it before release unless explicitly requested.
- Keep changelog wording user-facing and generic for hidden easter eggs.
- When preparing public repo updates, run the sync script and inspect the public sync output for private information before pushing.
- For public export or release publication, run the public safety preflight and inspect blocked-content output before pushing or uploading.

## Do Not
- Do not create stray runtime files in the repo root.
- Do not add new brand names without user approval.
- Do not store user login/session data in source-controlled files.
- Do not revert unrelated user changes.
- Do not delete backups or release zips unless explicitly requested.
- Do not commit large videos directly into source history; use GitHub Release assets when needed.
- Do not modify VRChat LocalLow OSC JSON files.
- Do not move update staging, update backups, secure metadata, or saved-login recovery data into the repo.
