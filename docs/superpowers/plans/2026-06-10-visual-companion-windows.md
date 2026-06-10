# Visual Companion Windows Wrapper Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Windows-native PowerShell wrappers for the brainstorming visual companion so the AI can launch it on Windows 11 without VS Code (or any other editor) auto-opening.

**Architecture:** Two `.ps1` files mirror the upstream `start-server.sh` / `stop-server.sh` and run `node server.cjs` directly with the right env vars. A project-level skill and an `AGENTS.md` note tell the AI to use the wrappers on Windows. No upstream files are modified.

**Tech Stack:** PowerShell 5.1+ (already what opencode's bash tool runs on Windows), Node.js (already a dep of the upstream package), plain markdown for the skill and AGENTS.md note.

**Spec:** `docs/superpowers/specs/2026-06-10-visual-companion-windows-design.md`

**Working directory note:** All paths are relative to the repo root `E:\!!!Program to work on\Proper Crystal Relay`. The user's PowerShell environment is `powershell.exe` (5.1) unless noted.

---

## File Structure

Files to be created:

- `.opencode/skills/visual-companion-windows/scripts/start-server.ps1` — PowerShell port of `start-server.sh`. The launcher.
- `.opencode/skills/visual-companion-windows/scripts/stop-server.ps1` — PowerShell port of `stop-server.sh`. The stopper.
- `.opencode/skills/visual-companion-windows/SKILL.md` — Project-level skill that tells the AI to use the `.ps1` wrappers on Windows.

Files to be modified:

- `AGENTS.md` — Add a short "Dev Tooling — Visual Companion on Windows" section so the AI sees the workaround on every session, even before it loads the project-level skill.

The upstream `server.cjs` and the upstream `.sh` scripts are NOT touched. The wrapper resolves `server.cjs` by walking up from its own location to find `node_modules/superpowers/skills/brainstorming/scripts/server.cjs`.

---

## Task 1: Create the folder and `start-server.ps1`

**Files:**
- Create: `.opencode/skills/visual-companion-windows/scripts/start-server.ps1`

- [ ] **Step 1: Create the target folder**

```powershell
New-Item -ItemType Directory -Force -Path ".opencode/skills/visual-companion-windows/scripts" | Out-Null
```

- [ ] **Step 2: Verify the folder exists**

Run: `Test-Path -LiteralPath ".opencode/skills/visual-companion-windows/scripts"`
Expected: `True`

- [ ] **Step 3: Write `start-server.ps1`**

Write the following content to `.opencode/skills/visual-companion-windows/scripts/start-server.ps1`:

```powershell
<#
.SYNOPSIS
  Windows-native launcher for the brainstorming visual companion server.
.DESCRIPTION
  PowerShell port of scripts/start-server.sh from the upstream superpowers
  package. Use this on Windows instead of the .sh script — invoking the
  .sh from PowerShell causes Windows to open it in the default file
  association editor (typically VS Code).
.PARAMETER ProjectDir
  Store session files under <ProjectDir>/.superpowers/brainstorm/ instead
  of the system temp folder. Files persist after the server stops.
.PARAMETER Host
  Host/interface to bind (default: 127.0.0.1).
.PARAMETER UrlHost
  Hostname shown in the returned URL JSON (default: localhost).
#>
[CmdletBinding()]
param(
    [string]$ProjectDir,
    [string]$Host = '127.0.0.1',
    [string]$UrlHost = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrEmpty($UrlHost)) {
    if ($Host -eq '127.0.0.1' -or $Host -eq 'localhost') {
        $UrlHost = 'localhost'
    } else {
        $UrlHost = $Host
    }
}

# Generate unique session directory
$sessionId = "$PID-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"

if (-not [string]::IsNullOrEmpty($ProjectDir)) {
    if (-not (Test-Path -LiteralPath $ProjectDir)) {
        Write-Output "{""error"": ""--project-dir path does not exist: $ProjectDir""}"
        exit 1
    }
    $ProjectDir = (Resolve-Path -LiteralPath $ProjectDir).Path
    $sessionRoot = Join-Path $ProjectDir '.superpowers/brainstorm'
    $sessionDir = Join-Path $sessionRoot $sessionId
} else {
    $tempRoot = [System.IO.Path]::GetTempPath().TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $sessionDir = Join-Path $tempRoot "brainstorm-$sessionId"
}

$contentDir = Join-Path $sessionDir 'content'
$stateDir = Join-Path $sessionDir 'state'
New-Item -ItemType Directory -Path $contentDir, $stateDir -Force | Out-Null

# Locate server.cjs in the upstream superpowers package by walking up.
$scriptDir = $PSScriptRoot
$serverCjs = $null
$probe = $scriptDir
for ($i = 0; $i -lt 8; $i++) {
    $candidate = Join-Path $probe 'node_modules/superpowers/skills/brainstorming/scripts/server.cjs'
    if (Test-Path -LiteralPath $candidate) {
        $serverCjs = (Resolve-Path -LiteralPath $candidate).Path
        break
    }
    $parent = Split-Path -Parent $probe
    if ([string]::IsNullOrEmpty($parent) -or $parent -eq $probe) { break }
    $probe = $parent
}
if (-not $serverCjs) {
    Write-Output "{""error"": ""Could not locate server.cjs. superpowers package not found in node_modules.""}"
    exit 1
}

# Find node.exe
$nodeExe = (Get-Command node.exe -ErrorAction SilentlyContinue)?.Source
if (-not $nodeExe) {
    Write-Output "{""error"": ""node.exe not found in PATH. Install Node.js or add it to PATH.""}"
    exit 1
}

# Launch the server detached so this script can return.
$logFile = Join-Path $stateDir 'server.log'
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $nodeExe
$startInfo.Arguments = ('"' + $serverCjs + '"')
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.EnvironmentVariables['BRAINSTORM_DIR'] = $sessionDir
$startInfo.EnvironmentVariables['BRAINSTORM_HOST'] = $Host
$startInfo.EnvironmentVariables['BRAINSTORM_URL_HOST'] = $UrlHost
$startInfo.EnvironmentVariables['BRAINSTORM_OWNER_PID'] = "$PID"

$proc = [System.Diagnostics.Process]::Start($startInfo)

# Persist the Node PID for the stopper.
$pidFile = Join-Path $stateDir 'server.pid'
Set-Content -LiteralPath $pidFile -Value "$($proc.Id)" -Encoding ASCII

# Drain stdout/stderr into the log file in the background.
$logWriter = [System.IO.StreamWriter]::new($logFile, $true)
$logWriter.AutoFlush = $true
$proc.add_OutputDataReceived({ if ($null -ne $_.Data) { $logWriter.WriteLine($_.Data) } })
$proc.add_ErrorDataReceived({  if ($null -ne $_.Data) { $logWriter.WriteLine($_.Data) } })
$proc.BeginOutputReadLine()
$proc.BeginErrorReadLine()

# Poll for server-info (written by server.cjs when ready).
$infoPath = Join-Path $stateDir 'server-info'
$deadline = (Get-Date).AddSeconds(5)
$info = $null
while ((Get-Date) -lt $deadline) {
    if ($proc.HasExited) { break }
    if (Test-Path -LiteralPath $infoPath) {
        $info = Get-Content -LiteralPath $infoPath -Raw
        break
    }
    Start-Sleep -Milliseconds 100
}

if ($info) {
    Write-Output ($info.Trim())
    exit 0
}

if ($proc.HasExited) {
    Write-Output "{""error"": ""Server exited before becoming ready. See $logFile""}"
    exit 1
}

Write-Output "{""error"": ""Server failed to start within 5 seconds""}"
try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
exit 1
```

- [ ] **Step 4: Verify the file was created**

Run: `Test-Path -LiteralPath ".opencode/skills/visual-companion-windows/scripts/start-server.ps1"`
Expected: `True`

- [ ] **Step 5: Smoke-test the launcher**

Run:
```powershell
$tmpProj = Join-Path $env:TEMP "cr-vc-test-$PID"
New-Item -ItemType Directory -Force -Path $tmpProj | Out-Null
$out = pwsh -NoProfile -File ".opencode/skills/visual-companion-windows/scripts/start-server.ps1" -ProjectDir $tmpProj 2>&1
$out
$out | ForEach-Object { if ($_ -like '*server-started*') { Write-Output "OK" } else { Write-Output "NOT-OK: $_" } }
```

Expected: One line of JSON containing `"type":"server-started"` and `"url":"http://localhost:..."` followed by `OK`. If you see `NOT-OK`, capture the actual output and inspect it.

- [ ] **Step 6: Verify a session directory was created**

Run:
```powershell
Get-ChildItem -Path "$tmpProj/.superpowers/brainstorm" -Directory | Select-Object -First 1 | ForEach-Object { Get-ChildItem $_.FullName -Recurse | Select-Object FullName }
```

Expected: A session directory containing `content\` and `state\`, with `state\server.pid` and `state\server.log` and `state\server-info`.

- [ ] **Step 7: Confirm no editor was spawned**

Open Task Manager (Ctrl+Shift+Esc). Confirm `code.exe`, `devenv.exe`, or any other editor is NOT running for this user. (The Node process from step 5 may be running — that's expected.)

- [ ] **Step 8: Stop the test server before continuing**

Run:
```powershell
$session = Get-ChildItem -Path "$tmpProj/.superpowers/brainstorm" -Directory | Select-Object -First 1 -ExpandProperty FullName
Stop-Process -Name node -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $tmpProj -Recurse -Force -ErrorAction SilentlyContinue
```

- [ ] **Step 9: Commit**

```bash
git add .opencode/skills/visual-companion-windows/scripts/start-server.ps1
git commit -m "feat(tools): add PowerShell launcher for visual companion on Windows"
```

---

## Task 2: Add `stop-server.ps1`

**Files:**
- Create: `.opencode/skills/visual-companion-windows/scripts/stop-server.ps1`

- [ ] **Step 1: Write `stop-server.ps1`**

Write the following content to `.opencode/skills/visual-companion-windows/scripts/stop-server.ps1`:

```powershell
<#
.SYNOPSIS
  Windows-native stopper for the brainstorming visual companion server.
.DESCRIPTION
  PowerShell port of scripts/stop-server.sh from the upstream superpowers
  package. Pass the session directory printed by start-server.ps1 (the
  value of the screen_dir/state_dir parents).
.PARAMETER SessionDir
  The session directory whose state/server.pid should be honoured.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$SessionDir
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SessionDir)) {
    Write-Output '{"status":"not_running"}'
    exit 0
}

