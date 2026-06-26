const GITHUB_OWNER = "seluvia";
const GITHUB_REPO = "crystal-relay-public";
const ISSUE_LABELS = ["bug", "from-crystal-relay", "needs-triage"];
const MAX_PAYLOAD_BYTES = 56 * 1024;
const MAX_DIAGNOSTICS_LENGTH = 44 * 1024;
const TRIMMED_MARKER = "[trimmed]";
const HOUR_LIMIT = 10;
const DAY_LIMIT = 30;
const DESKTOP_CLIENT_HEADER = "X-Crystal-Relay-Client";
const DESKTOP_CLIENT_VALUE = "CrystalRelayDesktop";
const APP_VERSION_HEADER = "X-Crystal-Relay-Version";

const CATEGORY_LABELS = {
  "connection": "connection",
  "rewards": "rewards",
  "scaling": "scaling",
  "movement": "movement",
  "ui-theme": "ui-theme",
  "crash": "crash",
  "other": null
};

const SEVERITY_PREFIX = {
  "low": "[Low]",
  "normal": "",
  "high": "[High]",
  "crash": "[Crash]"
};

export default {
  async fetch(request, env) {
    if (request.method === "OPTIONS") {
      return jsonResponse({ message: "Browser bug report requests are not accepted." }, 403);
    }

    if (request.method !== "POST") {
      return jsonResponse({ message: "Use POST to submit a bug report." }, 405);
    }

    const accessValidation = validateDesktopClientRequest(request);
    if (!accessValidation.ok) {
      return jsonResponse({ message: accessValidation.message }, 403);
    }

    if (!env.GITHUB_TOKEN) {
      return jsonResponse({ message: "Bug report service is not configured." }, 503);
    }

    if (!env.BUG_REPORT_RATE_LIMIT) {
      return jsonResponse({ message: "Bug report rate limit storage is not configured." }, 503);
    }

    const contentLength = Number(request.headers.get("content-length") ?? "0");
    if (contentLength > MAX_PAYLOAD_BYTES) {
      return jsonResponse({ message: "Bug report is too large." }, 413);
    }

    let payload;
    try {
      payload = await request.json();
    } catch {
      return jsonResponse({ message: "Bug report payload was not valid JSON." }, 400);
    }

    const validation = validatePayload(payload);
    if (!validation.ok) {
      return jsonResponse({ message: validation.message }, 400);
    }

    const clientKey = await getClientKey(request);
    const limitResult = await enforceRateLimits(env, clientKey);
    if (!limitResult.ok) {
      return jsonResponse(
        {
          message: limitResult.message,
          retryAfterSeconds: limitResult.retryAfterSeconds
        },
        429,
        { "Retry-After": String(limitResult.retryAfterSeconds) });
    }

    const issue = buildGitHubIssue(payload);
    const createResult = await createIssue(env.GITHUB_TOKEN, issue);
    if (!createResult.ok && createResult.retryWithoutLabels) {
      const fallbackResult = await createIssue(env.GITHUB_TOKEN, { ...issue, labels: undefined });
      if (fallbackResult.ok) {
        return jsonResponse({ issueUrl: fallbackResult.issueUrl }, 201);
      }

      return jsonResponse({ message: fallbackResult.message }, 502);
    }

    if (!createResult.ok) {
      return jsonResponse({ message: createResult.message }, 502);
    }

    return jsonResponse({ issueUrl: createResult.issueUrl }, 201);
  }
};

function validatePayload(payload) {
  const title = normalize(payload.title);
  const whatHappened = normalize(payload.whatHappened);
  const expectedBehavior = normalize(payload.expectedBehavior);
  const stepsToReproduce = normalize(payload.stepsToReproduce);
  const category = normalize(payload.category) || "other";
  const severity = normalize(payload.severity) || "normal";

  if (!isInRange(title, 8, 120)) {
    return { ok: false, message: "Bug title must be 8 to 120 characters." };
  }

  if (!isInRange(whatHappened, 20, 5000)) {
    return { ok: false, message: "What happened must be 20 to 5000 characters." };
  }

  if (!isInRange(expectedBehavior, 20, 5000)) {
    return { ok: false, message: "Expected behavior must be 20 to 5000 characters." };
  }

  if (!isInRange(stepsToReproduce, 20, 5000)) {
    return { ok: false, message: "Steps to reproduce must be 20 to 5000 characters." };
  }

  if (!isInRange(category, 1, 40)) {
    return { ok: false, message: "Category is missing." };
  }

  if (!isInRange(severity, 1, 40)) {
    return { ok: false, message: "Severity is missing." };
  }

  return { ok: true };
}

