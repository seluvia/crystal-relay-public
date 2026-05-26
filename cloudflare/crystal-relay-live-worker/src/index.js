const HEARTBEAT_TTL_SECONDS = 75 * 60;
const PUBLIC_PAGE_REFRESH_MS = 5 * 60 * 1000;
const LIVE_PREFIX = "live:";
const LIVE_INDEX_KEY = "live-index";
const LIVE_INDEX_REPAIR_THROTTLE_KEY = "live-index-repair-throttle";
const LIVE_INDEX_REPAIR_THROTTLE_SECONDS = 5 * 60;
const LIVE_INDEX_REPAIR_LIST_LIMIT = 100;
const LIVE_INDEX_REPAIR_MAX_PAGES = 3;
const LIVE_INDEX_REPAIR_MAX_KEYS = LIVE_INDEX_REPAIR_LIST_LIMIT * LIVE_INDEX_REPAIR_MAX_PAGES;
const MAX_PING_BYTES = 4 * 1024;
const MAX_DISPLAY_NAME_LENGTH = 80;
const MAX_OPTIONAL_FIELD_LENGTH = 40;
const TWITCH_CHANNEL_PATTERN = /^[a-z0-9_]{3,30}$/;
const UNSUPPORTED_TWITCH_PATHS = new Set([
  "videos",
  "directory",
  "settings",
  "login",
  "signup",
  "p",
  "popout",
  "moderator"
]);

const CORS_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type"
};

export default {
  async fetch(request, env, ctx) {
    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: CORS_HEADERS });
    }

    const url = new URL(request.url);
    if (url.pathname === "/api/ping") {
      return handlePing(request, env);
    }

    if (url.pathname === "/api/live") {
      return handleLive(env, ctx);
    }

    if (url.pathname === "/") {
      return htmlResponse(renderLivePage());
    }

    return jsonResponse({ message: "Not found." }, 404);
  }
};

async function handlePing(request, env) {
  if (request.method !== "POST") {
    return jsonResponse({ message: "Use POST to send a live feedback heartbeat." }, 405);
  }

  if (!env.LIVE_USERS) {
    return jsonResponse({ message: "Live feedback storage is not configured." }, 503);
  }

  const parsed = await readLimitedJson(request, MAX_PING_BYTES);
  if (!parsed.ok) {
    return jsonResponse({ message: parsed.message }, parsed.status);
  }

  const validation = validatePingPayload(parsed.value);
  if (!validation.ok) {
    return jsonResponse({ message: validation.message }, 400);
  }

  const now = new Date();
  const normalizedUrl = validation.value.twitchUrl;
  const key = `${LIVE_PREFIX}${await sha256Hex(normalizedUrl)}`;

  if (!validation.value.isLive) {
    await env.LIVE_USERS.delete(key);
    await tryRemoveLiveIndexEntry(env, key, now);
    return jsonResponse({ message: "ok" }, 200);
  }

  const expiresAt = new Date(now.getTime() + HEARTBEAT_TTL_SECONDS * 1000);
  const liveEntry = {
    displayName: validation.value.displayName,
    twitchUrl: normalizedUrl,
    relayVersion: validation.value.relayVersion,
    buildChannel: validation.value.buildChannel,
    lastPingAt: now.toISOString(),
    expiresAt: expiresAt.toISOString()
  };
  await env.LIVE_USERS.put(
    key,
    JSON.stringify(liveEntry),
    { expirationTtl: HEARTBEAT_TTL_SECONDS });
  await tryUpsertLiveIndexEntry(env, key, liveEntry, now);

  return jsonResponse({ message: "ok" }, 200);
}

async function handleLive(env, ctx) {
  if (!env.LIVE_USERS) {
    return jsonResponse({ message: "Live feedback storage is not configured." }, 503);
  }

  const now = new Date();
  const users = [];
  const index = await readLiveIndex(env, now);
  if (index.needsRepair) {
    return respondWithLiveEntries(await repairLiveIndex(env, ctx, now), now);
  }

  const cleanEntries = [];
  const invalidKeys = new Set();
  let indexNeedsCleanup = false;

  for (const entry of index.entries) {
    const liveEntry = normalizeLiveIndexEntry(entry, now);
    if (!liveEntry.ok) {
      indexNeedsCleanup = true;
      if (liveEntry.key) {
        invalidKeys.add(liveEntry.key);
      }

      continue;
    }

    users.push(liveEntry.user);
    cleanEntries.push(liveEntry.indexEntry);
  }

  if (indexNeedsCleanup) {
    scheduleBackground(ctx, () => cleanLiveIndex(env, cleanEntries, invalidKeys, now));
  }

  return respondWithLiveUsers(users, now);
}