$stateDir = Join-Path $SessionDir 'state'
$pidFile = Join-Path $stateDir 'server.pid'

if (-not (Test-Path -LiteralPath $pidFile)) {
    Write-Output '{"status":"not_running"}'
    exit 0
}

$rawPid = (Get-Content -LiteralPath $pidFile -Raw).Trim()
$serverPid = 0
if (-not [int]::TryParse($rawPid, [ref]$serverPid) -or $serverPid -le 0) {
    Remove-Item -LiteralPath $pidFile -ErrorAction SilentlyContinue
    Write-Output '{"status":"not_running"}'
    exit 0
}

# Graceful
try { Stop-Process -Id $serverPid -ErrorAction SilentlyContinue } catch {}

$deadline = (Get-Date).AddSeconds(2)
while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $serverPid -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 100
}

# Force if still alive
if (Get-Process -Id $serverPid -ErrorAction SilentlyContinue) {
    try { Stop-Process -Id $serverPid -Force -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Milliseconds 100
}

if (Get-Process -Id $serverPid -ErrorAction SilentlyContinue) {
    Write-Output '{"status":"failed","error":"process still running"}'
    exit 1
}

Remove-Item -LiteralPath $pidFile -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $stateDir 'server.log') -ErrorAction SilentlyContinue

