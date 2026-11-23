// Admin edit (2026-11-24) in a real browser: edit a school result and a goal on a student's profile, edit a class
// material, and see an archived student who still owes money on Fees with an "Archived" badge. Test records are created
// through the API with fictional data. Screenshots at 375/768/1440; fails on overflow, console errors or failed requests.
import { BASE_URL, launch, newPage, signIn, users, viewports, screenshot, demoPassword } from "./browser.mjs";

const API = process.env.API_BASE_URL ?? "http://localhost:5080";
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => { throw new Error(msg); };
const tag = Math.random().toString(36).slice(2, 7);

const tokens = {};
async function api(email, path, method = "GET", body) {
  if (!tokens[email]) {
    const r = await fetch(`${API}/api/v1/auth/login`, { method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ email, password: demoPassword(), clientName: "e2e" }) });
    tokens[email] = (await r.json()).accessToken;
  }
  const r = await fetch(`${API}/api/v1/${path}`, { method, headers: { "content-type": "application/json", authorization: `Bearer ${tokens[email]}` },
    body: body ? JSON.stringify(body) : undefined });
  if (!r.ok) fail(`${method} ${path} → ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
}

async function fill(page, selector, value) {
  await page.$eval(selector, (el) => { el.value = ""; });
  await page.type(selector, String(value));
  await page.$eval(selector, (el) => el.dispatchEvent(new Event("change", { bubbles: true })));
  await wait(150);
}

async function clickText(page, scope, text) {
  const handle = await page.waitForFunction((scope, text) =>
    [...document.querySelectorAll(`${scope} button`)].find((b) => b.textContent.trim().startsWith(text) && !b.disabled), { timeout: 10000 }, scope, text);
  await handle.asElement().click();
  await wait(400);
}

const hasText = (page, scope, text) => page.waitForFunction((s, t) => document.querySelector(s)?.textContent.includes(t), { timeout: 10000 }, scope, text);

async function shots(page, name) {
  for (const [size, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await wait(500);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    if (overflow > 0) fail(`${name} overflows by ${overflow}px at ${size}`);
    await screenshot(page, `crud3-${name}-${size}`);
  }
  await page.setViewport(viewports.desktop);
}

const browser = await launch();
const { page, consoleErrors, failedRequests } = await newPage(browser);
try {
  const student = (await api(users.admin, "students", "POST", { fullName: `Edit ${tag} (Demo Student)`, email: `edit-${tag}@horizon.example.test`,
    studentCode: `ED-${tag}`, timeZoneId: "Asia/Dubai" })).id;
  await api(users.admin, `students/${student}/school-reports`, "POST", { title: `Term ${tag}`, overallPercent: 70, marks: [{ subject: "Maths", marks: 60, maxMarks: 100 }] });
  await api(users.admin, `students/${student}/notes`, "POST", { kind: "Goal", text: `Goal ${tag}` });

  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/students/${student}`, { waitUntil: "networkidle0" });

  // 1. School result: edit the overall % and the maths marks.
  await page.waitForSelector("#sr-h");
  await clickText(page, "section[aria-labelledby=sr-h]", "Edit");
  await fill(page, "#srp", 75);
  await fill(page, "#srm0", 68);
  await shots(page, "report-dialog");
  await clickText(page, ".modal .form-actions", "Save result");
  await hasText(page, "section[aria-labelledby=sr-h]", "Overall 75%");
  await hasText(page, "section[aria-labelledby=sr-h]", "68/100");

  // 2. Goal: edit through the form under the list.
  await clickText(page, "section[aria-labelledby=notes-h] ul", "Edit");
  await fill(page, "#sn-text", `Goal ${tag} edited`);
  await clickText(page, "section[aria-labelledby=notes-h]", "Save changes");
  await hasText(page, "section[aria-labelledby=notes-h] ul", `Goal ${tag} edited`);
  await shots(page, "profile");

  // 3. Archived student who still owes money: on Fees with the Archived badge.
  await api(users.admin, `students/${student}/packages`, "POST", { hours: 1, price: 3000, paidNow: 1000 });
  await api(users.admin, `students/${student}`, "DELETE");
  await page.goto(`${BASE_URL}/admin/fees`, { waitUntil: "networkidle0" });
  const row = await page.waitForFunction((t) => [...document.querySelectorAll(".fee-list li")].find((li) => li.textContent.includes(t)), { timeout: 10000 }, `Edit ${tag}`);
  if (!/Archived/.test(await row.evaluate((li) => li.textContent))) fail("Archived badge missing on Fees due");
  await shots(page, "fees-archived");

  // 4. Class material: rename it in the class hub.
  const sessions = await api(users.privateTeacher, "sessions?limit=50");
  const session = sessions.find((s) => s.sessionType === "OneToOne" && s.title.includes("Fatima")) ?? fail("No Fatima class for Priya");
  const material = (await api(users.admin, "materials/link", "POST", { title: `Material ${tag}`, url: "https://example.test/notes", sessionId: session.id })).id;
  await page.goto(`${BASE_URL}/sessions/${session.id}?tab=materials`, { waitUntil: "networkidle0" });
  const tab = await page.$$eval("[role=tab], .tabs button", (bs) => bs.findIndex((b) => /Materials/.test(b.textContent)));
  if (tab >= 0) { const tabs = await page.$$("[role=tab], .tabs button"); await tabs[tab].click(); await wait(500); }
  await page.waitForFunction((t) => document.querySelector("main")?.textContent.includes(t), { timeout: 10000 }, `Material ${tag}`);
  await page.evaluate((t) => [...document.querySelectorAll("main li")].find((li) => li.textContent.includes(t))?.setAttribute("data-e2e", "mat"), `Material ${tag}`);
  await clickText(page, '[data-e2e="mat"]', "Edit");
  await fill(page, "#emt", `Material ${tag} renamed`);
  await clickText(page, ".modal .form-actions", "Save");
  await hasText(page, "main", `Material ${tag} renamed`);
  await shots(page, "materials");
  await api(users.admin, `materials/${material}`, "DELETE");

  if (consoleErrors.length) fail(`Console errors:\n${consoleErrors.join("\n")}`);
  if (failedRequests.length) fail(`Failed requests:\n${failedRequests.join("\n")}`);
  console.log("admin-crud-final: OK — school result edit, goal edit, archived debt on Fees, material edit");
} catch (e) {
  await screenshot(page, "crud3-failure").catch(() => {});
  console.error(e.message);
  process.exitCode = 1;
} finally {
  await browser.close();
}