function respondWithLiveEntries(entries, now) {
  const users = [];
  for (const entry of entries) {
    const liveEntry = normalizeLiveIndexEntry(entry, now);
    if (liveEntry.ok) {
      users.push(liveEntry.user);
    }
  }

  return respondWithLiveUsers(users, now);
}

function respondWithLiveUsers(users, now) {
  users.sort((left, right) => {
    const displayComparison = left.displayName.localeCompare(right.displayName, undefined, { sensitivity: "base" });
    return displayComparison !== 0
      ? displayComparison
      : left.twitchUrl.localeCompare(right.twitchUrl, undefined, { sensitivity: "base" });
  });

  return jsonResponse({
    updatedAt: now.toISOString(),
    count: users.length,
    users
  }, 200);
}

async function tryUpsertLiveIndexEntry(env, key, entry, now) {
  try {
    await upsertLiveIndexEntry(env, key, entry, now);
  } catch {
    // The heartbeat itself should remain usable if the developer live index cannot be refreshed.
  }
}

async function tryRemoveLiveIndexEntry(env, key, now) {
  try {
    await removeLiveIndexEntry(env, key, now);
  } catch {
    // A stale index entry is temporary and will be filtered by /api/live.
  }
}

async function upsertLiveIndexEntry(env, key, entry, now) {
  const index = await readLiveIndex(env, now);
  const entries = [];
  for (const existingEntry of index.entries) {
    const normalized = normalizeLiveIndexEntry(existingEntry, now);
    if (normalized.ok && normalized.key !== key) {
      entries.push(normalized.indexEntry);
    }
  }

  entries.push(createLiveIndexEntry(key, entry));
  await writeLiveIndex(env, entries, now);
}

async function removeLiveIndexEntry(env, key, now) {
  const index = await readLiveIndex(env, now);
  const entries = [];
  for (const existingEntry of index.entries) {
    const normalized = normalizeLiveIndexEntry(existingEntry, now);
    if (normalized.ok && normalized.key !== key) {
      entries.push(normalized.indexEntry);
    }
  }

  await writeLiveIndex(env, entries, now);
}

async function readLiveIndex(env, now) {
  try {
    const value = await env.LIVE_USERS.get(LIVE_INDEX_KEY, "json");
    if (!value) {
      return { entries: [], needsRepair: true };
    }

    if (typeof value !== "object" || Array.isArray(value) || !Array.isArray(value.entries)) {
      return { entries: [], needsRepair: true };
    }

    if (isLiveIndexExpired(value.updatedAt, now)) {
      return { entries: [], needsRepair: true };
    }

    return { entries: value.entries, needsRepair: false };
  } catch {
    return { entries: [], needsRepair: true };
  }
}

async function repairLiveIndex(env, ctx, now) {
  if (await isLiveIndexRepairThrottled(env)) {
    return [];
  }

  let keys;
  try {
    keys = await listLiveKeysForRepair(env);
  } catch {
    await trySetLiveIndexRepairThrottle(env, now);
    return [];
  }

  const results = await Promise.all(keys.map(key => readLiveKeyForIndex(env, key, now)));
  const entries = [];
  const invalidKeys = new Set();
  for (const result of results) {
    if (result.ok) {
      entries.push(result.indexEntry);
    } else if (result.key) {
      invalidKeys.add(result.key);
    }
  }

  try {
    await writeLiveIndex(env, entries, now);
  } catch {
    await trySetLiveIndexRepairThrottle(env, now);
  }

  if (invalidKeys.size > 0) {
    scheduleBackground(ctx, () => deleteLiveKeys(env, invalidKeys));
  }

  return entries;
}

