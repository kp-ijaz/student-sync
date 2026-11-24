// Marketing (2026-11-24) in a real browser. As a marketing team member (account created by the admin through the API,
// password chosen at first sign-in): the portal shows only marketing pages; add a lead, log a call with a follow-up,
// see it under Follow-ups, move its status on its page, add and edit a campaign, and read the overview. As the admin:
// the Marketing section and the Team page. Fictional data only; the flow removes what it created.
// Screenshots at 375/768/1440; fails on overflow, console errors or failed requests.
import crypto from "node:crypto";
import { BASE_URL, launch, newPage, signIn, users, viewports, screenshot, demoPassword } from "./browser.mjs";

const API = process.env.API_BASE_URL ?? "http://localhost:5080";
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => { throw new Error(msg); };
const tag = Math.random().toString(36).slice(2, 7);

async function login(email, password) {
  const r = await fetch(`${API}/api/v1/auth/login`, { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ email, password, clientName: "e2e" }) });
  if (!r.ok) fail(`login ${email} → ${r.status}`);
  return (await r.json()).accessToken;
}
async function api(token, path, method = "GET", body) {
  const r = await fetch(`${API}/api/v1/${path}`, { method, headers: { "content-type": "application/json", authorization: `Bearer ${token}` }, body: body ? JSON.stringify(body) : undefined });
  if (!r.ok) fail(`${method} ${path} → ${r.status} ${await r.text()}`);
  return r.status === 204 ? null : r.json();
}

async function fill(page, selector, value) {
  await page.waitForSelector(selector, { timeout: 10000 });
  await page.$eval(selector, (el) => { el.value = ""; });
  await page.type(selector, String(value));
  await page.$eval(selector, (el) => el.dispatchEvent(new Event("change", { bubbles: true })));
  await wait(150);
}
async function clickText(page, scope, text) {
  const handle = await page.waitForFunction((scope, text) =>
    [...document.querySelectorAll(`${scope} button, ${scope} a.btn`)].find((b) => b.textContent.trim().startsWith(text) && !b.disabled), { timeout: 10000 }, scope, text);
  await handle.asElement().click();
  await wait(500);
}
const hasText = (page, scope, text) => page.waitForFunction((s, t) => document.querySelector(s)?.textContent.includes(t), { timeout: 10000 }, scope, text);

