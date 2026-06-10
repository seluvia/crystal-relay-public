# Visual Companion Windows Wrapper — Design Spec

**Date:** 2026-06-10
**Status:** Approved (pending user spec review)
**Owner:** Opencode + Crystal Relay tooling
**Scope:** Opencode superpowers plugin (`brainstorming` skill) on Windows

## Goal

Stop Visual Studio Code from auto-opening when the brainstorming visual companion is started on Windows 11. The visual companion must keep working as designed — a local HTTP/WebSocket server that the AI pushes HTML screens to and reads click events back from.

## Root Cause

The `brainstorming` skill's visual companion is launched by `scripts/start-server.sh` (a bash script) in the upstream `superpowers` package, which lives in:

```
C:\Users\screm\.cache\opencode\packages\superpowers@git+https_\github.com\obra\superpowers.git\node_modules\superpowers\skills\brainstorming\scripts\start-server.sh
```

Opencode's bash tool on Windows runs commands through **PowerShell**, not Git Bash. When the AI invokes `start-server.sh`, PowerShell cannot execute the script directly. Windows then falls back to its default file-association handler for `.sh` files, which on this machine is Visual Studio Code. That is the unexpected VS Code window.

None of the upstream scripts (`start-server.sh`, `server.cjs`, `helper.js`, `stop-server.sh`) call any external editor. The problem is purely about how the launcher gets executed on Windows.

The fix is to give the AI a **PowerShell-native launcher** that does the same work, so PowerShell never falls back to the file-association handler.

## Non-Goals

- Not modifying the upstream `superpowers` package (lives in `.cache`, will be overwritten by future updates).
- Not changing the brainstorming skill's flow, JSON output format, or the AI's interaction with the user.
- Not changing how the brainstorming server itself works (the Node.js code in `server.cjs` is unchanged).
- Not introducing a new background-process model. Foreground is the safe default on Windows.

## Components

### Component 1 — `start-server.ps1`

- **Path:** `E:\!!!Program to work on\Proper Crystal Relay\.opencode\skills\visual-companion-windows\scripts\start-server.ps1`
- **Purpose:** PowerShell port of `start-server.sh`.
- **CLI:** Mirrors upstream flags: `--project-dir`, `--host`, `--url-host`, `--foreground`, `--background`.
- **Behavior:**
  - Parse flags using PowerShell parameter binding.
  - Default to foreground mode on Windows (matches the .sh auto-detection of `msys`/`cygwin`/`mingw`).
  - Generate session directory:
    - If `--project-dir` set: `<project>/.superpowers/brainstorm/<pid>-<unix-ts>`.
    - Else: `[System.IO.Path]::GetTempPath() + 'brainstorm-<pid>-<unix-ts>'` (PowerShell-portable equivalent of `/tmp`, never hardcodes a user path).
  - Set environment variables: `BRAINSTORM_DIR`, `BRAINSTORM_HOST`, `BRAINSTORM_URL_HOST`, `BRAINSTORM_OWNER_PID`.
  - Resolve `server.cjs` automatically from the upstream `superpowers` package path. The script's resolution algorithm:
    1. Start at `$PSScriptRoot`.
    2. Walk up parent directories looking for `node_modules\superpowers\skills\brainstorming\scripts\server.cjs` (using the same `node_modules` layout that npm/Node use on Windows).
    3. If not found in 8 levels, exit with the error described in the error-handling table.
  - This means the wrapper does not need a copy of the server and does not need a manually-configured path.
  - Invoke `node` directly via `Start-Process` with the env vars; for foreground mode, call `node` synchronously so PowerShell blocks until the server exits (matching the .sh foreground behavior).
  - Output the same JSON as the .sh: `{"type":"server-started","port":...,"host":...,"url_host":...,"url":...,"screen_dir":...,"state_dir":...}`.
  - Also write the JSON to `<state-dir>/server-info` so the AI can read it on the next turn.

### Component 2 — `stop-server.ps1`

