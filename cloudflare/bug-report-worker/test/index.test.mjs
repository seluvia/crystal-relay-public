import assert from "node:assert/strict";
import test from "node:test";
import { buildGitHubIssue, readPayloadFromRequest } from "../src/index.js";

test("buildGitHubIssue sanitizes title contact and legacy diagnostics", () => {
  const issue = buildGitHubIssue({
    title: "access_token=title-secret leak",
    whatHappened: "The bug report form accepted a secret in the title at D:\\StreamTools\\CrystalRelay\\secret.json.",
    expectedBehavior: "The worker should redact the secret before creating the issue.",
    stepsToReproduce: "Open Crystal Relay, submit the report, inspect the GitHub issue.",
    contactName: "C:\\Users\\secretuser\\contact.txt",
    appVersion: "3.1.9",
    category: "other",
    severity: "normal",
    diagnostics: "legacy diagnostics C:\\Users\\secretuser\\crash.txt"
  });

  assert.doesNotMatch(issue.title, /title-secret|access_token=/i);
  assert.doesNotMatch(issue.body, /secretuser|access_token=|D:\\StreamTools|secret\.json/i);
  assert.match(issue.body, /<user>/);
  assert.match(issue.body, /<local path>/);
  assert.match(issue.body, /legacy diagnostics/);
});

test("readPayloadFromRequest rejects oversized bodies without content length", async () => {
  const oversizedBody = JSON.stringify({ value: "x".repeat(57 * 1024) });
  const request = new Request("https://example.test/report", {
    method: "POST",
    body: oversizedBody
  });

  const result = await readPayloadFromRequest(request);

  assert.equal(result.ok, false);
  assert.equal(result.status, 413);
});
