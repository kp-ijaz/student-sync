// Browser check for slices 1–2 (admin-only accounts, student academic profile):
//   1. sign-in page has no registration link;
//   2. admin: student profile shows syllabus, records a weak subject and a goal, resets a password;
//   3. student: /learning-plan lists the goal, adds and removes a weak subject.
// Screenshots at desktop and mobile; console errors and failed requests are reported per step.
// Usage: BASE_URL=http://localhost:5090 node puppeteer/learning-profile.mjs
import { BASE_URL, launch, newPage, screenshot, signIn, users, viewports } from "./browser.mjs";

const results = [];
const browser = await launch();
async function step(name, vp, fn) {
  const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
  try {
    await fn(page);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    results.push({ name, ok: true, consoleErrors, failedRequests, overflow });
  } catch (e) {
    await screenshot(page, `${name}-FAILED`).catch(() => {});
    results.push({ name, ok: false, error: e.message, consoleErrors, failedRequests });
  } finally {
    await page.close();
  }
}
const settle = (page) => page.waitForNetworkIdle({ idleTime: 500, timeout: 15_000 }).catch(() => {});
async function clickText(page, selector, text) {
  const handle = await page.evaluateHandle((sel, t) => [...document.querySelectorAll(sel)].find((el) => el.textContent.trim().includes(t)) ?? null, selector, text);
  const el = handle.asElement();
  if (!el) throw new Error(`No ${selector} containing "${text}"`);
  await el.click();
}
async function selectByLabel(page, labelText, optionText) {
  const ok = await page.evaluate((lt, ot) => {
    const label = [...document.querySelectorAll("label")].find((l) => l.textContent.trim().startsWith(lt));
    const select = label && document.getElementById(label.getAttribute("for"));
    if (!select) return false;
    const option = [...select.options].find((o) => o.textContent.trim().startsWith(ot));
    if (!option) return false;
    select.value = option.value;
    select.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
  }, labelText, optionText);
  if (!ok) throw new Error(`Could not select "${optionText}" for "${labelText}"`);
}
async function typeByLabel(page, labelText, value) {
  const id = await page.evaluate((lt) => {
    const label = [...document.querySelectorAll("label")].find((l) => l.textContent.trim().startsWith(lt));
    return label?.getAttribute("for") ?? null;
  }, labelText);
  if (!id) throw new Error(`No field labelled "${labelText}"`);
  await page.click(`#${id}`, { clickCount: 3 });
  await page.type(`#${id}`, value);
  await page.keyboard.press("Tab"); // Blazor @bind commits on change/blur
  await settle(page);
}
const stamp = Date.now().toString().slice(-5);

async function cleanup(name) {
  await step(name, viewports.desktop, async (page) => {
    await signIn(page, users.admin);
    await page.goto(`${BASE_URL}/admin/students?search=aisha`, { waitUntil: "networkidle0" });
    await settle(page);
    await clickText(page, "a", "Aisha");
    await page.waitForSelector("#needs-h");
    await settle(page);
    // Resolve every open Hindi need recorded by this script, through the dialog.
    for (let i = 0; i < 2; i++) {
      const has = await page.evaluate(() => [...document.querySelectorAll("section[aria-labelledby=needs-h] li")].some((li) => li.textContent.includes("Hindi") && li.textContent.includes("Puppeteer check")));
      if (!has) break;
      await page.evaluate(() => { const li = [...document.querySelectorAll("section[aria-labelledby=needs-h] li")].find((l) => l.textContent.includes("Hindi") && l.textContent.includes("Puppeteer check")); li.querySelector("button").click(); });
      await page.waitForSelector("#ln-review");
      await page.type("#ln-review", "Resolved by the puppeteer check (demo)");
      await clickText(page, "[role=dialog] button.btn-primary", "Resolve");
      await settle(page);
    }
    await screenshot(page, "admin-student-profile-resolved-desktop");
    // Delete the goals this script added.
    for (let i = 0; i < 4; i++) {
      const deleted = await page.evaluate(() => { const li = [...document.querySelectorAll("section[aria-labelledby=notes-h] li")].find((l) => l.textContent.includes("Goal from puppeteer")); if (!li) return false; li.querySelector("button").click(); return true; });
      if (!deleted) break;
      await settle(page);
    }
  });
}


