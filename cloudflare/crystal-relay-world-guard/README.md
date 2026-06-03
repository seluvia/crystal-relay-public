# Crystal Relay World Guard Worker

Private Cloudflare Worker for Crystal Relay's protected VRChat world guard.

## Setup

1. Create a KV namespace:

   ```powershell
   npx wrangler kv namespace create WORLD_GUARD
   ```

   If Wrangler cannot detect the Cloudflare account, set `CLOUDFLARE_ACCOUNT_ID`
   first or add `account_id` to `wrangler.toml`.

2. Put the returned namespace id in `wrangler.toml`.

3. Store the admin secret as a Worker secret. Use a long random password.

   ```powershell
   npx wrangler secret put WORLD_GUARD_ADMIN_SECRET
   ```

4. Deploy:

   ```powershell
   npx wrangler deploy
   ```

Crystal Relay checks:

```text
https://crystal-relay-world-guard.screminpal-animation.workers.dev/api/check
```

Admin edits are made at:

```text
https://crystal-relay-world-guard.screminpal-animation.workers.dev/admin
```

The admin secret stays in Cloudflare and must not be committed.
