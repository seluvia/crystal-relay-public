# Crystal Relay Bug Report Worker

This Worker receives Crystal Relay in-app bug reports and creates GitHub issues in
`seluvia/crystal-relay-public`. The GitHub token must stay in Cloudflare secrets and
must never be committed to the desktop app or this folder.

## Required setup

1. Create a fine-grained GitHub token for `seluvia/crystal-relay-public` with Issues read/write access.
2. Create a Cloudflare KV namespace for rate limiting.
3. Replace the placeholder KV namespace id in `wrangler.toml`.
4. Store the GitHub token as a Worker secret:

   ```powershell
   wrangler secret put GITHUB_TOKEN
   ```

5. Deploy:

   ```powershell
   wrangler deploy
   ```

The desktop app posts to:

```text
https://crystal-relay-bug-report.screminpal-animation.workers.dev/report
```

If the deployed Worker uses a different URL, update `BugReportService` before releasing.

## Category and severity labels

The desktop app sends a `category` and `severity` with each report. The worker maps
category to a GitHub label on the created issue:

| Category   | GitHub label  |
|------------|---------------|
| connection | `connection`  |
| rewards    | `rewards`     |
| scaling    | `scaling`     |
| movement   | `movement`    |
| ui-theme   | `ui-theme`    |
| crash      | `crash`       |
| other      | (no label)    |

Severity adds a prefix to the issue title (`[Low]`, `[High]`, `[Crash]`; `normal` has no prefix).

Create these labels in `seluvia/crystal-relay-public` before deploying:
`connection`, `rewards`, `scaling`, `movement`, `ui-theme`, `crash`.

If a label is missing, the worker retries without labels so the report still succeeds.

## Payload caps

- Max payload: 56 KB
- Max diagnostics: 44 KB
- Snapshot: 2 KB, Activity log: 16 KB, Debug log: 16 KB, Crash log: 12 KB
