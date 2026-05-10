const CHANNEL_ID_PATTERN = /^[A-Za-z0-9_-]{8,80}$/;
const MAX_WEBHOOK_BYTES = 64 * 1024;
const ACK_TIMEOUT_MS = 9000;
const RECENT_EVENT_TTL_MS = 10 * 60 * 1000;
const RATE_WINDOW_MS = 60 * 1000;
const CHANNEL_RATE_LIMIT = 120;
const IP_RATE_LIMIT = 60;

export class KoFiRelayRoom {
  constructor(state, env) {
    this.state = state;
    this.env = env;
    this.client = null;
    this.clientAuthenticated = false;
    this.clientSecret = "";
    this.verificationToken = "";
    this.pendingAcks = new Map();
    this.recentEvents = new Map();
    this.channelHits = [];
    this.ipHits = new Map();
  }

  async fetch(request) {
    const url = new URL(request.url);
    if (url.pathname.includes("/connect/")) {
      return this.handleConnect(request);
    }

    if (url.pathname.includes("/webhook/")) {
      return this.handleWebhook(request);
    }

    return jsonResponse({ message: "Not found." }, 404);
  }

  handleConnect(request) {
    if (request.headers.get("Upgrade")?.toLowerCase() !== "websocket") {
      return jsonResponse({ message: "WebSocket upgrade required." }, 426);
    }

    const pair = new WebSocketPair();
    const [clientSocket, serverSocket] = Object.values(pair);
    serverSocket.accept();

    if (this.client && this.client.readyState === WebSocket.OPEN) {
      this.client.close(1012, "Replaced by a new Crystal Relay connection.");
    }

    this.client = serverSocket;
    this.clientAuthenticated = false;

    serverSocket.addEventListener("message", event => {
      void this.handleSocketMessage(serverSocket, event.data);
    });
    serverSocket.addEventListener("close", () => this.detachSocket(serverSocket));
    serverSocket.addEventListener("error", () => this.detachSocket(serverSocket));

    return new Response(null, {
      status: 101,
      webSocket: clientSocket
    });
  }

  async handleSocketMessage(socket, data) {
    if (socket !== this.client || typeof data !== "string") {
      return;
    }

    let message;
    try {
      message = JSON.parse(data);
    } catch {
      socket.close(1003, "Invalid relay message.");
      return;
    }

    if (message.type === "auth") {
      await this.handleAuthMessage(socket, message);
      return;
    }

    if (!this.clientAuthenticated) {
      socket.close(1008, "Relay authentication required.");
      return;
    }

    if (message.type === "ack" && typeof message.eventId === "string") {
      const pending = this.pendingAcks.get(message.eventId);
      if (pending) {
        pending(true);
      }
    }
  }

  async handleAuthMessage(socket, message) {
    const clientSecret = normalize(message.clientSecret);
    const verificationToken = normalize(message.verificationToken);
    const protocolVersion = Number(message.protocolVersion);
    if (protocolVersion !== 1 || clientSecret.length < 24 || verificationToken.length < 6) {
      socket.close(1008, "Invalid Ko-fi relay credentials.");
      return;
    }

    if (this.clientSecret.length > 0 && !(await constantTimeEquals(this.clientSecret, clientSecret))) {
      socket.close(1008, "Invalid Ko-fi relay client secret.");
      return;
    }

    this.clientSecret = clientSecret;
    this.verificationToken = verificationToken;
    this.clientAuthenticated = true;
    socket.send(JSON.stringify({ type: "ready", protocolVersion: 1 }));
  }

  detachSocket(socket) {
    if (socket !== this.client) {
      return;
    }

    this.client = null;
    this.clientAuthenticated = false;
    for (const resolve of this.pendingAcks.values()) {
      resolve(false);
    }

    this.pendingAcks.clear();
  }

