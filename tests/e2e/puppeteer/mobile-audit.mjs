// Phone-width layout audit for every menu screen of each role (responsive-ui-implementation skill).
// For each page at 375×812 it records: horizontal overflow, elements whose box leaves the screen
// (outside any scrolling container), touch targets below 44 px, console errors and failed requests,
// plus two viewport screenshots (top and mid-scroll, to catch sticky/fixed overlap while scrolling).
//
//   node puppeteer/mobile-audit.mjs [admin|teacher|student ...]      (default: all three)
//   AUDIT_WIDTH=768 node puppeteer/mobile-audit.mjs                   (tablet)
//
// Writes screenshots/mobile-audit/<role>-<page>-{top,mid}.png and screenshots/mobile-audit/report.json.
import fs from "node:fs";
import path from "node:path";
import { launch, newPage, signIn, users, BASE_URL, SCREENSHOT_DIR } from "./browser.mjs";

const width = Number(process.env.AUDIT_WIDTH ?? 375);
const viewport = { width, height: width >= 768 ? 1024 : 812, isMobile: width < 768, hasTouch: true, deviceScaleFactor: 2 };
const outDir = path.join(SCREENSHOT_DIR, width === 375 ? "mobile-audit" : `audit-${width}`);
fs.mkdirSync(outDir, { recursive: true });

const pages = {
  admin: ["/admin", "/admin/students", "/admin/teachers", "/sessions", "/sessions/new", "/admin/fees", "/admin/reports",
    "/admin/academics", "/admin/settings", "/videos", "/assignments", "/exams", "/doubts", "/messages", "/announcements",
    "/profile", "/admin/students/new", "/admin/subjects", "/admin/student-numbers", "/admin/salaries", "/progress-reports", "/class-requests", "/admin/settings?tab=automations",
    "/admin/finance", "/admin/fee-plans", "/marketing", "/marketing/leads", "/marketing/follow-ups", "/marketing/campaigns", "/marketing/team",
    "/questions", "/notifications", "#student", "#session", "#teacher"],
  teacher: ["/teacher", "/sessions", "/teacher/students", "/teacher/tasks", "/teacher/availability", "/teacher/profile", "/progress-reports", "/class-requests", "/videos", "/assignments",
    "/exams", "/questions", "/doubts", "/messages", "/profile", "#student", "#session"],
  student: ["/student", "/subjects", "/timetable", "/learning", "/learning-plan", "/doubts", "/assistant", "/messages",
    "/announcements", "/profile", "#subject", "#session"],
};
const accounts = { admin: users.admin, teacher: users.teacher, student: users.student };

// "#name" entries open the first matching detail link found on an earlier page.
const detailFrom = {
  "#student": { from: { admin: "/admin/students", teacher: "/teacher/students" }, match: /^\/students\/[0-9a-f-]{36}$/ },
  "#session": { from: { admin: "/sessions", teacher: "/sessions", student: "/timetable" }, match: /^\/sessions\/[0-9a-f-]{36}$/ },
  "#subject": { from: { student: "/subjects" }, match: /^\/subjects\/[0-9a-f-]{36}/ },
  "#teacher": { from: { admin: "/admin/teachers" }, match: /^\/teachers\/[0-9a-f-]{36}$/ },
};