# Only delete ephemeral temp session directories. Sessions under
# .superpowers/ persist for later review.
$tempRoot = [System.IO.Path]::GetTempPath().TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$normalized = $SessionDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
if ($normalized.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Remove-Item -LiteralPath $SessionDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output '{"status":"stopped"}'
```

- [ ] **Step 2: Smoke-test the stopper with a running session**

Run:
```powershell
$tmpProj = Join-Path $env:TEMP "cr-vc-test-$PID"
New-Item -ItemType Directory -Force -Path $tmpProj | Out-Null
$out = pwsh -NoProfile -File ".opencode/skills/visual-companion-windows/scripts/start-server.ps1" -ProjectDir $tmpProj
$outJson = $out | Where-Object { $_ -like '*server-started*' } | Select-Object -First 1
$obj = $outJson | ConvertFrom-Json
$sessionDir = Split-Path -Parent $obj.state_dir
Write-Output ("Session dir: " + $sessionDir)

$stopOut = pwsh -NoProfile -File ".opencode/skills/visual-companion-windows/scripts/stop-server.ps1" $sessionDir
$stopOut
$stopOut | ForEach-Object { if ($_ -eq '{"status":"stopped"}') { Write-Output "OK" } else { Write-Output ("NOT-OK: " + $_) } }

# Confirm Node process is gone
$nodeStillRunning = Get-Process -Name node -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $obj.port * 1 + 0 }  # port is not pid, just sanity
Get-Process -Name node -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("Node still running: pid " + $_.Id) }