async function shots(page, name) {
  for (const [size, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await wait(500);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    if (overflow > 0) fail(`${name} overflows by ${overflow}px at ${size}`);
    await screenshot(page, `marketing-${name}-${size}`);
  }
  await page.setViewport(viewports.desktop);
}

const browser = await launch();
let adminToken, memberId;
try {
  // The admin creates the team member; the member chooses a password at first sign-in (here through the API).
  adminToken = await login(users.admin, demoPassword());
  const email = `marketer-${tag}@horizon.example.test`;
  const created = await api(adminToken, "marketing/team", "POST", { fullName: `Marketer ${tag} (Demo)`, email });
  memberId = created.id;
  const password = `Mk-${crypto.randomBytes(9).toString("base64url")}9a`;
  await api(await login(email, created.temporaryPassword), "me/password", "POST", { currentPassword: created.temporaryPassword, newPassword: password });

  // ---- As the marketing team member ----
  const ctx = await browser.createBrowserContext();
  const m = await newPage({ newPage: () => ctx.newPage() });
  const page = m.page;
  await page.goto(`${BASE_URL}/login`, { waitUntil: "networkidle0" });
  await page.type("input[type=email], #email", email);
  await page.type("input[type=password]", password);
  await Promise.all([page.waitForNavigation({ waitUntil: "networkidle0" }), page.click("button[type=submit]")]);
  if (!page.url().endsWith("/marketing")) fail(`Marketing user landed on ${page.url()}`);

  // Only marketing pages in the menu; a student page is refused.
  const links = await page.$$eval(".sidebar a[href]", (as) => [...new Set(as.map((a) => a.getAttribute("href")))]);
  if (links.some((h) => /students|teachers|fees|finance|sessions|settings/.test(h))) fail(`Marketing menu shows other sections: ${links.join(", ")}`);
  if (!["/marketing", "/marketing/leads", "/marketing/follow-ups", "/marketing/campaigns"].every((h) => links.includes(h))) fail(`Marketing menu incomplete: ${links.join(", ")}`);
  await page.goto(`${BASE_URL}/admin/students`, { waitUntil: "networkidle0" });
  if (await page.$(".desk-list tbody tr, .plist li")) fail("A marketing user can see the students list");
  m.failedRequests.length = 0; m.consoleErrors.length = 0; // the refusal above is expected

  // Campaign: add, then edit the amount spent.
  const campaign = `Campaign ${tag}`;
  await page.goto(`${BASE_URL}/marketing/campaigns`, { waitUntil: "networkidle0" });
  await clickText(page, ".page-header", "Add campaign");
  await fill(page, "#cp-n", campaign);
  await clickText(page, ".modal", "Flyer");
  await fill(page, "#cp-l", "Near the school gate");
  await fill(page, "#cp-b", 2000);
  await shots(page, "campaign-dialog");
  await clickText(page, ".modal .form-actions", "Add campaign");
  await hasText(page, "table", campaign);
  // Money paid for the campaign is added as spend (it also lands in Finance ▸ Expenses ▸ Marketing).
  await page.evaluate((c) => [...document.querySelectorAll("tbody tr")].find((r) => r.textContent.includes(c))?.setAttribute("data-e2e", "camp"), campaign);
  await clickText(page, '[data-e2e="camp"]', "Spend");
  await fill(page, "#sp-a", 600);
  await fill(page, "#sp-n", `Printing ${tag}`);
  await clickText(page, ".modal .form-actions", "Add spend");
  await hasText(page, ".modal .finance-ledger", `Printing ${tag}`);
  await shots(page, "spend-dialog");
  await page.click('.modal button[aria-label="Close dialog"]');
  await page.waitForFunction((c) => [...document.querySelectorAll("tbody tr")].some((r) => r.textContent.includes(c) && r.textContent.includes("600")), { timeout: 10000 }, campaign);
  await shots(page, "campaigns");

  // Lead: add from the dashboard button, with the campaign.
  const lead = `Lead ${tag}`;
  await page.goto(`${BASE_URL}/marketing`, { waitUntil: "networkidle0" });
  await clickText(page, ".page-header", "Add lead");
  await fill(page, "#ld-n", lead);
  await fill(page, "#ld-p", `+91 9${Math.floor(100000000 + Math.random() * 899999999)}`);
  await fill(page, "#ld-g", "Parent (fictional)");
  await clickText(page, ".modal", "Flyer");
  const campaignId = await page.$$eval("#ld-ca option", (os, c) => os.find((o) => o.textContent === c)?.value, campaign);
  if (!campaignId) fail("The new campaign is not offered on the lead form");
  await page.select("#ld-ca", campaignId);
  await fill(page, "#ld-i", "Maths, Class 10");
  await shots(page, "lead-dialog");
  await clickText(page, ".modal .form-actions", "Add lead");
  await hasText(page, ".lead-table", lead);

  // Log a call from the list: interested, follow up today.
  await fill(page, 'input[aria-label="Search leads"]', lead);
  await page.waitForFunction(() => document.querySelectorAll(".lead-table tbody tr").length === 1, { timeout: 10000 });
  await clickText(page, ".lead-table", "Log call");
  await clickText(page, ".modal", "Interested");
  await fill(page, "#ct-n", "Wants a trial class");
  await page.select("#ct-s", "Interested");
  const today = new Date(); const iso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-${String(today.getDate()).padStart(2, "0")}`;
  await page.$eval("#ct-f", (el, v) => { el.value = v; el.dispatchEvent(new Event("change", { bubbles: true })); }, iso);
  await shots(page, "call-dialog");
  await clickText(page, ".modal .form-actions", "Save");
  await page.waitForFunction(() => /Interested/.test(document.querySelector(".lead-table tbody")?.textContent ?? ""), { timeout: 10000 });
  await shots(page, "leads");

  // Import two more from a pasted spreadsheet.
  await page.goto(`${BASE_URL}/marketing/leads`, { waitUntil: "networkidle0" });
  await clickText(page, ".page-header", "Import");
  await fill(page, "#im-r", `Imp A ${tag}, 97${Math.floor(10000000 + Math.random() * 89999999)}, Parent A\nImp B ${tag}, 96${Math.floor(10000000 + Math.random() * 89999999)}`);
  await clickText(page, ".modal .form-actions", "Import");
  await hasText(page, ".modal", "2 added");
  await shots(page, "import-result");
  await clickText(page, ".modal .form-actions", "Done");

  // It is on Follow-ups (due today).
  await page.goto(`${BASE_URL}/marketing/follow-ups`, { waitUntil: "networkidle0" });
  await hasText(page, ".lead-table", lead);

  // Its page: the call is in the log; one tap moves the status.
  await page.$$eval(".lead-table a", (as, l) => as.find((a) => a.textContent.includes(l))?.click(), lead);
  await page.waitForFunction(() => /\/marketing\/leads\/[0-9a-f-]{36}$/.test(location.pathname), { timeout: 10000 });
  await hasText(page, ".lead-log", "Wants a trial class");
  await clickText(page, ".lead-detail", "Trial booked");
  await page.waitForFunction(() => document.querySelector('.lead-detail .pick[aria-pressed="true"]')?.textContent.trim() === "Trial booked", { timeout: 10000 });
  await shots(page, "lead");
  const leadId = page.url().split("/").pop();

  // The overview counts it.
  await page.goto(`${BASE_URL}/marketing`, { waitUntil: "networkidle0" });
  await hasText(page, "main", campaign);
  await hasText(page, "main", "Trial booked");
  await shots(page, "overview");
  if (m.consoleErrors.length) fail(`Console errors (marketing):\n${m.consoleErrors.join("\n")}`);
  if (m.failedRequests.length) fail(`Failed requests (marketing):\n${m.failedRequests.join("\n")}`);
  await ctx.close();

  // ---- As the admin ----
  const a = await newPage(browser);
  await signIn(a.page, users.admin);
  await a.page.goto(`${BASE_URL}/marketing/team`, { waitUntil: "networkidle0" });
  await hasText(a.page, "table", `Marketer ${tag}`);
  // Targets for the team member.
  await a.page.evaluate((n) => [...document.querySelectorAll("tbody tr")].find((r) => r.textContent.includes(n))?.setAttribute("data-e2e", "member"), `Marketer ${tag}`);
  await clickText(a.page, '[data-e2e="member"]', "Edit");
  await fill(a.page, "#mt-c", 20);
  await fill(a.page, "#mt-a", 5);
  await clickText(a.page, ".modal .form-actions", "Save");
  await hasText(a.page, "table", "20 calls/day");
  // The website enquiry form: switch it on, send an enquiry without signing in, see it as a Website lead.
  const form = await api(adminToken, "marketing/enquiry-form");
  if (!form.enabled) await clickText(a.page, "section[aria-labelledby=ef-h]", "Switch on");
  await hasText(a.page, "section[aria-labelledby=ef-h]", "On");
  await shots(a.page, "team");
  const pub = await browser.createBrowserContext();
  const v = await newPage({ newPage: () => pub.newPage() });
  await v.page.goto(`${BASE_URL}/enquire/${form.code}`, { waitUntil: "networkidle0" });
  await v.page.type("#eq-n", `Web ${tag}`);
  await v.page.type("#eq-p", `94${Math.floor(10000000 + Math.random() * 89999999)}`);
  await v.page.type("#eq-i", "Maths");
  for (const [size, viewport] of Object.entries(viewports)) { await v.page.setViewport(viewport); await wait(300); await screenshot(v.page, `marketing-enquiry-form-${size}`); }
  await Promise.all([v.page.waitForNavigation({ waitUntil: "networkidle0" }), v.page.click("button[type=submit]")]);
  if (!(await v.page.evaluate(() => document.body.textContent.includes("we have your enquiry")))) fail(`Enquiry not accepted: ${v.page.url()}`);
  if (v.consoleErrors.length || v.failedRequests.length) fail(`Public form errors:\n${[...v.consoleErrors, ...v.failedRequests].join("\n")}`);
  await pub.close();
  await a.page.goto(`${BASE_URL}/marketing/leads?followUp=website`, { waitUntil: "networkidle0" });
  await hasText(a.page, ".lead-table", `Web ${tag}`);
  if (!form.enabled) await api(adminToken, "marketing/enquiry-form", "PUT", { enabled: false });
  await a.page.goto(`${BASE_URL}/marketing/leads/${leadId}`, { waitUntil: "networkidle0" });
  await hasText(a.page, ".lead-log", `Marketer ${tag}`);
  if (!(await a.page.$('.sidebar a[href="/marketing/team"]'))) fail("Admin menu has no Marketing ▸ Team");
  if (a.consoleErrors.length) fail(`Console errors (admin):\n${a.consoleErrors.join("\n")}`);
  if (a.failedRequests.length) fail(`Failed requests (admin):\n${a.failedRequests.join("\n")}`);

  // Clean up what the flow created.
  await api(adminToken, `marketing/leads/${leadId}`, "DELETE");
  for (const l of (await api(adminToken, `marketing/leads?search=${tag}&pageSize=50`)).items) await api(adminToken, `marketing/leads/${l.id}`, "DELETE");
  for (const e of (await api(adminToken, `finance/entries?search=Printing ${tag}&pageSize=10`)).page.items) await api(adminToken, `finance/expenses/${e.recordId}`, "DELETE");
  const camp = (await api(adminToken, "marketing/campaigns")).find((c) => c.name === campaign);
  if (camp) await api(adminToken, `marketing/campaigns/${camp.id}`, "DELETE");
  console.log("marketing: OK — team portal limited to marketing, campaign add/edit, lead add, call + follow-up, status, overview, admin team page");
} catch (e) {
  console.error(e.message);
  process.exitCode = 1;
} finally {
  if (adminToken && memberId) await fetch(`${API}/api/v1/marketing/team/${memberId}`, { method: "DELETE", headers: { authorization: `Bearer ${adminToken}` } }).catch(() => {});
  await browser.close();
}
