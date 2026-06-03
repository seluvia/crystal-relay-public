import assert from "node:assert/strict";
import { describe, it } from "node:test";
import worker, {
  evaluateBlacklist,
  parseBlacklistContent,
  validateBlacklistContent
} from "../src/index.js";

const ADMIN_SECRET = "abcdefghijklmnopqrstuvwxyz123456";
const WORLD_ID = "wrld_00000000-0000-0000-0000-000000000000";
const OTHER_WORLD_ID = "wrld_11111111-1111-1111-1111-111111111111";
const AUTHOR_ID = "usr_00000000-0000-0000-0000-000000000000";
const REASON = "Private event reason";

describe("protected world list parser", () => {
  it("parses world and creator entries", () => {
    const result = parseBlacklistContent([
      "# comment",
      `${AUTHOR_ID} - [06/01/2026 - 06/07/2026] Special event`,
      `${WORLD_ID} - ${REASON}`
    ].join("\n"));

    assert.equal(result.ok, true);
    assert.equal(result.value.worldIds.size, 1);
    assert.equal(result.value.creatorEntries.length, 1);
  });

  it("accepts unbracketed date ranges", () => {
    const result = parseBlacklistContent(`${AUTHOR_ID} - 06/01/2026 - 06/07/2026 Special event`);

    assert.equal(result.ok, true);
    assert.equal(result.value.creatorEntries.length, 1);
  });

  it("rejects invalid lines and expired ranges do not match", () => {
    const invalid = validateBlacklistContent("not-valid");
    assert.equal(invalid.ok, false);

    const parsed = parseBlacklistContent(`${AUTHOR_ID} - [05/01/2026 - 05/02/2026] Expired`);
    assert.equal(parsed.ok, true);
    assert.deepEqual(evaluateBlacklist(parsed.value, OTHER_WORLD_ID, AUTHOR_ID, 20260601), {
      blocked: false,
      reason: ""
    });
  });

  it("matches inclusive world and creator rules", () => {
    const parsed = parseBlacklistContent([
      `${AUTHOR_ID} - [06/01/2026 - 06/07/2026] Special event`,
      `${WORLD_ID} - ${REASON}`
    ].join("\n"));

    assert.equal(parsed.ok, true);
    assert.deepEqual(evaluateBlacklist(parsed.value, WORLD_ID, "", 20260601), {
      blocked: true,
      reason: REASON
    });
    assert.deepEqual(evaluateBlacklist(parsed.value, OTHER_WORLD_ID, AUTHOR_ID, 20260601), {
      blocked: true,
      reason: "Special event"
    });
    assert.deepEqual(evaluateBlacklist(parsed.value, OTHER_WORLD_ID, AUTHOR_ID, 20260607), {
      blocked: true,
      reason: "Special event"
    });
    assert.deepEqual(evaluateBlacklist(parsed.value, OTHER_WORLD_ID, AUTHOR_ID, 20260608), {
      blocked: false,
      reason: ""
    });
  });
});

describe("worker endpoints", () => {
  it("rejects a wrong admin secret", async () => {
    const env = createEnv();
    const response = await postAdmin(env, new URLSearchParams({
      action: "load",
      adminSecret: "wrong"
    }));

    assert.equal(response.status, 403);
  });

  it("saves valid content and returns only matched reasons", async () => {
    const env = createEnv();
    const content = [
      `${AUTHOR_ID} - [06/01/2026 - 06/07/2026] Special event`,
      `${WORLD_ID} - ${REASON}`
    ].join("\n");

    const saveResponse = await postAdmin(env, new URLSearchParams({
      action: "save",
      adminSecret: ADMIN_SECRET,
      content
    }));
    assert.equal(saveResponse.status, 200);

    const blockResponse = await postCheck(env, {
      worldId: WORLD_ID,
      authorId: "",
      localDate: "2026-06-01"
    });
    assert.equal(blockResponse.status, 200);
    const blockBody = await blockResponse.json();
    assert.deepEqual(blockBody, { blocked: true, reason: REASON });
    assert.equal(JSON.stringify(blockBody).includes(WORLD_ID), false);
    assert.equal(JSON.stringify(blockBody).includes(AUTHOR_ID), false);

    const allowResponse = await postCheck(env, {
      worldId: OTHER_WORLD_ID,
      authorId: AUTHOR_ID,
      localDate: "2026-06-08"
    });
    assert.equal(allowResponse.status, 200);
    assert.deepEqual(await allowResponse.json(), { blocked: false });
  });

  it("does not replace active content when an admin save is invalid", async () => {
    const env = createEnv();
    const validContent = `${WORLD_ID} - ${REASON}`;

    const saveResponse = await postAdmin(env, new URLSearchParams({
      action: "save",
      adminSecret: ADMIN_SECRET,
      content: validContent
    }));
    assert.equal(saveResponse.status, 200);

    const invalidResponse = await postAdmin(env, new URLSearchParams({
      action: "save",
      adminSecret: ADMIN_SECRET,
      content: "not-valid"
    }));
    assert.equal(invalidResponse.status, 400);

    const blockResponse = await postCheck(env, {
      worldId: WORLD_ID,
      authorId: "",
      localDate: "2026-06-01"
    });
    assert.equal(blockResponse.status, 200);
    assert.deepEqual(await blockResponse.json(), { blocked: true, reason: REASON });
  });

  it("fails status when direct KV content is invalid", async () => {
    const env = createEnv();
    await env.WORLD_GUARD.put("protected-world-list", "not-valid");

    const response = await worker.fetch(new Request("https://example.test/api/status"), env);

    assert.equal(response.status, 503);
  });
});

function createEnv() {
  return {
    WORLD_GUARD: new MemoryKv(),
    WORLD_GUARD_ADMIN_SECRET: ADMIN_SECRET
  };
}

async function postAdmin(env, body) {
  return worker.fetch(new Request("https://example.test/admin", {
    method: "POST",
    headers: {
      "Content-Type": "application/x-www-form-urlencoded"
    },
    body
  }), env);
}

async function postCheck(env, body) {
  return worker.fetch(new Request("https://example.test/api/check", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(body)
  }), env);
}

class MemoryKv {
  constructor() {
    this.values = new Map();
  }

  async get(key) {
    return this.values.get(key) ?? null;
  }

  async put(key, value) {
    this.values.set(key, String(value));
  }
}