async function listLiveKeysForRepair(env) {
  const keys = [];
  let cursor;
  let pages = 0;
  do {
    const listed = await env.LIVE_USERS.list({
      prefix: LIVE_PREFIX,
      cursor,
      limit: LIVE_INDEX_REPAIR_LIST_LIMIT
    });
    for (const key of listed.keys ?? []) {
      if (typeof key.name === "string" && key.name.startsWith(LIVE_PREFIX)) {
        keys.push(key.name);
      }

      if (keys.length >= LIVE_INDEX_REPAIR_MAX_KEYS) {
        return keys;
      }
    }

    cursor = listed.cursor;
    pages++;
  } while (cursor && pages < LIVE_INDEX_REPAIR_MAX_PAGES);

  return keys;
}

async function readLiveKeyForIndex(env, key, now) {
  let value;
  try {
    value = await env.LIVE_USERS.get(key, "json");
  } catch {
    return { ok: false, key };
  }

  const user = normalizeLiveEntry(value, now);
  if (!user.ok) {
    return { ok: false, key };
  }

  return {
    ok: true,
    indexEntry: createLiveIndexEntry(key, {
      ...user.value,
      expiresAt: normalizeText(value.expiresAt, 80)
    })
  };
}

async function isLiveIndexRepairThrottled(env) {
  try {
    return await env.LIVE_USERS.get(LIVE_INDEX_REPAIR_THROTTLE_KEY) !== null;
  } catch {
    return false;
  }
}

async function trySetLiveIndexRepairThrottle(env, now) {
  try {
    await env.LIVE_USERS.put(
      LIVE_INDEX_REPAIR_THROTTLE_KEY,
      JSON.stringify({ updatedAt: now.toISOString() }),
      { expirationTtl: LIVE_INDEX_REPAIR_THROTTLE_SECONDS });
  } catch {
    // If the throttle key cannot be written, /api/live still returns a safe empty list.
  }
}

function isLiveIndexExpired(updatedAt, now) {
  const value = normalizeText(updatedAt, 80);
  if (!value) {
    return false;
  }

  const date = new Date(value);
  return Number.isFinite(date.getTime())
    && now.getTime() - date.getTime() > HEARTBEAT_TTL_SECONDS * 1000;
}

async function writeLiveIndex(env, entries, now) {
  await env.LIVE_USERS.put(
    LIVE_INDEX_KEY,
    JSON.stringify({
      updatedAt: now.toISOString(),
      entries
    }),
    { expirationTtl: HEARTBEAT_TTL_SECONDS });
}

async function cleanLiveIndex(env, entries, invalidKeys, now) {
  await writeLiveIndex(env, entries, now);
  await deleteLiveKeys(env, invalidKeys);
}

async function deleteLiveKeys(env, keys) {
  await Promise.allSettled([...keys].map(key => env.LIVE_USERS.delete(key)));
}

function createLiveIndexEntry(key, entry) {
  return {
    key,
    displayName: entry.displayName,
    twitchUrl: entry.twitchUrl,
    relayVersion: entry.relayVersion,
    buildChannel: entry.buildChannel,
    lastPingAt: entry.lastPingAt,
    expiresAt: entry.expiresAt
  };
}

function normalizeLiveIndexEntry(entry, now) {
  if (!entry || typeof entry !== "object" || Array.isArray(entry)) {
    return { ok: false };
  }

  const key = normalizeLiveIndexKey(entry.key);
  if (!key) {
    return { ok: false };
  }

  const user = normalizeLiveEntry(entry, now);
  if (!user.ok) {
    return { ok: false, key };
  }

  return {
    ok: true,
    key,
    user: user.value,
    indexEntry: createLiveIndexEntry(key, {
      ...user.value,
      expiresAt: normalizeText(entry.expiresAt, 80)
    })
  };
}

function normalizeLiveIndexKey(value) {
  return typeof value === "string" && value.startsWith(LIVE_PREFIX) ? value : "";
}

function scheduleBackground(ctx, createPromise) {
  if (!ctx || typeof ctx.waitUntil !== "function") {
    return;
  }

  ctx.waitUntil(createPromise().catch(() => undefined));
}

