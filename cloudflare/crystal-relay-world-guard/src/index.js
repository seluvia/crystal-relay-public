const KV_KEY = "world-guard-data";
const ADMIN_COOKIE = "wg_admin";
const ADMIN_SECRET_NAME = "WORLD_GUARD_ADMIN_SECRET";
const MAX_REQUEST_BYTES = 64 * 1024;
const MAX_REASON_LENGTH = 200;
const WORLD_ID_PATTERN = /^wrld_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const USER_ID_PATTERN = /^usr_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/api/check") {
      return handleCheck(request, env);
    }
    if (url.pathname === "/api/status") {
      return handleStatus(request, env);
    }
    if (url.pathname === "/admin") {
      return handleAdmin(request, env);
    }
    if (url.pathname === "/") {
      return Response.redirect(new URL("/admin", request.url), 302);
    }

    return jsonResponse({ message: "Not found." }, 404);
  }
};

// ── API: Check ──────────────────────────────────────────────
async function handleCheck(request, env) {
  if (request.method !== "POST") {
    return jsonResponse({ message: "Use POST to check." }, 405);
  }
  if (!env.WORLD_GUARD) {
    return jsonResponse({ blocked: true, reason: "Guard storage unavailable." }, 503);
  }

  const parsed = await readJson(request, MAX_REQUEST_BYTES);
  if (!parsed.ok) return jsonResponse({ message: parsed.message }, parsed.status);

  const { worldId, authorId, localDate } = parsed.value;
  if (!worldId || !WORLD_ID_PATTERN.test(worldId)) {
    return jsonResponse({ message: "Invalid worldId." }, 400);
  }
  if (authorId && !USER_ID_PATTERN.test(authorId)) {
    return jsonResponse({ message: "Invalid authorId." }, 400);
  }

  const data = await readGuardData(env);
  const now = new Date();
  const todayStr = localDate || `${String(now.getMonth()+1).padStart(2,'0')}/${String(now.getDate()).padStart(2,'0')}/${now.getFullYear()}`;

  // Check world entries
  for (const entry of data.entries) {
    if (entry.type === "world" && entry.id.toLowerCase() === worldId.toLowerCase()) {
      if (isEntryActive(entry, todayStr)) {
        return jsonResponse({ blocked: true, reason: entry.reason || "" });
      }
    }
  }

  // Check creator entries
  if (authorId) {
    for (const entry of data.entries) {
      if (entry.type === "creator" && entry.id.toLowerCase() === authorId.toLowerCase()) {
        if (isEntryActive(entry, todayStr)) {
          return jsonResponse({ blocked: true, reason: entry.reason || "" });
        }
      }
    }
  }

  return jsonResponse({ blocked: false });
}

function isEntryActive(entry, todayStr) {
  const today = parseDate(todayStr);
  if (!today) return false;
  // If no start date, entry is permanent from the beginning
  const start = parseDate(entry.startDate);
  if (start && today < start) return false;
  if (entry.endDate) {
    const end = parseDate(entry.endDate);
    if (end && today > end) return false;
  }
  return true;
}

// ── API: Status ─────────────────────────────────────────────
async function handleStatus(request, env) {
  if (!env.WORLD_GUARD) {
    return jsonResponse({ ok: false, message: "Guard storage unavailable." }, 503);
  }

  const data = await readGuardData(env);
  const now = new Date();
  const todayStr = `${String(now.getMonth()+1).padStart(2,'0')}/${String(now.getDate()).padStart(2,'0')}/${now.getFullYear()}`;

  const activeWorlds = data.entries.filter(e => e.type === "world" && isEntryActive(e, todayStr));
  const activeCreators = data.entries.filter(e => e.type === "creator" && isEntryActive(e, todayStr));

  return jsonResponse({
    ok: true,
    worldEntryCount: activeWorlds.length,
    creatorEntryCount: activeCreators.length,
    totalEntries: data.entries.length
  });
}