  async handleWebhook(request) {
    if (request.method !== "POST") {
      return jsonResponse({ message: "Use POST for Ko-fi webhooks." }, 405);
    }

    this.pruneRecentEvents();
    const limit = this.enforceRateLimit(getRequestIp(request));
    if (!limit.ok) {
      return jsonResponse(
        { message: "Too many Ko-fi webhook requests." },
        429,
        { "Retry-After": String(limit.retryAfterSeconds) });
    }

    if (!this.isClientReady()) {
      return jsonResponse({ message: "Crystal Relay is offline." }, 503);
    }

    const body = await readLimitedText(request, MAX_WEBHOOK_BYTES);
    if (body === null) {
      return jsonResponse({ message: "Ko-fi webhook payload is too large." }, 413);
    }

    const payload = parseKoFiPayload(body);
    if (!payload.ok) {
      return jsonResponse({ message: payload.message }, payload.status);
    }

    const event = normalizeKoFiEvent(payload.value);
    if (!event.ok) {
      return jsonResponse({ message: event.message }, event.status);
    }

    if (!(await constantTimeEquals(this.verificationToken, event.verificationToken))) {
      return jsonResponse({ message: "Invalid Ko-fi verification token." }, 403);
    }

    if (this.recentEvents.has(event.value.eventId)) {
      return jsonResponse({ message: "duplicate" }, 200);
    }

    const delivered = await this.forwardEventAndWaitForAck(event.value);
    if (!delivered) {
      return jsonResponse({ message: "Crystal Relay did not acknowledge the Ko-fi event." }, 503);
    }

    this.recentEvents.set(event.value.eventId, Date.now());
    return jsonResponse({ message: "ok" }, 200);
  }

  isClientReady() {
    return this.client
      && this.client.readyState === WebSocket.OPEN
      && this.clientAuthenticated
      && this.verificationToken.length > 0;
  }

  async forwardEventAndWaitForAck(event) {
    if (!this.isClientReady()) {
      return false;
    }

    if (this.pendingAcks.has(event.eventId)) {
      return false;
    }

    let resolveAck;
    const ackPromise = new Promise(resolve => {
      resolveAck = resolve;
    });
    this.pendingAcks.set(event.eventId, resolveAck);

    try {
      this.client.send(JSON.stringify({
        type: "kofi.event",
        protocolVersion: 1,
        event
      }));
    } catch {
      this.pendingAcks.delete(event.eventId);
      return false;
    }

    const timeoutPromise = new Promise(resolve => {
      setTimeout(() => resolve(false), ACK_TIMEOUT_MS);
    });
    const acknowledged = await Promise.race([ackPromise, timeoutPromise]);
    this.pendingAcks.delete(event.eventId);
    return acknowledged === true;
  }

  enforceRateLimit(ip) {
    const now = Date.now();
    this.channelHits = pruneHits(this.channelHits, now);
    if (this.channelHits.length >= CHANNEL_RATE_LIMIT) {
      return { ok: false, retryAfterSeconds: secondsUntilWindowReset(this.channelHits, now) };
    }

    const hits = pruneHits(this.ipHits.get(ip) ?? [], now);
    if (hits.length >= IP_RATE_LIMIT) {
      this.ipHits.set(ip, hits);
      return { ok: false, retryAfterSeconds: secondsUntilWindowReset(hits, now) };
    }

    this.channelHits.push(now);
    hits.push(now);
    this.ipHits.set(ip, hits);
    return { ok: true };
  }

  pruneRecentEvents() {
    const cutoff = Date.now() - RECENT_EVENT_TTL_MS;
    for (const [eventId, seenAt] of this.recentEvents) {
      if (seenAt < cutoff) {
        this.recentEvents.delete(eventId);
      }
    }
  }
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/health") {
      return jsonResponse({ status: "ok" }, 200);
    }

    const route = parseRoute(url.pathname);
    if (!route.ok) {
      return jsonResponse({ message: route.message }, route.status);
    }

    const stub = env.KOFI_RELAY_ROOMS.getByName(route.channelId);
    return stub.fetch(request);
  }
};

