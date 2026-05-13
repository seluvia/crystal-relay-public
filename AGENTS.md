# AGENTS.md

## Project Identity
- Product name: `Crystal Relay`
- Legacy source/project name may still appear as `VrcTwitchOscBridge`
- Current source version: `v3.1.3`
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
- GitHub latest-release update notifications for `seluvia/crystal-relay-public`
- VRChat login with 2FA
- VRChat avatar cache and OSC parameter cache
- LocalLow VRChat OSC avatar JSON scanning through `VrChatLocalOscCacheService`
- OSC / OSCQuery bridge and status monitoring
- Managed Twitch channel-point reward sync: create, adopt, update, disable/hide, delete when opted in, and stale cleanup
- Avatar Sets and grouped avatar-set redeems
- Avatar Change and Avatar Roulette redeems
- Movement Redeems, including jump pulse behavior
- Bits + Subs override rules with paid override priority
- Universal Triggers for commands, rewards, bits, subs, gift subs, and follows
- Fooma Twitch Interaction JSON import and command/reward fusion
- Avatar Scaling Scale Sets using VRChat OSC Avatar Scaling `/avatar/eyeheight`
- Avatar Scaling Master Reward, Supporter Growth, relative min/max limits, cooldowns, disable pairing, and optional avatar-change blocker
- Twitch Chatbox window with themed settings and emote rendering support
- App-wide built-in/custom theme system with optional main-window custom background image
- About page live-status cards
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
- Solution:
  `E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge.slnx`
- Release script:
  `E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Release.ps1`
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
- Source launcher:
  `E:\!!!Program to work on\Proper Crystal Relay\Run-Crystal-Relay-Source.ps1`
- Root docs:
  `E:\!!!Program to work on\Proper Crystal Relay\README.md`
  `E:\!!!Program to work on\Proper Crystal Relay\CHANGELOG.txt`
  `E:\!!!Program to work on\Proper Crystal Relay\RELEASE-CHANGE-RECORD.txt`

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
- Runtime config:
  `C:\Users\screm\AppData\Local\CrystalRelay\bridge.runtime.json`
- Legacy app data may still be migrated from:
  `C:\Users\screm\AppData\Local\VrcTwitchOscBridge`

## Security Rules
- Twitch OAuth tokens and VRChat auth cookies are stored in Windows Credential Manager through `WindowsCredentialStore`.
- Secure metadata, avatar cache, and OSC parameter cache live under `AppData\Local\CrystalRelay\Secure`.
- Never write Twitch OAuth tokens, VRChat auth data, cookies, or secrets into repo files.
- Never expose secrets in `README.md`, `CHANGELOG.txt`, release notes, examples, test data, or public repo files.
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
- If `AGENTS.md` disagrees with the project file or latest semantic test-build folder, trust the project file and version folders, then update `AGENTS.md` as part of the housekeeping.
- Update version in `VrcTwitchOscBridge\VrcTwitchOscBridge.csproj` before release packaging.
- Update `CHANGELOG.txt` for official releases.
- Use `RELEASE-CHANGE-RECORD.txt` for in-progress release notes before finalizing `CHANGELOG.txt`.

## Build and Release Rules
- Test build first when a change is visual, risky, runtime-affecting, or user-requested.
- Release builds go in a version folder:
  - Example: `Releases\v2.9.2`
- Test builds go in a version folder:
  - Example: `TestBuilds\v2.9.2`
- Raw backups go in a version folder:
  - Example: `Backups\v2.9.2`
- Test backup lane uses:
  - `Backups\Test\v2.9.2`
- Use `Build-Crystal-Relay-Test.ps1` for organized test packages.
- Use `Build-Crystal-Relay-Release.ps1` for official release packages.
- Both build scripts run the localization audit before publishing.
- Current test package layout should stay:
  - root shortcut: `Crystal Relay Test.lnk`
  - top-level docs: `README.md`, `CHANGELOG.txt`, optional `docs`
  - runtime files inside `App`
- Release packages should stay flat at the top level so the real versioned `.exe` is visible without a launcher script.
- Release zips should be kept; loose old release folders may be cleaned up only if the user requests it.
- Do not leave temporary developer-only controls in release builds.

## Backup Rules
- Run `Backup-Crystal-Relay-Project.ps1` before broad runtime changes, save-format changes, risky refactors, or release prep when the user asks for a raw backup.
- Use normal raw-source backups for real restore points.
- Use `-TestBackup` only for test-only safeguards that must not touch the normal restore lane.
- Do not delete backups unless the user explicitly requests it.
- App-data backups are separate from raw source backups and should not be synced to the public repo.

## Current Themes
- `Void Crystal`
- `Custom`
- `Treetender's Arm`
- `Dream Scape`
- `MainFrame`
- `Trash Kitty`
- `Bratwurst`
- `Carrot Patch`
- `Bubblegum`
- `Cosmic Puppy Girl`
- `Peaches & Cream`
- `Moon Bunny Wink`
- `Dread Night Bar`
- `Baked`

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
- Supporter Growth is event-driven by bits/subs/gift subs and does not create a channel-point reward.
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

## Current Stability Priorities
- Keep Twitch reward activation, visibility, cooldown color, delete-when-inactive behavior, and cleanup predictable.
- Keep Avatar Sets and Avatar Change correctly tied to the current VRChat avatar.
- Keep Bits + Subs override priority independent from normal reward blockers.
- Keep Avatar Scaling transitions, cooldowns, disable pairing, master reward gating, and height carryover stable.
- Keep Universal Triggers import, command fusion, reward sync, and direct OSC execution stable.
- Keep OSC / OSCQuery connection reliable.
- Keep VRChat avatar and parameter caches stable.
- Keep Twitch Chatbox emote rendering reliable.
- Keep About-page live indicators accurate while the app stays open.
- Keep crash logs reliably written to disk.

## Agent Workflow Rules
- Read existing scripts and services before adding new ones.
- Reuse existing output folders; do not invent new storage roots.
- Prefer minimal, targeted edits over broad renames.
- Use `apply_patch` for manual file edits.
- Do not revert unrelated user changes.
- Build after code changes that affect runtime behavior or XAML:
  `dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj" --no-restore`
- Refresh a test package after test-build changes, using the active test version discovered from the Versioning Rules:
  `powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\Build-Crystal-Relay-Test.ps1" -Version 3.1.3`
- Update release packages only after the change is confirmed good.
- If a temporary test tool is added, remove it before release unless explicitly requested.
- Keep changelog wording user-facing and generic for hidden easter eggs.
- When preparing public repo updates, run the sync script and inspect the public sync output for private information before pushing.

## Do Not
- Do not create stray runtime files in the repo root.
- Do not add new brand names without user approval.
- Do not store user login/session data in source-controlled files.
- Do not revert unrelated user changes.
- Do not delete backups or release zips unless explicitly requested.
- Do not commit large videos directly into source history; use GitHub Release assets when needed.
- Do not modify VRChat LocalLow OSC JSON files.