// ── Admin: HTML UI ──────────────────────────────────────────
async function handleAdmin(request, env) {
  if (!env.WORLD_GUARD) {
    return htmlResponse(renderPage({ message: "Storage unavailable.", isError: true }), 503);
  }
  if (!hasValidSecret(env)) {
    return htmlResponse(renderPage({ message: "Admin secret not configured.", isError: true }), 503);
  }

  if (request.method === "GET") {
    const authed = await authenticate(request, env);
    if (!authed) return htmlResponse(renderPage({ needsLogin: true }), 200);
    return renderAdminPage(env, null);
  }

  // POST
  const form = await readForm(request);
  if (!form.ok) return htmlResponse(renderPage({ message: form.message, isError: true }), form.status);

  const authed = await authenticate(request, env, form.value);
  if (!authed) return htmlResponse(renderPage({ needsLogin: true, message: "Invalid secret.", isError: true }), 403);

  const action = form.value.get("action");

  if (action === "logout") {
    return htmlResponse(renderPage({ needsLogin: true, message: "Signed out." }), 200, clearCookie());
  }

  if (action === "save") {
    return renderAdminPage(env, "Saved.", false);
  }

  if (action === "add") {
    const result = await addEntry(env, form.value);
    if (!result.ok) return renderAdminPage(env, result.message, true);
    return renderAdminPage(env, "Entry added.", false);
  }

  if (action === "delete") {
    const idx = parseInt(form.value.get("index") || "-1", 10);
    if (idx < 0) return renderAdminPage(env, "Invalid entry index.", true);
    await deleteEntry(env, idx);
    return renderAdminPage(env, "Entry deleted.", false);
  }

  return renderAdminPage(env, null);
}

async function renderAdminPage(env, message, isError) {
  const data = await readGuardData(env);
  return htmlResponse(renderPage({ data, message, isError }), 200, setCookie(await createCookie(env)));
}

// ── Data helpers ────────────────────────────────────────────
async function readGuardData(env) {
  const raw = await env.WORLD_GUARD.get(KV_KEY);
  if (!raw) return { entries: [] };
  try {
    const parsed = JSON.parse(raw);
    if (parsed && Array.isArray(parsed.entries)) return parsed;
  } catch {}
  return { entries: [] };
}

async function writeGuardData(env, data) {
  await env.WORLD_GUARD.put(KV_KEY, JSON.stringify(data));
}

async function addEntry(env, form) {
  const type = form.get("entryType");
  const id = (form.get("entryId") || "").trim();
  const name = (form.get("entryName") || "").trim();
  const startDate = form.get("startDate");
  const endDate = form.get("noEndDate") === "on" ? null : (form.get("endDate") || "");
  const reason = (form.get("reason") || "").trim();

  if (type !== "world" && type !== "creator") {
    return { ok: false, message: "Invalid entry type." };
  }
  if (type === "world" && !WORLD_ID_PATTERN.test(id)) {
    return { ok: false, message: "World ID must match wrld_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" };
  }
  if (type === "creator" && !USER_ID_PATTERN.test(id)) {
    return { ok: false, message: "User ID must match usr_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" };
  }
  const isPermanent = form.get("noEndDate") === "on";
  if (!isPermanent && (!startDate || !parseDate(startDate))) {
    return { ok: false, message: "Start date is required." };
  }
  if (startDate && !parseDate(startDate)) {
    return { ok: false, message: "Start date is not valid." };
  }
  if (endDate && !parseDate(endDate)) {
    return { ok: false, message: "End date is not valid." };
  }
  if (!reason) {
    return { ok: false, message: "Reason is required." };
  }
  if (reason.length > MAX_REASON_LENGTH) {
    return { ok: false, message: `Reason too long (max ${MAX_REASON_LENGTH} chars).` };
  }

  const data = await readGuardData(env);
  data.entries.push({
    id: id.toLowerCase(),
    type,
    name: name || null,
    startDate: isPermanent ? null : (startDate || null),
    endDate: isPermanent ? null : (endDate || null),
    reason
  });
  await writeGuardData(env, data);
  return { ok: true };
}