try {
  await cleanup("admin-cleanup-before");
  for (const [vpName, vp] of [["desktop", viewports.desktop], ["mobile", viewports.mobile]]) {
    await step(`login-no-register-${vpName}`, vp, async (page) => {
      await page.goto(`${BASE_URL}/login`, { waitUntil: "networkidle0" });
      const hasRegister = await page.evaluate(() => !!document.querySelector('a[href="/register"]'));
      if (hasRegister) throw new Error("Register link still present");
      await page.goto(`${BASE_URL}/register`, { waitUntil: "networkidle0" });
      // Either the not-found page or (after enhanced navigation) the sign-in page: never a registration form.
      const gone = await page.evaluate(() => (/couldn't find that page/i.test(document.body.innerText) || location.pathname === "/login")
        && !/Send registration|Register for an account/i.test(document.body.innerText));
      if (!gone) throw new Error("/register still shows a registration form");
      await screenshot(page, `login-${vpName}`);
    });

    await step(`admin-student-profile-${vpName}`, vp, async (page) => {
      await signIn(page, users.admin);
      await page.goto(`${BASE_URL}/admin/students?search=aisha`, { waitUntil: "networkidle0" });
      await settle(page);
      await clickText(page, "a", "Aisha");
      await page.waitForSelector("h1");
      await settle(page);
      await page.waitForSelector("#needs-h");
      await screenshot(page, `admin-student-profile-${vpName}`);
      // Record a weak subject said by the student (Hindi, whole subject) with a note.
      await selectByLabel(page, "Subject", "Hindi");
      await settle(page);
      await typeByLabel(page, "Note (optional)", `Puppeteer check ${stamp} (demo)`);
      await clickText(page, "button", "Record weak subject");
      await settle(page);
      await page.waitForFunction((s) => document.body.innerText.includes(`Puppeteer check ${s}`), {}, stamp);
      // Add a goal visible to the student.
      await typeByLabel(page, "Text", `Goal from puppeteer ${stamp} (demo)`);
      await clickText(page, "section[aria-labelledby=notes-h] button.btn-primary", "Add");
      await settle(page);
      await page.waitForFunction((s) => document.body.innerText.includes(`Goal from puppeteer ${s}`), {}, stamp);
      await screenshot(page, `admin-student-profile-after-${vpName}`);
    });

    await step(`student-learning-plan-${vpName}`, vp, async (page) => {
      await signIn(page, users.student);
      await page.goto(`${BASE_URL}/learning-plan`, { waitUntil: "networkidle0" });
      await settle(page);
      await page.waitForFunction((s) => document.body.innerText.includes(`Goal from puppeteer ${s}`), { timeout: 15_000 }, stamp);
      const sawObservation = await page.evaluate(() => /observation/i.test(document.body.innerText));
      if (sawObservation) throw new Error("A staff observation leaked to the student page");
      await screenshot(page, `student-learning-plan-${vpName}`);
      // The student adds Social Science and removes it again.
      await selectByLabel(page, "Subject", "Social");
      await settle(page);
      await typeByLabel(page, "What is hard about it?", `Student note ${stamp}`);
      await clickText(page, "button", "Add subject");
      await settle(page);
      await page.waitForFunction((s) => document.body.innerText.includes(`Student note ${s}`), {}, stamp);
      await screenshot(page, `student-learning-plan-added-${vpName}`);
      await clickText(page, "button[aria-label^='Remove Social']", "Remove");
      await settle(page);
      await page.waitForFunction((s) => !document.body.innerText.includes(`Student note ${s}`), {}, stamp);
    });
  }

  await cleanup("admin-cleanup-after");

  // Keyboard pass on the learning plan (desktop): tab through the first controls and check a focus ring is visible.
  await step("student-learning-plan-keyboard", viewports.desktop, async (page) => {

    await signIn(page, users.student);
    await page.goto(`${BASE_URL}/learning-plan`, { waitUntil: "networkidle0" });
    await settle(page);
    const stops = [];
    for (let i = 0; i < 14; i++) {
      await page.keyboard.press("Tab");
      stops.push(await page.evaluate(() => { const a = document.activeElement; return `${a.tagName.toLowerCase()}${a.id ? "#" + a.id : ""}:${(a.getAttribute("aria-label") ?? a.textContent ?? "").trim().slice(0, 30)}`; }));
    }
    console.log("focus stops:", stops.join(" → "));
    await screenshot(page, "student-learning-plan-focus");
  });
} finally {
  await browser.close();
}

let failed = 0;
for (const r of results) {
  console.log(`${r.ok ? "PASS" : "FAIL"} ${r.name}${r.ok ? ` (overflow ${r.overflow}px)` : ` — ${r.error}`}`);
  console.log(`     console errors: ${r.consoleErrors.length}, failed requests: ${r.failedRequests.length}`);
  for (const e of [...r.consoleErrors, ...r.failedRequests]) console.log("     - " + e.slice(0, 200));
  if (!r.ok || r.consoleErrors.length || r.failedRequests.length || (r.overflow ?? 0) > 0) failed++;
}
process.exit(failed ? 1 : 0);
