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
