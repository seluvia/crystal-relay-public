# Crystal Relay Ko-fi Relay Worker

This Worker gives Crystal Relay a hosted Ko-fi webhook endpoint without asking
streamers to set up Cloudflare Tunnel, ngrok, router ports, or a public local URL.

## Routes

```text
POST /v1/kofi/webhook/{channelId}
GET  /v1/kofi/connect/{channelId}
GET  /health
```

The desktop app connects to `/v1/kofi/connect/{channelId}` over WebSocket and
authenticates with its local client secret plus the streamer's Ko-fi verification
token. Ko-fi sends webhooks to `/v1/kofi/webhook/{channelId}`.

The relay keeps payment events in memory only. It does not store payment history,
emails, messages, tokens, client secrets, or raw payloads.

## Deploy

1. Deploy to the Crystal Relay Cloudflare account or another account that will host
   the public relay.
2. Deploy from this folder:

   ```powershell
   npx wrangler deploy
   ```

3. In Crystal Relay, the current hosted relay base URL is:

   ```text
   https://crystal-relay-kofi-relay.screminpal-animation.workers.dev
   ```

   `https://relay.crystalrelay.app` can replace this later after the
   `crystalrelay.app` DNS zone is added to Cloudflare and routed to this Worker.

Use `npx wrangler deploy --dry-run` to validate the Worker bundle before release.