# Idempotent: a second stop should say not_running
$stopOut2 = pwsh -NoProfile -File ".opencode/skills/visual-companion-windows/scripts/stop-server.ps1" $sessionDir
$stopOut2 | ForEach-Object { if ($_ -eq '{"status":"not_running"}') { Write-Output "OK-IDEMPOTENT" } else { Write-Output ("NOT-OK-IDEMPOTENT: " + $_) } }

Remove-Item -LiteralPath $tmpProj -Recurse -Force -ErrorAction SilentlyContinue
```

Expected output ends with `OK` (first stop) and `OK-IDEMPOTENT` (second stop).

- [ ] **Step 3: Commit**

```bash
git add .opencode/skills/visual-companion-windows/scripts/stop-server.ps1
git commit -m "feat(tools): add PowerShell stopper for visual companion on Windows"
```

---

## Task 3: Create the project-level skill

**Files:**
- Create: `.opencode/skills/visual-companion-windows/SKILL.md`

- [ ] **Step 1: Write `SKILL.md`**

Write the following content to `.opencode/skills/visual-companion-windows/SKILL.md`:

```markdown
---
name: visual-companion-windows
description: Use this when the brainstorming visual companion needs to be started or stopped on Windows. Routes around the upstream bash scripts (which cause VS Code to auto-open) by calling the project-local PowerShell wrappers.
---

# Visual Companion on Windows

The upstream `superpowers` package launches the brainstorming visual companion with `scripts/start-server.sh`. On Windows, opencode's bash tool is PowerShell — invoking the `.sh` from PowerShell causes Windows to fall back to its file-association handler, which opens the script in VS Code. The companion never actually starts.

## How to start the companion

Call the project-local PowerShell wrapper instead:

```powershell
pwsh -NoProfile -File ".opencode/skills/visual-companion-windows/scripts/start-server.ps1" -ProjectDir (Get-Location).Path
```

The wrapper:

- Resolves `server.cjs` automatically from the upstream `node_modules/superpowers` package
- Spawns `node` directly with the right env vars — no `.sh` ever executes, no editor opens
- Writes the same `server-started` JSON the `.sh` writes, both to stdout and to `<state-dir>/server-info`
- Polls for `server-info` and exits within 5 seconds, returning the JSON to the bash tool

## How to read the URL

Either read stdout (the JSON line containing `server-started`), or on a later turn read `<state-dir>/server-info` directly. The JSON has `url`, `port`, `host`, `url_host`, `screen_dir`, and `state_dir`.

## How to stop the companion

Pass the session directory (the parent of `state_dir` from the start JSON) to the stopper:

```powershell
pwsh -NoProfile -File ".opencode/skills/visual-companion-windows/scripts/stop-server.ps1" <session-dir>
```

It returns `{"status":"stopped"}`, `{"status":"not_running"}`, or `{"status":"failed","error":"..."}`.

## What to do if you see VS Code open

