# Crystal Relay Live Worker

Temporary Cloudflare Worker backend for Crystal Relay Live Feedback Heartbeat.

The Worker does not use Twitch OAuth, Twitch APIs, user accounts, Twitch tokens, VRChat credentials, OSC data, chat messages, or permanent live-state storage. It stores only temporary live heartbeat entries in KV with a 75-minute TTL.

## Setup

1. Install dependencies:

```powershell
npm install
```

2. Login to Wrangler:

```powershell
npx wrangler login
```

3. Create the KV namespace:

```powershell
npx wrangler kv namespace create LIVE_USERS
```

4. Put the returned KV namespace ID into `wrangler.toml`:

```toml
[[kv_namespaces]]
binding = "LIVE_USERS"
id = "REPLACE_WITH_KV_NAMESPACE_ID"
```

5. Run locally:

```powershell
npx wrangler dev
```

6. Deploy:

```powershell
npx wrangler deploy
```

7. Copy the deployed Worker URL into Crystal Relay's runtime config:

```json
{
  "liveFeedbackHeartbeatEndpoint": "https://your-worker.example.workers.dev"
}
```

Crystal Relay will send heartbeat pings to `/api/ping`. The developer live page is available at the Worker root, and the JSON live list is available at `/api/live`.