async function deleteEntry(env, index) {
  const data = await readGuardData(env);
  if (index >= data.entries.length) return;
  data.entries.splice(index, 1);
  await writeGuardData(env, data);
}

// ── Auth ────────────────────────────────────────────────────
function hasValidSecret(env) {
  const s = env[ADMIN_SECRET_NAME];
  return typeof s === "string" && s.length >= 24;
}

async function authenticate(request, env, form) {
  const secret = env[ADMIN_SECRET_NAME];
  const provided = form ? (form.get("adminSecret") || "").trim() : "";
  if (provided && await constantTime(provided, secret)) return true;
  const cookie = getCookie(request, ADMIN_COOKIE);
  if (cookie && await constantTime(cookie, await sha256Hex(secret))) return true;
  return false;
}

async function createCookie(env) {
  const val = await sha256Hex(env[ADMIN_SECRET_NAME]);
  return `${ADMIN_COOKIE}=${val}; HttpOnly; Secure; SameSite=Strict; Path=/admin; Max-Age=86400`;
}

function clearCookie() {
  return { "Set-Cookie": `${ADMIN_COOKIE}=; HttpOnly; Secure; SameSite=Strict; Path=/admin; Max-Age=0` };
}

// ── HTML renderer ───────────────────────────────────────────
function renderPage(opts = {}) {
  const { data, message, isError, needsLogin } = opts;
  const entries = data ? data.entries : [];
  const msgHtml = message ? `<div class="${isError ? 'error' : 'msg'}">${esc(message)}</div>` : "";

  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Crystal Relay World Guard</title>
<style>
:root{--bg:#141018;--fg:#f6f1ff;--muted:#a89bb8;--card:#211929;--border:#4d405a;--accent:#7be0c3;--danger:#ff6b8a;--input-bg:#1a1322}
*{box-sizing:border-box}
body{margin:0;font-family:Inter,Segoe UI,system-ui,sans-serif;background:var(--bg);color:var(--fg);min-height:100vh}
main{max-width:960px;margin:0 auto;padding:32px 18px}
h1{margin:0 0 6px;font-size:26px}
.subtitle{color:var(--muted);margin:0 0 18px;font-size:14px}
.card{background:var(--card);border:1px solid var(--border);border-radius:10px;padding:18px;margin-bottom:16px}
label{display:block;font-weight:600;margin-bottom:4px;font-size:14px}
input,select,textarea,button{font:inherit;border-radius:8px;border:1px solid var(--border);background:var(--input-bg);color:var(--fg);padding:8px 12px}
input[type="date"]{color-scheme:dark}
textarea{resize:vertical;min-height:60px}
button{cursor:pointer;font-weight:700}
.btn-primary{background:var(--accent);color:#111;border:0}
.btn-danger{background:var(--danger);color:#fff;border:0}
.btn-secondary{background:var(--border);color:var(--fg);border:0}
.btn-sm{padding:5px 10px;font-size:13px}
.row{display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap}
.row>*{flex:1;min-width:0}
.msg{background:#18352f;color:#bffbe9;padding:10px 12px;border-radius:8px;margin-bottom:12px}
.error{background:#3d1e2a;color:#ffc4d0;padding:10px 12px;border-radius:8px;margin-bottom:12px}
table{width:100%;border-collapse:collapse;font-size:14px}
th{text-align:left;padding:8px 10px;border-bottom:2px solid var(--border);color:var(--muted);font-size:12px;text-transform:uppercase}
td{padding:8px 10px;border-bottom:1px solid var(--border);vertical-align:middle}
tr:hover td{background:rgba(123,224,195,0.05)}
.badge{display:inline-block;padding:2px 8px;border-radius:4px;font-size:12px;font-weight:700}
.badge-world{background:#2d1f4e;color:#c9a8ff}
.badge-creator{background:#1f3d2d;color:#a8ffc9}
.search-box{width:100%;margin-bottom:12px}
.permanent{color:var(--accent);font-style:italic}
.empty{text-align:center;color:var(--muted);padding:24px}
.form-grid{display:grid;grid-template-columns:1fr 1fr;gap:12px}
.form-grid .full{grid-column:1/-1}
@media(max-width:600px){.form-grid{grid-template-columns:1fr}}
</style></head><body><main>
<h1>Crystal Relay World Guard</h1>
<p class="subtitle">Manage protected VRChat worlds and creators.</p>
${msgHtml}
${needsLogin ? loginForm() : (data ? editorUI(entries) : "")}
</main></body></html>`;
}

function loginForm() {
  return `<div class="card"><form method="post" action="/admin">
<input type="hidden" name="action" value="login">
<label>Admin Secret<input type="password" name="adminSecret" autocomplete="current-password" required></label>
<div style="margin-top:12px"><button type="submit" class="btn-primary">Open Guard</button></div>
</form></div>`;
}

function editorUI(entries) {
  return `
<div class="card">
<h2 style="margin:0 0 12px;font-size:18px">Add Entry</h2>
<form method="post" action="/admin">
<input type="hidden" name="action" value="add">
<div class="form-grid">
<div><label>Type<select name="entryType" id="entryType">
<option value="world">World ID</option>
<option value="creator">Creator/User ID</option>
</select></label></div>
<div><label>ID<input type="text" name="entryId" id="entryId" placeholder="wrld_... or usr_..." required></label></div>
<div class="full"><label>Name (optional)<input type="text" name="entryName" id="entryName" placeholder="World or creator name for reference" maxlength="120"></label></div>
<div><label>Start Date<input type="date" name="startDate" id="startDate"></label></div>
<div><label>End Date<input type="date" name="endDate" id="endDate"></label></div>
<div class="full" style="display:flex;align-items:center;gap:8px;padding-top:4px">
<label style="display:flex;align-items:center;gap:6px;margin:0;cursor:pointer;font-weight:400">
<input type="checkbox" name="noEndDate" id="noEndDate" onchange="togglePermanent()">
No end date (permanent)
</label></div>
<div class="full"><label>Reason<input type="text" name="reason" placeholder="Why this is guarded..." maxlength="200" required></label></div>
</div>
<div style="margin-top:14px"><button type="submit" class="btn-primary">Add Entry</button></div>
</form>
</div>

<div class="card">
<h2 style="margin:0 0 12px;font-size:18px">Guarded Entries (${entries.length})</h2>
<input type="text" class="search-box" id="searchInput" placeholder="Search by ID, type, or reason..." oninput="filterTable()">
<table id="entriesTable">
<thead><tr><th>Type</th><th>Name</th><th>ID</th><th>Start</th><th>End</th><th>Reason</th><th></th></tr></thead>
<tbody>
${entries.length === 0 ? '<tr><td colspan="7" class="empty">No entries yet.</td></tr>' :
  entries.map((e, i) => `<tr data-search="${e.type} ${e.name || ""} ${e.id} ${e.reason}">
<td><span class="badge badge-${e.type}">${e.type === "world" ? "World" : "Creator"}</span></td>
<td style="font-weight:600">${e.name ? esc(e.name) : '<span style="color:var(--muted)">—</span>'}</td>
<td style="font-family:Consolas,monospace;font-size:13px">${esc(e.id)}</td>
<td>${e.startDate ? esc(e.startDate) : '<span class="permanent">Permanent</span>'}</td>
<td>${e.endDate ? esc(e.endDate) : '<span class="permanent">Permanent</span>'}</td>
<td style="max-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${esc(e.reason)}">${esc(e.reason)}</td>
<td><form method="post" action="/admin" style="display:inline"><input type="hidden" name="action" value="delete"><input type="hidden" name="index" value="${i}"><button type="submit" class="btn-danger btn-sm">Delete</button></form></td>
</tr>`).join("")}
</tbody>
</table>
</div>

<div style="margin-top:8px">
<form method="post" action="/admin" style="display:inline"><input type="hidden" name="action" value="logout"><button type="submit" class="btn-secondary btn-sm">Sign Out</button></form>
</div>

<script>
function filterTable(){
  const q=document.getElementById("searchInput").value.toLowerCase();
  document.querySelectorAll("#entriesTable tbody tr").forEach(r=>{
    r.style.display=r.dataset.search.toLowerCase().includes(q)?"":"none";
  });
}
function togglePermanent(){
  const checked=document.getElementById("noEndDate").checked;
  document.getElementById("startDate").disabled=checked;
  document.getElementById("startDate").required=!checked;
  document.getElementById("endDate").disabled=checked;
}
</script>`;
}

// ── Utilities ───────────────────────────────────────────────
function parseDate(s) {
  if (!s) return null;
  // Handle YYYY-MM-DD (from HTML date picker)
  let m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(s);
  if (m) {
    const [, year, month, day] = m;
    const d = new Date(Number(year), Number(month) - 1, Number(day));
    if (d.getFullYear() !== Number(year) || d.getMonth() !== Number(month) - 1 || d.getDate() !== Number(day)) return null;
    return d;
  }
  // Handle MM/DD/YYYY
  m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(s);
  if (!m) return null;
  const [, month, day, year] = m;
  const d = new Date(Number(year), Number(month) - 1, Number(day));
  if (d.getFullYear() !== Number(year) || d.getMonth() !== Number(month) - 1 || d.getDate() !== Number(day)) return null;
  return d;
}

function normalize(s) { return typeof s === "string" ? s.trim() : ""; }

async function readJson(req, maxBytes) {
  const text = await readText(req, maxBytes);
  if (!text.ok) return text;
  try { return { ok: true, value: JSON.parse(text.value) }; }
  catch { return { ok: false, status: 400, message: "Invalid JSON." }; }
}

async function readForm(req) {
  const text = await readText(req, MAX_REQUEST_BYTES);
  if (!text.ok) return text;
  return { ok: true, value: new URLSearchParams(text.value) };
}

async function readText(req, maxBytes) {
  const cl = Number(req.headers.get("content-length") || "0");
  if (cl > maxBytes) return { ok: false, status: 413, message: "Too large." };
  const text = await req.text();
  if (new TextEncoder().encode(text).byteLength > maxBytes) return { ok: false, status: 413, message: "Too large." };
  return { ok: true, value: text };
}

function jsonResponse(body, status = 200, headers = {}) {
  return new Response(JSON.stringify(body), {
    status, headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", ...headers }
  });
}

function htmlResponse(body, status = 200, headers = {}) {
  return new Response(body, {
    status, headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", ...headers }
  });
}

function setCookie(val) { return { "Set-Cookie": val }; }

function getCookie(req, name) {
  const c = (req.headers.get("cookie") || "").split(";").map(p => p.trim()).find(p => p.startsWith(name + "="));
  return c ? c.slice(name.length + 1) : "";
}

function esc(s) {
  return String(s).replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;").replace(/'/g,"&#39;");
}

async function constantTime(a, b) {
  const ha = await sha256Bytes(normalize(a));
  const hb = await sha256Bytes(normalize(b));
  let d = ha.length ^ hb.length;
  for (let i = 0; i < Math.max(ha.length, hb.length); i++) d |= (ha[i]||0) ^ (hb[i]||0);
  return d === 0;
}

async function sha256Hex(v) {
  const b = await sha256Bytes(v);
  return [...b].map(x => x.toString(16).padStart(2,"0")).join("");
}

async function sha256Bytes(v) {
  return new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(v)));
}