That means you (or a previous turn) invoked `start-server.sh` directly. Stop the Node process if one is running, then call the `.ps1` wrapper above. The wrapper is the only supported way to start the companion on Windows.
```

- [ ] **Step 2: Verify the file exists**

Run: `Test-Path -LiteralPath ".opencode/skills/visual-companion-windows/SKILL.md"`
Expected: `True`

- [ ] **Step 3: Commit**

```bash
git add .opencode/skills/visual-companion-windows/SKILL.md
git commit -m "docs(tools): add project skill for visual companion on Windows"
```

---

## Task 4: Add the `AGENTS.md` note

**Files:**
- Modify: `AGENTS.md` (append a new section under a "Dev Tooling" heading)

- [ ] **Step 1: Read the end of `AGENTS.md` to find a good insertion point**

Run:
```powershell
$path = "AGENTS.md"
$lines = Get-Content -LiteralPath $path
"Total lines: $($lines.Count)"
"Last 5 lines:"
$lines[-5..-1] | ForEach-Object { "[$_]" }
```

This tells you where the file ends so the next edit lands cleanly. The new section should be appended at the end of the file.

- [ ] **Step 2: Append the new section**

Append the following block at the end of `AGENTS.md`:

```markdown

## Dev Tooling

### Visual Companion on Windows

The brainstorming visual companion (loaded by the upstream `superpowers` `brainstorming` skill) launches via `scripts/start-server.sh`. On Windows, opencode's bash tool is PowerShell — invoking the `.sh` from PowerShell causes Windows to fall back to the file-association handler and open the script in Visual Studio Code.

Always use the project-local PowerShell wrappers instead. Do not invoke the upstream `.sh` directly on Windows:

```powershell
# Start
pwsh -NoProfile -File ".opencode/skills/visual-companion-windows/scripts/start-server.ps1" -ProjectDir (Get-Location).Path

# Stop
pwsh -NoProfile -File ".opencode/skills/visual-companion-windows/scripts/stop-server.ps1" <session-dir>
```

The wrapper resolves `server.cjs` from the upstream `node_modules/superpowers` package and spawns `node` directly, so no editor ever opens. See `.opencode/skills/visual-companion-windows/SKILL.md` for full details.
```

- [ ] **Step 3: Verify the section was added**

Run:
```powershell
Select-String -LiteralPath "AGENTS.md" -Pattern "Visual Companion on Windows" | Select-Object LineNumber
```

Expected: At least one match.

- [ ] **Step 4: Commit**

```bash
git add AGENTS.md
git commit -m "docs(agents): add Windows visual companion workaround note"
```

---

## Task 5: End-to-end verification in the opencode environment

**Files:** None — this is a manual verification task.

- [ ] **Step 1: Start the companion via the opencode bash tool**

In a new opencode turn, ask the AI to "Start the brainstorming visual companion for this project." The AI should pick up the `AGENTS.md` note and the project skill, and invoke the `.ps1` wrapper via the bash tool.

- [ ] **Step 2: Confirm no editor opened**

Switch to the desktop. Confirm Visual Studio Code (or any other editor) did not open. The bash tool should have returned the `server-started` JSON.

- [ ] **Step 3: Open the URL in a browser**

Take the `url` field from the JSON and open it in a browser. Expect the "Brainstorm Companion — Waiting for the agent to push a screen…" page.

- [ ] **Step 4: Write a test screen and verify reload**

Ask the AI to push a test screen. The browser should reload automatically and display the test content.

- [ ] **Step 5: Click an option and verify the event was recorded**

Click a `data-choice` element. Confirm the AI's next turn can read the event from `<state-dir>/events`.

- [ ] **Step 6: Stop the companion**

Ask the AI to stop the companion. Confirm the stopper reports `stopped` and the Node process exits.

- [ ] **Step 7: Document the result**

If all six steps above passed, the implementation is done. If any step failed, capture the actual output and start a new debugging pass — do not declare the task complete on partial evidence.

---

## Self-Review (already done before saving)

- **Spec coverage:** Every component in the spec (`start-server.ps1`, `stop-server.ps1`, `SKILL.md`, AGENTS.md note) has a dedicated task. End-to-end verification is its own task. No gaps.
- **Placeholder scan:** No "TBD" / "TODO" / "implement later" patterns. Every step has exact code, exact paths, or exact commands.
- **Type / signature consistency:** `start-server.ps1` exposes `-ProjectDir`, `-Host`, `-UrlHost`. `stop-server.ps1` exposes `-SessionDir` as the only positional parameter. The skill doc and AGENTS.md note both use the same names.