const measure = () => {
  const vw = document.documentElement.clientWidth;
  const out = { overflowX: document.documentElement.scrollWidth - vw, offscreen: [], smallTargets: [], smallCount: 0 };
  const name = (el) => {
    const t = (el.getAttribute("aria-label") || el.innerText || el.value || "").trim().replace(/\s+/g, " ").slice(0, 40);
    const cls = typeof el.className === "string" ? el.className.trim().split(/\s+/).slice(0, 3).join(".") : "";
    return `${el.tagName.toLowerCase()}${cls ? "." + cls : ""}${t ? ` "${t}"` : ""}`;
  };
  const inScroller = (el) => {
    for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
      const s = getComputedStyle(p);
      if (/(auto|scroll|hidden)/.test(s.overflowX) && p.scrollWidth > p.clientWidth + 1) return true;
    }
    return false;
  };
  const visible = (el) => {
    const s = getComputedStyle(el);
    if (s.visibility === "hidden" || s.display === "none" || Number(s.opacity) === 0) return false;
    // Content of a closed <details> is not rendered (Chrome still reports a box for it); its <summary> is.
    const closed = el.closest("details:not([open])");
    if (closed && !el.closest("summary")) return false;
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  };
  for (const el of document.querySelectorAll("main *, .content *")) {
    if (!visible(el)) continue;
    const r = el.getBoundingClientRect();
    if ((r.right > vw + 1 || r.left < -1) && !inScroller(el) && out.offscreen.length < 8) out.offscreen.push(`${name(el)} [${Math.round(r.left)}..${Math.round(r.right)}]`);
  }
  for (const el of document.querySelectorAll("a[href], button, input:not([type=hidden]), select, textarea, [role=button], [role=tab], summary")) {
    if (!visible(el) || el.closest(".sr-only, .visually-hidden")) continue;
    const r = el.getBoundingClientRect();
    // Inline text links inside sentences are exempt (WCAG 2.5.8 inline exception).
    if (el.tagName === "A" && getComputedStyle(el).display === "inline" && el.closest("p, li, td, .muted, small")) continue;
    if (r.height < 43.5 || r.width < 43.5) {
      out.smallCount++;
      if (out.smallTargets.length < 10) out.smallTargets.push(`${name(el)} ${Math.round(r.width)}×${Math.round(r.height)}`);
    }
  }
  out.title = document.title;
  out.height = document.documentElement.scrollHeight;
  return out;
};

const roles = process.argv.slice(2).length ? process.argv.slice(2) : Object.keys(pages);
const browser = await launch();
const report = {};
try {
  for (const role of roles) {
    const { page, consoleErrors, failedRequests } = await newPage(browser, viewport);
    await signIn(page, accounts[role]);
    const found = {};
    for (const entry of pages[role]) {
      let url = entry;
      if (entry.startsWith("#")) {
        url = found[entry];
        if (!url) { report[`${role} ${entry}`] = { skipped: "no link found" }; continue; }
      }
      consoleErrors.length = 0; failedRequests.length = 0;
      await page.goto(`${BASE_URL}${url}`, { waitUntil: "networkidle0", timeout: 60000 });
      await new Promise((r) => setTimeout(r, 600));
      // Remember detail links for later "#" entries.
      const links = await page.$$eval("a[href]", (as) => as.map((a) => new URL(a.href).pathname));
      for (const [key, d] of Object.entries(detailFrom))
        if (!found[key] && d.from[role] === url) found[key] = links.find((l) => d.match.test(l));
      const m = await page.evaluate(measure);
      const slug = `${role}-${(entry.startsWith("#") ? entry.slice(1) : url).replace(/^\//, "").replace(/[^a-z0-9]+/gi, "-") || "home"}`;
      await page.screenshot({ path: path.join(outDir, `${slug}-top.png`) });
      if (m.height > viewport.height * 1.3) {
        await page.evaluate(() => window.scrollTo(0, Math.min(document.documentElement.scrollHeight / 2, 1400)));
        await new Promise((r) => setTimeout(r, 400));
        await page.screenshot({ path: path.join(outDir, `${slug}-mid.png`) });
        await page.evaluate(() => window.scrollTo(0, 0));
      }
      report[`${role} ${url}`] = { slug, ...m, consoleErrors: [...consoleErrors], failedRequests: [...failedRequests] };
      const flag = m.overflowX > 0 || m.offscreen.length || consoleErrors.length || failedRequests.length ? "!!" : "ok";
      console.log(`${flag} ${role} ${url}: overflow ${m.overflowX}px, offscreen ${m.offscreen.length}, small targets ${m.smallCount}, errors ${consoleErrors.length}/${failedRequests.length}`);
    }
    await page.close();
  }
} finally {
  await browser.close();
  fs.writeFileSync(path.join(outDir, "report.json"), JSON.stringify(report, null, 2));
}
