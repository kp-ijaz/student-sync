// Admin edit/delete (2026-11-23) in a real browser: rename/remove chapters and topics, edit and remove a term, edit a
// holiday, edit and delete an announcement, delete a draft assignment and delete a doubt. Test records are created through
// the API with fictional data and removed by the flow itself. Screenshots at 375/768/1440; fails on overflow, console
// errors or failed requests (except the expected 404 after a deleted record is gone).
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

const click = async (page, selector) => { await page.waitForSelector(selector, { timeout: 10000 }); await page.click(selector); await wait(400); };
const toast = (page, re) => page.waitForFunction((src) => [...document.querySelectorAll(".toast")].some((t) => new RegExp(src).test(t.textContent)), { timeout: 10000 }, re.source);
const hasText = (page, text) => page.waitForFunction((t) => document.querySelector("main")?.textContent.includes(t), { timeout: 10000 }, text);
const gone = (page, text) => page.waitForFunction((t) => !document.querySelector("main")?.textContent.includes(t), { timeout: 10000 }, text);

async function shots(page, name) {
  for (const [size, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await wait(500);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    if (overflow > 0) fail(`${name} overflows by ${overflow}px at ${size}`);
    await screenshot(page, `crud2-${name}-${size}`);
  }
  await page.setViewport(viewports.desktop);
}

const browser = await launch();
const { page, consoleErrors, failedRequests } = await newPage(browser);
try {
  // Test data (fictional).
  const subject = (await api(users.admin, "academics/centre-subjects", "POST", { name: `Crud ${tag}` })).id;
  const chapter = `Chapter ${tag}`;
  const chapterId = (await api(users.admin, `academics/subjects/${subject}/chapters`, "POST", { title: chapter, sortOrder: 1 })).id;
  await api(users.admin, `academics/chapters/${chapterId}/topics`, "POST", { title: `Topic ${tag}`, sortOrder: 1 });

  await signIn(page, users.admin);

  // 1. Chapters and topics: rename, remove topic, remove chapter.
  await page.goto(`${BASE_URL}/admin/academics`, { waitUntil: "networkidle0" });
  await clickText(page, ".acad-picker", "Other courses");
  await click(page, `button[aria-label="Rename chapter ${chapter}"]`);
  await fill(page, "#rnm", `${chapter} renamed`);
  await clickText(page, ".modal .form-actions", "Save");
  await hasText(page, `${chapter} renamed`);
  await shots(page, "academics-chapters");
  await click(page, `button[aria-label="Remove topic Topic ${tag}"]`);
  await clickText(page, ".modal .form-actions", "Remove");
  await gone(page, `Topic ${tag}`);
  await click(page, `button[aria-label="Remove chapter ${chapter} renamed"]`);
  await clickText(page, ".modal .form-actions", "Remove");
  await gone(page, `${chapter} renamed`);
  await api(users.admin, `academics/subjects/${subject}`, "DELETE");

  // 2. Terms: edit and remove (in a fictional far-future year).
  const y0 = 2100 + Math.floor(Math.random() * 50);
  const year = (await api(users.admin, "academics/years", "POST", { name: `Year ${tag}`, startDate: `${y0}-04-01`, endDate: `${y0 + 1}-03-31`, isCurrent: false })).id;
  await api(users.admin, `academics/years/${year}/terms`, "POST", { name: `Term ${tag}`, startDate: `${y0}-04-01`, endDate: `${y0}-08-31` });
  await page.goto(`${BASE_URL}/admin/academics`, { waitUntil: "networkidle0" });
  await clickText(page, ".tabs", "Academic years");
  await click(page, `button[aria-label="Edit term Term ${tag}"]`);
  await fill(page, "#tn2", `Term ${tag} edited`);
  await clickText(page, ".modal .form-actions", "Save term");
  await hasText(page, `Term ${tag} edited`);
  await shots(page, "academics-terms");
  await click(page, `button[aria-label="Remove term Term ${tag} edited"]`);
  await clickText(page, ".modal .form-actions", "Remove");
  await gone(page, `Term ${tag} edited`);
  await api(users.admin, `academics/years/${year}`, "DELETE");

  // 3. Holiday: edit the name.
  const hDay = `${2100 + Math.floor(Math.random() * 50)}-01-${String(10 + Math.floor(Math.random() * 18)).padStart(2, "0")}`;
  const holiday = (await api(users.admin, "institution/holidays", "POST", { date: hDay, name: `Holiday ${tag}` })).id;
  await page.goto(`${BASE_URL}/admin/settings`, { waitUntil: "networkidle0" });
  await clickText(page, "main", "Holidays");
  await click(page, `xpath/.//li[contains(., "Holiday ${tag}")]//button[starts-with(normalize-space(.), "Edit")]`);
  await fill(page, 'input[aria-label="Holiday name"]', `Holiday ${tag} edited`);
  await clickText(page, "main", "Save holiday");
  await toast(page, /Holiday saved/);
  await hasText(page, `Holiday ${tag} edited`);
  await shots(page, "holidays");
  await api(users.admin, `institution/holidays/${holiday}`, "DELETE");

  // 4. Announcement: edit, then delete.
  const title = `Notice ${tag}`;
  await api(users.admin, "announcements", "POST", { title, body: "Centre closed on Friday (test).", audience: "Everyone" });
  await page.goto(`${BASE_URL}/announcements`, { waitUntil: "networkidle0" });
  await click(page, `button[aria-label="Edit ${title}"]`);
  await fill(page, "#eb", "Centre closed on Saturday (test).");
  await clickText(page, ".modal .form-actions", "Save");
  await hasText(page, "Centre closed on Saturday (test).");
  await shots(page, "announcements");
  await click(page, `button[aria-label="Delete ${title}"]`);
  await clickText(page, ".modal .form-actions", "Delete");
  await gone(page, title);

  // 5. Draft assignment (by the 1:1 chemistry teacher) deleted by the admin.
  const sessions = await api(users.privateTeacher, "sessions?limit=50");
  const session = sessions.find((s) => s.sessionType === "OneToOne" && s.title.includes("Fatima")) ?? fail("No Fatima class for Priya");
  const assignment = (await api(users.privateTeacher, "assignments", "POST", { title: `Homework ${tag}`, instructionsMarkdown: "Do it.",
    dueAtUtc: new Date(Date.now() + 2 * 864e5).toISOString(), maxMarks: 10, allowLateSubmission: false, allowResubmission: false, sessionId: session.id })).id;
  await page.goto(`${BASE_URL}/assignments/${assignment}`, { waitUntil: "networkidle0" });
  await clickText(page, "main", "Delete");
  await shots(page, "assignment-delete");
  await clickText(page, ".modal .form-actions", "Delete assignment");
  await page.waitForFunction(() => location.pathname === "/assignments", { timeout: 10000 });
  await toast(page, /Assignment deleted/);

  // 6. Doubt posted by a student, deleted by the admin.
  const chemistry = (await api(users.fatima, "academics/subjects")).find((s) => s.name === "Chemistry");
  const doubt = (await api(users.fatima, "doubts", "POST", { title: `Mistake ${tag}`, description: "Posted by mistake (test).", subjectId: chemistry.id, isPrivate: true })).id;
  await page.goto(`${BASE_URL}/doubts/${doubt}`, { waitUntil: "networkidle0" });
  await clickText(page, "main", "Delete");
  await clickText(page, ".modal .form-actions", "Delete doubt");
  await page.waitForFunction(() => location.pathname === "/doubts", { timeout: 10000 });
  await toast(page, /Doubt deleted/);

  if (consoleErrors.length) fail(`Console errors:\n${consoleErrors.join("\n")}`);
  if (failedRequests.length) fail(`Failed requests:\n${failedRequests.join("\n")}`);
  console.log("admin-crud-more: OK — chapters/topics, terms, holidays, announcements, assignment and doubt delete");
} catch (e) {
  await screenshot(page, "crud2-failure").catch(() => {});
  console.error(e.message);
  process.exitCode = 1;
} finally {
  await browser.close();
}
