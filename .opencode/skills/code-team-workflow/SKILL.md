---
name: code-team-workflow
description: Run coding work through a staged AI code team: research, coder, debugger, and final tester with major/minor bug handling and documented dependency decisions.
license: MIT
compatibility: opencode
metadata:
  audience: developers
  workflow: research-code-debug-finalize
---

# Code Team Workflow

Use this skill when the user wants a coding task handled by a coordinated code team rather than one all-purpose pass.

## Core rule

Never jump straight into edits unless the user explicitly asks for a tiny direct fix. For normal feature work, bug fixes, library choices, UI work, integrations, refactors, or new apps, run this order:

1. Research Team
2. Coder Team
3. Debugger Team
4. Finalize Tester

Each team must produce a handoff packet for the next team. Keep handoffs concise but complete enough that the next agent can continue without guessing.

## Team communication protocol

Every handoff packet must include:

- `phase`: research, coding, debugging, or final-test
- `goal`: the user-visible goal being solved
- `facts`: important discovered facts and assumptions
- `files`: files read, created, or changed
- `commands`: commands run and their result
- `decisions`: important design or library choices
- `risks`: possible problems or things not yet verified
- `next`: exact next action for the next team

If installed, prefer delegating to these OpenCode agents:

- `@code-researcher`
- `@code-builder`
- `@code-debugger`
- `@code-finalizer`

If the agents are not installed, simulate the same four phases in the current session.

## Phase 1: Research Team

The Research Team gathers the information needed before coding. It should:

- Inspect the existing codebase structure, package files, docs, tests, and build scripts.
- Research relevant official documentation when library or platform behavior matters.
- Find public, open, free-to-use UI/component options when the user asks for UI.
- Check licenses and avoid recommending libraries with unclear, paid-only, or incompatible terms.
- Identify the minimum libraries needed for the program to work.
- Prefer established libraries with active maintenance, clear documentation, and simple integration.
- Produce a dependency plan with install commands, why each library is needed, and any alternatives rejected.

The Research Team must not edit files unless the user specifically asked for a research document to be written.

## Phase 2: Coder Team

The Coder Team implements only after receiving the research handoff. It should:

- Make the smallest clean set of edits that solves the goal.
- Preserve the existing project style, naming, folder structure, and tooling.
- Add or update tests when appropriate.
- Add clear comments only where they explain non-obvious logic.
- Update documentation when the change affects setup, usage, configuration, or dependencies.
- Avoid destructive commands and avoid broad rewrites unless the task requires them.

The Coder Team hands off all changed files, commands run, and unresolved risks to the Debugger Team.

## Phase 3: Debugger Team

The Debugger Team verifies and fixes problems after coding. It should:

- Run the project's normal checks such as tests, type checks, lint, format checks, and builds.
- Investigate failures before changing code.
- Fix major bugs immediately.
- Re-run the relevant checks after each fix.
- Classify leftover issues as major or minor.

Major bugs must not be deferred. Major bugs include:

- Build failures
- Test failures related to the change
- Runtime crashes
- Broken user-facing behavior
- Security problems
- Data loss or corruption risks
- Missing required dependency or invalid install/setup steps

Minor bugs may be noted for later only when they do not break functionality. Minor bugs include:

- Spelling mistakes
- Cosmetic spacing or wording issues
- Small formatting problems that do not affect execution
- Non-blocking documentation polish

When deferring a minor issue, note the file, line or section if known, what should be fixed later, and why it is safe to defer.

## Phase 4: Finalize Tester

The Finalize Tester independently verifies that the program does what the user asked. It should:

- Re-read the original user request and compare it to the implementation.
- Run final checks or manual verification steps.
- Confirm expected behavior, commands run, and results.
- Review dependency documentation for accuracy.
- Report any remaining issues.

If the Finalize Tester finds a major issue, it must return a clear correction request to the Coder Team. Do not call the work complete until the Coder Team fixes it and the Debugger Team re-checks it.

If the Finalize Tester finds only minor issues, it may approve the work while listing those deferred notes.

## Completion response

When the workflow is complete, give the user:

- What changed
- What files changed
- What checks passed
- Any deferred minor issues
- Any setup/install commands the user should run
- Any important limitations or assumptions

Do not hide failed checks. If something could not be verified, say so clearly.
