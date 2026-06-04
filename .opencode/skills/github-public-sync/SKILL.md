---
name: github-public-sync
description: Safely sync Crystal Relay to the public GitHub repo with multi-layer privacy gates, secret scanning, and preflight validation before any push.
license: MIT
compatibility: opencode
metadata:
  audience: developers
  workflow: export-scan-verify-push
---

# GitHub Public Sync Workflow

Use this skill when the user wants to push changes to the public Crystal Relay repo (`seluvia/crystal-relay-public`). This is the ONLY approved path for public releases and source sync.

## Core rule

NEVER push to the public repo without running every safety gate in order. Privacy is non-negotiable. A single leaked secret, private path, internal note, or AI reference damages trust permanently.

## Required execution order

### Step 1: Export private → public

Run the export script from the private repo root:

```powershell
powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\tools\github\Export-Crystal-Relay-Public.ps1" -PublicRepoPath "C:\Users\screm\Documents\GitHub\crystal-relay-public"
```

This mirrors the private repo into the public working copy while excluding:
- `.appdata/`, `.dotnet/`, `.nuget/`, `.wrangler/`
- `Backups/`, `Releases/`, `TestBuilds/`, `Code Review/`, `temp-build/`
- `tools/private/`, `cloudflare/`, `tools/`
- `AGENTS.md`, `RELEASE-CHANGE-RECORD.txt`, `Backup-*.ps1`, `Sync-*.ps1`
- `*.user`, `*.suo`, `*.tmp`, `*.cache`, `*.nupkg`, `*.local.json`
- `secrets.json`, `appsettings.Development.json`, `bridge.runtime.local.json`

### Step 2: Safety preflight

Run the safety preflight against the public working copy:

```powershell
powershell -ExecutionPolicy Bypass -File "E:\!!!Program to work on\Proper Crystal Relay\tools\github\Test-Crystal-Relay-PublicSafety.ps1" -PublicRepoPath "C:\Users\screm\Documents\GitHub\crystal-relay-public"
```

This checks:
- **Blocked directories**: `.appdata`, `.codex`, `.dotnet`, `.nuget`, `.vs`, `.wrangler`, `Backups`, `Releases`, `TestBuilds`, `Code Review`, `temp-build`, `tools`, `private`, `cloudflare`, `bin`, `obj`
- **Blocked files**: `AGENTS.md`, `RELEASE-CHANGE-RECORD.txt`, `Backup-*.ps1`, `Sync-*.ps1`, `GITHUB-UPLOAD-NOTES.txt`
- **Blocked text patterns**: local paths, AI tooling references, private repo names, internal workflow notes
- **Blocked regex patterns**: filesystem paths, GitHub tokens (`ghp_`, `github_pat_`), OpenAI keys (`sk-`), Slack tokens (`xox`), Bearer/OAuth tokens, credential assignments
- **Git whitespace check**: `git diff --check`

### Step 3: Manual secret scan

Run `rg` across the public repo for anything automated checks might miss:

```powershell
rg -i --glob '!**/.git/**' --glob '!**/bin/**' --glob '!**/obj/**' "E:\\|C:\\Users|ghp_|github_pat_|AGENTS\.md|crystal-relay-private|cloudflare|tools/private|\.opencode|Codex|ChatGPT|OpenAI|prompt transcript|system prompt|developer message" "C:\Users\screm\Documents\GitHub\crystal-relay-public"
```

If ANY matches are found, STOP. Report them to the user. Do not push.

### Step 4: Verify git status

```powershell
git -C "C:\Users\screm\Documents\GitHub\crystal-relay-public" status
```

Confirm:
- Only expected files are staged or modified
- No untracked files that shouldn't be there
- No deleted files that should remain

### Step 5: Review the full diff (BEFORE staging)

```powershell
git -C "C:\Users\screm\Documents\GitHub\crystal-relay-public" diff
git -C "C:\Users\screm\Documents\GitHub\crystal-relay-public" diff --stat
```

Read every changed file. Look for:
- Local filesystem paths
- Credentials, tokens, or API keys
- Internal workflow notes or AI references
- Anything that looks wrong or out of place

Do NOT stage until you have read the entire diff.

### Step 6: Stage all changes

Only after ALL checks and the full diff review pass:

```powershell
git -C "C:\Users\screm\Documents\GitHub\crystal-relay-public" add -A
```

### Step 6b: Review staged diff before committing

```powershell
git -C "C:\Users\screm\Documents\GitHub\crystal-relay-public" diff --staged
```

If the staged diff contains anything unexpected, unstage and report to the user. Do not commit.

### Step 7: Commit

```powershell
git -C "C:\Users\screm\Documents\GitHub\crystal-relay-public" commit -m "<clear commit message>"
```