function parseRoute(pathname) {
  const parts = pathname.split("/").filter(Boolean);
  if (parts.length !== 4 || parts[0] !== "v1" || parts[1] !== "kofi") {
    return { ok: false, status: 404, message: "Not found." };
  }

  if (parts[2] !== "webhook" && parts[2] !== "connect") {
    return { ok: false, status: 404, message: "Not found." };
  }

  const channelId = parts[3];
  if (!CHANNEL_ID_PATTERN.test(channelId)) {
    return { ok: false, status: 400, message: "Invalid Ko-fi relay channel." };
  }

  return { ok: true, channelId };
}

async function readLimitedText(request, maxBytes) {
  const contentLength = Number(request.headers.get("content-length") ?? "0");
  if (Number.isFinite(contentLength) && contentLength > maxBytes) {
    return null;
  }

  if (!request.body) {
    return "";
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
      return null;
    }

    chunks.push(value);
  }

  const combined = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    combined.set(chunk, offset);
    offset += chunk.byteLength;
  }

  return new TextDecoder().decode(combined);
}

function parseKoFiPayload(body) {
  const trimmed = normalize(body);
  if (trimmed.startsWith("{")) {
    return parseJson(trimmed);
  }

  const params = new URLSearchParams(trimmed);
  const data = params.get("data");
  if (!data) {
    return { ok: false, status: 400, message: "Ko-fi webhook data was missing." };
  }

  return parseJson(data);
}

function parseJson(json) {
  try {
    return { ok: true, value: JSON.parse(json) };
  } catch {
    return { ok: false, status: 400, message: "Ko-fi webhook data was not valid JSON." };
  }
}

function normalizeKoFiEvent(payload) {
  const verificationToken = normalize(payload.verification_token);
  const type = normalize(payload.type);
  if (!verificationToken) {
    return { ok: false, status: 400, message: "Ko-fi verification token was missing." };
  }

  if (type && type.toLowerCase() !== "donation" && type.toLowerCase() !== "tip") {
    return { ok: false, status: 200, message: "ignored" };
  }

  const amount = Number(payload.amount);
  if (!Number.isFinite(amount) || amount <= 0) {
    return { ok: false, status: 400, message: "Ko-fi amount was invalid." };
  }

  const eventId = normalize(payload.message_id)
    || normalize(payload.kofi_transaction_id)
    || crypto.randomUUID();

  return {
    ok: true,
    verificationToken,
    value: {
      provider: "KoFi",
      eventId,
      userDisplayName: normalize(payload.from_name) || "Ko-fi supporter",
      amount,
      currencyCode: normalize(payload.currency).slice(0, 8).toUpperCase(),
      message: normalize(payload.message),
      receivedAt: new Date().toISOString()
    }
  };
}

function pruneHits(hits, now) {
  const cutoff = now - RATE_WINDOW_MS;
  return hits.filter(hit => hit >= cutoff);
}

function secondsUntilWindowReset(hits, now) {
  if (hits.length === 0) {
    return 1;
  }

  return Math.max(1, Math.ceil((hits[0] + RATE_WINDOW_MS - now) / 1000));
}

function getRequestIp(request) {
  return request.headers.get("cf-connecting-ip")
    || request.headers.get("x-forwarded-for")
    || "unknown";
}

async function constantTimeEquals(left, right) {
  const leftBytes = new TextEncoder().encode(left);
  const rightBytes = new TextEncoder().encode(right);
  const length = Math.max(leftBytes.length, rightBytes.length);
  let difference = leftBytes.length ^ rightBytes.length;
  for (let index = 0; index < length; index += 1) {
    difference |= (leftBytes[index] ?? 0) ^ (rightBytes[index] ?? 0);
  }

  await crypto.subtle.digest("SHA-256", leftBytes);
  return difference === 0;
}

function normalize(value) {
  return typeof value === "string" ? value.trim() : "";
}

function jsonResponse(payload, status, extraHeaders = {}) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      ...extraHeaders,
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store"
    }
  });
}