function validateDesktopClientRequest(request) {
  if (request.headers.has("origin") || request.headers.has("referer")) {
    return { ok: false, message: "Browser bug report requests are not accepted." };
  }

  const client = request.headers.get(DESKTOP_CLIENT_HEADER);
  if (client !== DESKTOP_CLIENT_VALUE) {
    return { ok: false, message: "Bug reports must be sent from Crystal Relay." };
  }

  const version = normalize(request.headers.get(APP_VERSION_HEADER));
  if (!isInRange(version, 1, 80)) {
    return { ok: false, message: "Crystal Relay version header is missing." };
  }

  return { ok: true };
}

function buildGitHubIssue(payload) {
  const title = normalize(payload.title);
  const appVersion = normalize(payload.appVersion) || "Unknown";
  const contactName = normalize(payload.contactName) || "Not provided";
  const submittedAtUtc = normalize(payload.submittedAtUtc) || new Date().toISOString();
  const category = normalize(payload.category) || "other";
  const severity = normalize(payload.severity) || "normal";
  const severityPrefix = SEVERITY_PREFIX[severity] ?? "";
  const snapshot = trimUtf8(sanitize(normalize(payload.snapshot)), 2 * 1024);
  const activityLog = payload.activityLog ? trimUtf8(sanitize(normalize(payload.activityLog)), 16 * 1024) : null;
  const debugLog = payload.debugLog ? trimUtf8(sanitize(normalize(payload.debugLog)), 16 * 1024) : null;
  const crashLog = payload.crashLog ? trimUtf8(sanitize(normalize(payload.crashLog)), 12 * 1024) : null;

  const body = [
    "## Bug Report",
    "",
    `**Category:** ${category}`,
    `**Severity:** ${severity}`,
    `**App version:** ${appVersion}`,
    `**Submitted at:** ${submittedAtUtc}`,
    `**Contact:** ${contactName}`,
    "",
    "## What happened",
    "",
    sanitize(normalize(payload.whatHappened)),
    "",
    "## Expected behavior",
    "",
    sanitize(normalize(payload.expectedBehavior)),
    "",
    "## Steps to reproduce",
    "",
    sanitize(normalize(payload.stepsToReproduce)),
    "",
    "## Live status snapshot",
    "",
    snapshot.length > 0 ? `\`\`\`text\n${snapshot}\n\`\`\`` : "Not included.",
    "",
    "## Activity log",
    "",
    activityLog && activityLog.length > 0 ? `\`\`\`text\n${activityLog}\n\`\`\`` : "Not included.",
    "",
    "## Debug logs",
    "",
    debugLog && debugLog.length > 0 ? `\`\`\`text\n${debugLog}\n\`\`\`` : "Not included.",
    "",
    "## Crash log",
    "",
    crashLog && crashLog.length > 0 ? `\`\`\`text\n${crashLog}\n\`\`\`` : "Not included."
  ].join("\n");

  const baseLabels = ISSUE_LABELS;
  const categoryLabel = CATEGORY_LABELS[category] ?? null;
  const labels = categoryLabel ? [...baseLabels, categoryLabel] : baseLabels;

  return {
    title: `[Bug] ${severityPrefix} ${title}`.replace(/\s+/g, " ").trim(),
    body,
    labels
  };
}

async function createIssue(githubToken, issue) {
  const response = await fetch(`https://api.github.com/repos/${GITHUB_OWNER}/${GITHUB_REPO}/issues`, {
    method: "POST",
    headers: {
      "Accept": "application/vnd.github+json",
      "Authorization": `Bearer ${githubToken}`,
      "Content-Type": "application/json",
      "User-Agent": "Crystal-Relay-Bug-Report-Worker",
      "X-GitHub-Api-Version": "2022-11-28"
    },
    body: JSON.stringify(issue)
  });

  let body = {};
  try {
    body = await response.json();
  } catch {
    body = {};
  }

  if (response.ok && body.html_url) {
    return { ok: true, issueUrl: body.html_url };
  }

  return {
    ok: false,
    retryWithoutLabels: response.status === 422 && Array.isArray(issue.labels) && issue.labels.length > 0,
    message: body.message || `GitHub returned HTTP ${response.status}.`
  };
}