function validatePingPayload(payload) {
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
    return { ok: false, message: "Heartbeat payload must be a JSON object." };
  }

  const displayName = normalizeText(payload.displayName, MAX_DISPLAY_NAME_LENGTH);
  if (!displayName) {
    return { ok: false, message: "displayName is required." };
  }

  const twitchUrl = normalizeTwitchUrl(payload.twitchUrl);
  if (!twitchUrl.ok) {
    return { ok: false, message: twitchUrl.message };
  }

  if (typeof payload.isLive !== "boolean") {
    return { ok: false, message: "isLive must be true or false." };
  }

  return {
    ok: true,
    value: {
      displayName,
      twitchUrl: twitchUrl.value,
      isLive: payload.isLive,
      relayVersion: normalizeText(payload.relayVersion, MAX_OPTIONAL_FIELD_LENGTH),
      buildChannel: normalizeText(payload.buildChannel, MAX_OPTIONAL_FIELD_LENGTH)
    }
  };
}

function normalizeLiveEntry(value, now) {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return { ok: false };
  }

  const expiresAt = new Date(normalizeText(value.expiresAt, 80));
  if (!Number.isFinite(expiresAt.getTime()) || expiresAt <= now) {
    return { ok: false, expired: true };
  }

  const displayName = normalizeText(value.displayName, MAX_DISPLAY_NAME_LENGTH);
  const twitchUrl = normalizeTwitchUrl(value.twitchUrl);
  const lastPingAt = normalizeText(value.lastPingAt, 80);
  if (!displayName || !twitchUrl.ok || !lastPingAt) {
    return { ok: false };
  }

  return {
    ok: true,
    value: {
      displayName,
      twitchUrl: twitchUrl.value,
      relayVersion: normalizeText(value.relayVersion, MAX_OPTIONAL_FIELD_LENGTH),
      buildChannel: normalizeText(value.buildChannel, MAX_OPTIONAL_FIELD_LENGTH),
      lastPingAt
    }
  };
}

function normalizeTwitchUrl(value) {
  if (typeof value !== "string" || !value.trim()) {
    return { ok: false, message: "twitchUrl is required." };
  }

  let url;
  try {
    url = new URL(value.trim());
  } catch {
    return { ok: false, message: "twitchUrl must be a valid Twitch channel URL." };
  }

  const host = url.hostname.toLowerCase();
  if (host !== "twitch.tv" && host !== "www.twitch.tv") {
    return { ok: false, message: "Only twitch.tv channel URLs are accepted." };
  }

  if (url.username || url.password) {
    return { ok: false, message: "Twitch URLs with credentials are not accepted." };
  }

  const parts = url.pathname.split("/").filter(Boolean);
  if (parts.length !== 1) {
    return { ok: false, message: "Only normal Twitch channel URLs are accepted." };
  }

  const channel = parts[0].toLowerCase();
  if (UNSUPPORTED_TWITCH_PATHS.has(channel) || !TWITCH_CHANNEL_PATTERN.test(channel)) {
    return { ok: false, message: "Unsupported Twitch URL path." };
  }

  return { ok: true, value: `https://www.twitch.tv/${channel}` };
}

async function readLimitedJson(request, maxBytes) {
  const contentLength = Number(request.headers.get("content-length") ?? "0");
  if (Number.isFinite(contentLength) && contentLength > maxBytes) {
    return { ok: false, status: 413, message: "Heartbeat payload is too large." };
  }

  if (!request.body) {
    return { ok: false, status: 400, message: "Heartbeat payload was missing." };
  }

  const reader = request.body.getReader();
  const chunks = [];
  let total = 0;
  while (true) {
    const { value, done } = await reader.read();
    if (done) {
      break;
    }

    total += value.byteLength;
    if (total > maxBytes) {
      return { ok: false, status: 413, message: "Heartbeat payload is too large." };
    }

    chunks.push(value);
  }

  const combined = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    combined.set(chunk, offset);
    offset += chunk.byteLength;
  }

  try {
    return {
      ok: true,
      value: JSON.parse(new TextDecoder().decode(combined))
    };
  } catch {
    return { ok: false, status: 400, message: "Heartbeat payload was not valid JSON." };
  }
}

async function sha256Hex(value) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
}

function normalizeText(value, maxLength) {
  if (typeof value !== "string") {
    return "";
  }

  const normalized = value.trim();
  return normalized.length <= maxLength ? normalized : normalized.slice(0, maxLength);
}

