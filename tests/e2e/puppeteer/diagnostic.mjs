// Browser check for slice 3 (diagnostic assessment):
//   1. admin opens Hiro's profile → the seeded diagnostic is listed → Breakdown shows a flagged chapter → records it as a weak area;
//   2. admin creates a new diagnostic for Aisha from the profile (prefilled), picks questions, publishes, enters paper answers,
//      and lands on the result with a breakdown;
//   3. teacher opens the question bank with chapter + image fields; student sees the released diagnostic breakdown.
// Desktop and mobile screenshots; console errors and failed requests reported. Usage: BASE_URL=http://localhost:5090 node puppeteer/diagnostic.mjs
import { BASE_URL, launch, newPage, screenshot, signIn, users, viewports } from "./browser.mjs";

const results = [];
const browser = await launch();
const settle = (page) => page.waitForNetworkIdle({ idleTime: 500, timeout: 15_000 }).catch(() => {});
async function step(name, vp, fn) {
  const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
  try {
    await fn(page);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    results.push({ name, ok: true, consoleErrors, failedRequests, overflow });
  } catch (e) {
    await screenshot(page, `${name}-FAILED`).catch(() => {});
    results.push({ name, ok: false, error: e.message, consoleErrors, failedRequests });
  } finally { await page.close(); }
}
async function clickText(page, selector, text) {
  const handle = await page.evaluateHandle((sel, t) => [...document.querySelectorAll(sel)].find((el) => el.textContent.trim().includes(t)) ?? null, selector, text);
  const el = handle.asElement();
  if (!el) throw new Error(`No ${selector} containing "${text}"`);
  await el.click();
}
async function select(page, id, optionText) {
  const ok = await page.evaluate((i, ot) => {
    const s = document.getElementById(i); if (!s) return false;
    const o = [...s.options].find((x) => x.textContent.trim().startsWith(ot)); if (!o) return false;
    s.value = o.value; s.dispatchEvent(new Event("change", { bubbles: true })); return true;
  }, id, optionText);
  if (!ok) throw new Error(`Could not select "${optionText}" in #${id}`);
  await settle(page);
}
async function openHiroProfile(page) {
  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/admin/students?search=hiro`, { waitUntil: "networkidle0" });
  await settle(page);
  await clickText(page, "a", "Hiro");
  await page.waitForSelector("#diag-h");
  await settle(page);
}
const stamp = Date.now().toString().slice(-5);

try {
  for (const [vpName, vp] of [["desktop", viewports.desktop], ["mobile", viewports.mobile]]) {
    await step(`seeded-diagnostic-breakdown-${vpName}`, vp, async (page) => {
      await openHiroProfile(page);
      await screenshot(page, `student-profile-diagnostics-${vpName}`);
      await clickText(page, "section[aria-labelledby=diag-h] a", "Breakdown");
      await page.waitForSelector("#bd-h", { timeout: 15_000 });
      await settle(page);
      const flagged = await page.evaluate(() => [...document.querySelectorAll(".badge")].some((b) => /needs attention|already a weak subject/.test(b.textContent)));
      if (!flagged) throw new Error("No chapter flagged in the breakdown");
      await screenshot(page, `diagnostic-breakdown-${vpName}`);
      // Record the flagged rows (if any are not yet recorded) and check the toast / state.
      const canRecord = await page.evaluate(() => !![...document.querySelectorAll("button")].find((b) => b.textContent.includes("Record") && !b.disabled));
      if (canRecord) {
        await clickText(page, "button", "Record ");
        await settle(page);
        await page.waitForFunction(() => [...document.querySelectorAll(".badge")].some((b) => b.textContent.includes("already a weak subject")), { timeout: 15_000 });
      }
      await screenshot(page, `diagnostic-breakdown-recorded-${vpName}`);
    });
  }

  await step("admin-creates-diagnostic-and-enters-paper", viewports.desktop, async (page) => {
    await signIn(page, users.admin);
    await page.goto(`${BASE_URL}/admin/students?search=aisha`, { waitUntil: "networkidle0" });
    await settle(page);
    await clickText(page, "a", "Aisha");
    await page.waitForSelector("#diag-h");
    await clickText(page, "section[aria-labelledby=diag-h] a", "New diagnostic");
    await page.waitForSelector("#ep");
    await settle(page);
    const purpose = await page.$eval("#ep", (s) => s.value);
    if (purpose !== "Diagnostic") throw new Error(`Purpose not prefilled: ${purpose}`);
    await page.evaluate((v) => { const i = document.getElementById("et"); i.value = v; i.dispatchEvent(new Event("change", { bubbles: true })); }, `Puppeteer diagnostic ${stamp} (demo)`);
    await settle(page);
    await select(page, "es", "Mathematics (Class 10");
    await select(page, "etc", "Amira");
    await select(page, "ebs", "Mathematics (Class 10");
    await page.waitForSelector(".list input[type=checkbox]", { timeout: 15_000 });
    // Pick the first two bank questions.
    await page.evaluate(() => { const boxes = [...document.querySelectorAll(".list input[type=checkbox]")].slice(0, 2); for (const b of boxes) b.click(); });
    await settle(page);
    const picked = await page.evaluate(() => document.querySelectorAll(".list input[type=checkbox]:checked").length);
    if (picked < 2) throw new Error(`Only ${picked} questions picked`);
    await screenshot(page, "diagnostic-editor-desktop");
    await clickText(page, "button", "Save draft");
    await page.waitForFunction(() => /\/exams\/[0-9a-f-]{36}$/.test(location.pathname), { timeout: 15_000 });
    await settle(page);
    await clickText(page, "button", "Publish to");
    await settle(page);
    await page.waitForFunction(() => document.body.innerText.includes("Enter paper answers"), { timeout: 15_000 });
    await clickText(page, "a", "Enter paper answers");
    await page.waitForSelector("#pe-student", { timeout: 15_000 });
    await settle(page);
    const preselected = await page.$eval("#pe-student", (s) => s.value);
    if (!preselected) throw new Error("The single recipient was not preselected");
    // Answer the first option of every question.
    await page.evaluate(() => { for (const fs of document.querySelectorAll("fieldset")) { const r = fs.querySelector("input[type=radio], input[type=checkbox]"); if (r) r.click(); } });
    await settle(page);
    await screenshot(page, "diagnostic-paper-entry-desktop");
    await page.evaluate(() => { const b = [...document.querySelectorAll("button.btn-primary")].find((x) => x.textContent.includes("Save answers and mark")); if (!b || b.disabled) throw new Error("Save button missing or disabled"); b.click(); });
    try { await page.waitForFunction(() => location.pathname.includes("/attempts/"), { timeout: 20_000 }); }
    catch { throw new Error("Paper entry did not navigate to the result; toasts: " + (await page.evaluate(() => [...document.querySelectorAll(".toast")].map((x) => x.textContent.trim()).join(" | ") || "none"))); }
    await page.waitForSelector("#bd-h", { timeout: 20_000 });
    await settle(page);
    await screenshot(page, "diagnostic-paper-result-desktop");
  });

  await step("paper-entry-mobile", viewports.mobile, async (page) => {
    await signIn(page, users.admin);
    await page.goto(`${BASE_URL}/exams`, { waitUntil: "networkidle0" });
    await settle(page);
    await clickText(page, "a", `Puppeteer diagnostic ${stamp}`);
    await page.waitForFunction(() => document.body.innerText.includes("Enter paper answers"), { timeout: 15_000 });
    await clickText(page, "a", "Enter paper answers");
    await page.waitForSelector("#pe-student", { timeout: 15_000 });
    await settle(page);
    await screenshot(page, "diagnostic-paper-entry-mobile");
  });

  await step("teacher-question-bank-fields", viewports.desktop, async (page) => {
    await signIn(page, users.teacher);
    await page.goto(`${BASE_URL}/questions`, { waitUntil: "networkidle0" });
    await settle(page);
    await clickText(page, "button", "New question");
    await page.waitForSelector("#qch");
    await page.waitForSelector("#qimg");
    await screenshot(page, "question-editor-desktop");
  });

  await step("student-sees-released-breakdown", viewports.mobile, async (page) => {
    await signIn(page, "hiro@horizon.example.test");
    await page.goto(`${BASE_URL}/exams`, { waitUntil: "networkidle0" });
    await settle(page);
    await clickText(page, "a", "View result");
    await page.waitForSelector("#bd-h", { timeout: 15_000 });
    await settle(page);
    const hasRecordButton = await page.evaluate(() => [...document.querySelectorAll("button")].some((b) => b.textContent.includes("Record ")));
    if (hasRecordButton) throw new Error("A student can see the staff record button");
    await screenshot(page, "student-diagnostic-breakdown-mobile");
  });
} finally { await browser.close(); }

let failed = 0;
for (const r of results) {
  console.log(`${r.ok ? "PASS" : "FAIL"} ${r.name}${r.ok ? ` (overflow ${r.overflow}px)` : ` — ${r.error}`}`);
  console.log(`     console errors: ${r.consoleErrors.length}, failed requests: ${r.failedRequests.length}`);
  for (const e of [...r.consoleErrors, ...r.failedRequests]) console.log("     - " + e.slice(0, 200));
  if (!r.ok || r.consoleErrors.length || r.failedRequests.length || (r.overflow ?? 0) > 0) failed++;
}
process.exit(failed ? 1 : 0);