Commit message format: `Release v<version>: <brief description>` or `Update source: <brief description>`.

### Step 8: Push

```powershell
git -C "C:\Users\screm\Documents\GitHub\crystal-relay-public" push
```

### Step 9: Verify push succeeded

```powershell
git -C "C:\Users\screm\Documents\GitHub\crystal-relay-public" log --oneline -3
```

Confirm the push completed and the remote is up to date with `origin/main`.

---

## Workflow B: GitHub release upload

Use this when publishing a release ZIP to GitHub. The build must already exist — this workflow does NOT build the app.

### Release asset locations and naming

ZIPs are in: `E:\!!!Program to work on\Proper Crystal Relay\Releases\v<version>\`

| Type | ZIP name |
|---|---|
| Stable | `CrystalRelayTwitchOsc-v<version>-win-x64.zip` |
| Beta | `CrystalRelayTwitchOsc-v<version>-beta<N>-win-x64.zip` |

### Release upload steps

1. **Confirm ZIP exists** before creating any release on GitHub.

2. **Check if release already exists**:
   ```powershell
   gh release view v<version> --repo seluvia/crystal-relay-public
   ```

3. **Create GitHub release as draft** (if not already exists):
   - Stable: `gh release create v<version> --repo seluvia/crystal-relay-public --title "Crystal Relay v<version>" --notes-file "E:\!!!Program to work on\Proper Crystal Relay\CHANGELOG.txt" --draft`
   - Beta: `gh release create v<version>-beta<N> --repo seluvia/crystal-relay-public --title "Crystal Relay v<version> Beta <N>" --notes-file "E:\!!!Program to work on\Proper Crystal Relay\CHANGELOG.txt" --prerelease --draft`

4. **Upload ZIP**:
   ```powershell
   gh release upload v<version> "E:\!!!Program to work on\Proper Crystal Relay\Releases\v<version>\CrystalRelayTwitchOsc-v<version>-win-x64.zip" --repo seluvia/crystal-relay-public
   ```

5. **Verify upload**: `gh release view v<version> --repo seluvia/crystal-relay-public` — confirm asset appears.

6. **Publish (remove draft)** only after user confirmation:
   ```powershell
   gh release edit v<version> --draft=false --repo seluvia/crystal-relay-public
   ```

7. **Confirm live**: `gh release list --repo seluvia/crystal-relay-public`

---

## Failure handling

If ANY step fails:
1. Report the exact failure with file paths, line numbers, and matched content
2. Do NOT attempt to fix the issue — the Code Team handles fixes
3. Do NOT push or publish — the workflow stops until the issue is resolved
4. Wait for the user to confirm the fix, then re-run from Step 1

## What NEVER goes public

| Category | Examples |
|---|---|
| Secrets | Twitch OAuth, VRChat cookies, API keys, Cloudflare tokens |
| Private paths | `E:\!!!Program to work on\...`, `C:\Users\screm\...` |
| Internal files | `AGENTS.md`, `RELEASE-CHANGE-RECORD.txt` |
| Private tooling | `tools/private/`, `cloudflare/` |
| Build artifacts | `Releases/`, `TestBuilds/`, `Backups/` |
| AI references | `Codex`, `ChatGPT`, `OpenAI`, `prompt transcript` |
| Runtime data | `.appdata/`, crash logs, saved logins |
| Local state | `bridge.runtime.json`, `*.local.json` |
| Credentials | OAuth tokens, API keys, cookies, Bearer tokens |
| Internal branding | Legacy `VrcTwitchOscBridge` in user-facing docs |
| `.opencode/` | Agent definitions, skills, AI workflow config |
| `AGENTS.md` | AI agent instructions and project rules |
| `.appdata/` | App runtime data |
| `.codex/` | AI tool config |
| `.wrangler/` | Cloudflare worker state |
| `.nuget/` | NuGet cache |
| `temp-build/` | Temporary build output |
| `Code Review/` | Source review staging |
| `crystal-relay-live-list` | Private live list tool |
| `*.nupkg` | NuGet packages |
| `secrets.json` | Secret config |
| `appsettings.Development.json` | Dev config |
| `bridge.runtime.local.json` | Local runtime config |

## Public repo layout

The public repo should contain:
- `VrcTwitchOscBridge/` — main app source
- `CrystalRelayUpdater/` — updater source
- `oscquery-lib/` — OSCQuery library
- `LocalizationAudit/` — localization audit
- `VrcTwitchOscBridge.slnx` — solution file
- `README.md`, `LICENSE`, `CHANGELOG.txt` — docs
- `.github/workflows/` — CI workflows
- `docs/` — documentation
- `NuGet.Config`, `.gitignore` — config
- PowerShell scripts: `Build-*.ps1`, `Run-*.ps1`, `Launch-*.bat`

Everything else should be excluded.