async function enforceRateLimits(env, clientKey) {
  const now = new Date();
  const hourKey = `bug-report:hour:${clientKey}:${formatUtcHour(now)}`;
  const dayKey = `bug-report:day:${clientKey}:${formatUtcDay(now)}`;

  const hour = await incrementRateLimit(env.BUG_REPORT_RATE_LIMIT, hourKey, HOUR_LIMIT, 3700);
  if (!hour.ok) {
    return {
      ok: false,
      message: "Too many bug reports were sent from this network.",
      retryAfterSeconds: secondsUntilNextUtcHour(now)
    };
  }

  const day = await incrementRateLimit(env.BUG_REPORT_RATE_LIMIT, dayKey, DAY_LIMIT, 90000);
  if (!day.ok) {
    return {
      ok: false,
      message: "Daily bug report limit reached from this network.",
      retryAfterSeconds: secondsUntilNextUtcDay(now)
    };
  }

  return { ok: true };
}

async function incrementRateLimit(kv, key, limit, ttlSeconds) {
  const current = Number(await kv.get(key) ?? "0");
  if (current >= limit) {
    return { ok: false };
  }

  await kv.put(key, String(current + 1), { expirationTtl: ttlSeconds });
  return { ok: true };
}

async function getClientKey(request) {
  const ip = request.headers.get("cf-connecting-ip")
    || request.headers.get("x-forwarded-for")
    || "unknown";
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(ip));
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
}

function sanitize(value) {
  return value
    .replace(/\b(access[_-]?token|refresh[_-]?token|client[_-]?secret|device[_-]?code|user[_-]?code|authorization|set-cookie|authcookie|twofactorauth|vrchat[-_ ]?auth|cookie)\b\s*[:=]\s*([^\r\n;]+)/gi, "$1=[redacted]")
    .replace(/\bBearer\s+[A-Za-z0-9._~+/=-]+/gi, "Bearer [redacted]")
    .replace(/C:\\Users\\[^\\\r\n]+/gi, "C:\\Users\\<user>")
    .replace(/(\s+in\s+)[A-Z]:\\[^\r\n]*\\([^\\\r\n:]+(?:\.cs|\.xaml|\.js|\.ts|\.json|\.xml|\.ps1|\.txt))(:line\s+\d+)/gi, "$1<local path>\\$2$3")
    .replace(/(asks for a code,\s*use\s+)[A-Z0-9-]{4,}/gi, "$1[redacted]");
}

function trimUtf8(value, maxBytes) {
  if (maxBytes <= 0 || value.length === 0) {
    return "";
  }

  if (utf8ByteLength(value) <= maxBytes) {
    return value;
  }

  const marker = `\n${TRIMMED_MARKER}`;
  const markerBytes = utf8ByteLength(marker);
  if (markerBytes >= maxBytes) {
    return trimMarker(marker, maxBytes);
  }

  const contentBudget = maxBytes - markerBytes;
  let result = "";
  let byteCount = 0;
  for (const char of value) {
    const charBytes = utf8ByteLength(char);
    if (byteCount + charBytes > contentBudget) {
      break;
    }

    result += char;
    byteCount += charBytes;
  }

  return `${result.trimEnd()}${marker}`;
}

function trimMarker(marker, maxBytes) {
  let result = "";
  let byteCount = 0;
  for (const char of marker) {
    const charBytes = utf8ByteLength(char);
    if (byteCount + charBytes > maxBytes) {
      break;
    }

    result += char;
    byteCount += charBytes;
  }

  return result;
}

function utf8ByteLength(value) {
  return new TextEncoder().encode(value).length;
}

function normalize(value) {
  return typeof value === "string" ? value.trim() : "";
}

function isInRange(value, minLength, maxLength) {
  return value.length >= minLength && value.length <= maxLength;
}

function formatUtcHour(date) {
  return `${date.getUTCFullYear()}${String(date.getUTCMonth() + 1).padStart(2, "0")}${String(date.getUTCDate()).padStart(2, "0")}${String(date.getUTCHours()).padStart(2, "0")}`;
}

function formatUtcDay(date) {
  return `${date.getUTCFullYear()}${String(date.getUTCMonth() + 1).padStart(2, "0")}${String(date.getUTCDate()).padStart(2, "0")}`;
}

function secondsUntilNextUtcHour(date) {
  const next = new Date(date);
  next.setUTCMinutes(0, 0, 0);
  next.setUTCHours(next.getUTCHours() + 1);
  return Math.max(1, Math.ceil((next.getTime() - date.getTime()) / 1000));
}

function secondsUntilNextUtcDay(date) {
  const next = new Date(date);
  next.setUTCHours(0, 0, 0, 0);
  next.setUTCDate(next.getUTCDate() + 1);
  return Math.max(1, Math.ceil((next.getTime() - date.getTime()) / 1000));
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