function renderLivePage() {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Crystal Relay Live Feedback</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #10131d;
      --panel: #171c29;
      --panel-2: #202738;
      --text: #f4f6fb;
      --muted: #aeb8ca;
      --accent: #6fe7d3;
      --border: #354055;
      --danger: #ff7f9b;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      font-family: "Segoe UI", system-ui, sans-serif;
      background: var(--bg);
      color: var(--text);
    }
    main {
      width: min(960px, calc(100% - 32px));
      margin: 0 auto;
      padding: 32px 0;
    }
    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 20px;
    }
    h1 {
      margin: 0;
      font-size: clamp(1.7rem, 3vw, 2.5rem);
      font-weight: 750;
    }
    .status {
      color: var(--muted);
      margin: 6px 0 0;
    }
    button, .card {
      border: 1px solid var(--border);
      border-radius: 8px;
    }
    button {
      background: var(--panel-2);
      color: var(--text);
      font: inherit;
      font-weight: 700;
      padding: 10px 16px;
      cursor: pointer;
    }
    button:hover { border-color: var(--accent); }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
      gap: 12px;
    }
    .card {
      display: block;
      padding: 16px;
      background: var(--panel);
      color: inherit;
      text-decoration: none;
    }
    .card:hover { border-color: var(--accent); }
    .name {
      font-size: 1.12rem;
      font-weight: 800;
      margin-bottom: 8px;
    }
    .url {
      color: var(--accent);
      overflow-wrap: anywhere;
    }
    .meta {
      color: var(--muted);
      font-size: 0.92rem;
      margin-top: 10px;
      line-height: 1.45;
    }
    .empty, .error {
      padding: 22px;
      border: 1px solid var(--border);
      border-radius: 8px;
      background: var(--panel);
      color: var(--muted);
    }
    .error { color: var(--danger); }
  </style>
</head>
<body>
  <main>
    <header>
      <div>
        <h1>Crystal Relay Live Feedback</h1>
        <p class="status" id="status">Loading live users...</p>
      </div>
      <button id="refresh" type="button">Refresh</button>
    </header>
    <section id="content" class="grid" aria-live="polite"></section>
  </main>
  <script>
    const refreshMs = ${PUBLIC_PAGE_REFRESH_MS};
    const status = document.getElementById("status");
    const content = document.getElementById("content");
    document.getElementById("refresh").addEventListener("click", loadLiveUsers);
    async function loadLiveUsers() {
      status.textContent = "Refreshing...";
      try {
        const response = await fetch("/api/live", { cache: "no-store" });
        if (!response.ok) {
          throw new Error("Live list returned HTTP " + response.status);
        }
        render(await response.json());
      } catch (error) {
        content.className = "";
        content.textContent = "";
        const message = document.createElement("div");
        message.className = "error";
        message.textContent = "Could not load the live list. Try again in a moment.";
        content.append(message);
        status.textContent = "Last refresh failed.";
      }
    }
    function render(data) {
      content.textContent = "";
      content.className = data.users && data.users.length > 0 ? "grid" : "";
      status.textContent = "Updated " + new Date(data.updatedAt).toLocaleString();
      if (!data.users || data.users.length === 0) {
        const empty = document.createElement("div");
        empty.className = "empty";
        empty.textContent = "No Crystal Relay users are live right now";
        content.append(empty);
        return;
      }
      for (const user of data.users) {
        const card = document.createElement("a");
        card.className = "card";
        card.href = user.twitchUrl;
        card.target = "_blank";
        card.rel = "noopener noreferrer";

        const name = document.createElement("div");
        name.className = "name";
        name.textContent = user.displayName;
        card.append(name);

        const url = document.createElement("div");
        url.className = "url";
        url.textContent = user.twitchUrl;
        card.append(url);

        const metaParts = [];
        if (user.relayVersion) metaParts.push("Crystal Relay " + user.relayVersion);
        if (user.buildChannel) metaParts.push(user.buildChannel);
        if (user.lastPingAt) metaParts.push("Last heartbeat " + new Date(user.lastPingAt).toLocaleString());
        if (metaParts.length > 0) {
          const meta = document.createElement("div");
          meta.className = "meta";
          meta.textContent = metaParts.join(" | ");
          card.append(meta);
        }

        content.append(card);
      }
    }
    loadLiveUsers();
    setInterval(loadLiveUsers, refreshMs);
  </script>
</body>
</html>`;
}

function jsonResponse(payload, status) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      ...CORS_HEADERS,
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store"
    }
  });
}

function htmlResponse(html) {
  return new Response(html, {
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      "Cache-Control": "no-store"
    }
  });
}