- **Path:** `E:\!!!Program to work on\Proper Crystal Relay\.opencode\skills\visual-companion-windows\scripts\stop-server.ps1`
- **Purpose:** PowerShell port of `stop-server.sh`.
- **CLI:** First positional argument is the session directory.
- **Behavior:**
  - Read `<session>/state/server.pid`.
  - Stop the Node process (graceful, then `Stop-Process -Force` if it doesn't exit within 2 seconds).
  - Remove `server.pid` and `server.log`.
  - Only delete the session directory if it is under the ephemeral `Temp\brainstorm-*` prefix. Sessions under `.superpowers/brainstorm/` are preserved.
  - Output `{"status":"stopped"}` on success, `{"status":"not_running"}` if no PID file, or `{"status":"failed","error":"..."}` on error.

### Component 3 — `SKILL.md` (project-level skill)

- **Path:** `E:\!!!Program to work on\Proper Crystal Relay\.opencode\skills\visual-companion-windows\SKILL.md`
- **Frontmatter:**
  ```
  ---
  name: visual-companion-windows
  description: Use this when the brainstorming visual companion needs to be started or stopped on Windows. Routes around the upstream bash scripts (which cause VS Code to auto-open) by calling the project-local PowerShell wrappers.
  ---
  ```
- **Body:** Short, focused. Tells the AI:
  - On Windows, never call `scripts/start-server.sh` directly — that triggers VS Code.
  - Call `.opencode/skills/visual-companion-windows/scripts/start-server.ps1` with the same flags instead.
  - On the next turn, read `<state-dir>/server-info` for the URL.
  - For cleanup, call `stop-server.ps1 <session-dir>`.
  - Show the exact command lines for both.

### Component 4 — `AGENTS.md` note

- **Path:** New section in `E:\!!!Program to work on\Proper Crystal Relay\AGENTS.md` under a "Dev Tooling" heading.
- **Purpose:** Surface the workaround on every session, even before the AI loads the project-level skill.
- **Content:** A short paragraph: which script to use on Windows, why, and where the .ps1 lives. No more than a few lines.

## Data Flow

1. The AI loads the `brainstorming` skill (from the upstream superpowers plugin).
2. The AI is asked a question that benefits from a visual mockup, so the brainstorming skill directs it to start the visual companion.
3. The AI consults `AGENTS.md` (which it reads on every session) and/or loads the `visual-companion-windows` skill, and sees the override.
4. The AI calls `start-server.ps1 --project-dir <project> --host 127.0.0.1 --url-host localhost` through the opencode bash tool (PowerShell).
5. PowerShell executes the .ps1. The .ps1 runs `node server.cjs` with the right env vars. The server starts. **No file-association fallback, no VS Code window.**
6. The .ps1 emits the `server-started` JSON on stdout and also writes it to `<state-dir>/server-info`.
7. The AI reads `<state-dir>/server-info` (or the stdout JSON) and reports the URL to the user.
8. The rest of the brainstorming flow is unchanged: AI writes HTML to `screen_dir`, server picks it up, user clicks, server writes to `state_dir/events`, AI reads the events on the next turn.
9. When done, the AI calls `stop-server.ps1 <session-dir>` to clean up.

## Error Handling

| Failure                                                       | Behavior                                                                                  |
| ------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `node.exe` not in PATH                                        | `start-server.ps1` exits with a clear error: `"Node.js not found in PATH. ..."`. No Node process is started. |
| Upstream `superpowers` package not found                      | `start-server.ps1` exits with: `"Could not locate server.cjs. superpowers package missing."` |
| `--project-dir` does not exist                               | Exit with: `"--project-dir path does not exist: <path>"`                                  |
| AI invokes the upstream `.sh` directly                        | The project skill and AGENTS.md note both warn against this. The .ps1 cannot intercept upstream calls — it relies on the AI following the override. |
| Server dies after start (Node crashes)                        | `<state-dir>/server-info` is never written. AI sees no `server-started` line and can retry with `--foreground` for live logs. (In `--foreground` mode the .ps1 streams Node's stdout/stderr directly so the crash is visible.) |
| `stop-server.ps1` invoked on an already-stopped session      | Returns `{"status":"not_running"}` and exits 0. No-op cleanup.                            |

## Testing

Manual, since this is a local developer-tooling change with no automated test infrastructure for opencode skills.

1. **Launcher smoke test**
   - Run `pwsh -File start-server.ps1 --project-dir <test-dir>` in a real PowerShell window.
   - Expect a single JSON line on stdout: `{"type":"server-started",...}`.
   - Expect `<test-dir>/.superpowers/brainstorm/<id>/state/server-info` to exist with the same JSON.
2. **Server end-to-end**
   - Open the returned URL in a browser.
   - Confirm the waiting page renders.
   - Write a simple HTML fragment (e.g. a single `<h2>hello</h2>`) to `<content-dir>/test.html`.
   - Confirm the browser reloads and shows the fragment.
   - Click a `data-choice` element on a test page; confirm `<state-dir>/events` is appended to.
3. **Stopper**
   - Run `stop-server.ps1 <session-dir>` while the server is up.
   - Expect `{"status":"stopped"}` on stdout and the Node process gone from Task Manager.
   - Run it again on the same session: expect `{"status":"not_running"}`.
4. **No VS Code spawn**
   - Repeat all of the above in the opencode environment (i.e. AI invokes the .ps1 via the bash tool).
   - At no point should Visual Studio Code or any other editor open.
5. **Regression**
   - Confirm the upstream `.sh` still works for users on macOS/Linux (no changes to upstream files).
   - Confirm the rest of the brainstorming flow (HTML push, click events, `waiting.html` unload) is unchanged.

## Acceptance Criteria

- AI can start the visual companion on Windows 11 without VS Code (or any editor) auto-opening.
- The .ps1 wrappers produce the same JSON output as the .sh scripts.
- AI can stop the visual companion cleanly via the .ps1 stopper.
- The workaround is documented in two places the AI can find: `AGENTS.md` and a project-level skill.
- No upstream files (in `.cache/opencode/...`) are modified.
- The brainstorming flow is otherwise unchanged from the user's perspective.

## Open Questions

None at design time. Implementation may surface PowerShell-specific quirks (env-var inheritance, `Start-Process` vs direct invocation, `Get-Date -UFormat` vs `[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()`); these will be handled inline.

## Rollout

1. Land the four components in a single commit.
2. Verify with a manual end-to-end test in opencode.
3. If a Windows port is ever accepted upstream, the project-local files can be removed in favor of the upstream version.
