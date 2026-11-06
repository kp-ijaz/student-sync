// Whole-app UI audit: full-page screenshots of every main page per role at 375/768/1440,
// plus horizontal overflow, small touch targets (< 44 px on phones), console errors and failed requests.
// Usage: node puppeteer/ui-audit.mjs [prefix] [role,role] [mobile,tablet,desktop]
import fs from "node:fs";
import path from "node:path";
import { BASE_URL, SCREENSHOT_DIR, launch, newPage, signIn, users, viewports } from "./browser.mjs";

const prefix = process.argv[2] ?? "audit";
const onlyRoles = process.argv[3]?.split(",");
const onlyVps = process.argv[4]?.split(",");
const flows = {
  student: { user: users.student, pages: ["/student", "/subjects", "/timetable", "/learning", "/doubts", "/assistant", "/messages", "/announcements", "/notifications", "/profile", "/progress", "/learning-plan"] },
  teacher: { user: users.teacher, pages: ["/teacher", "/sessions", "/sessions/new", "/teacher/students", "/teacher/tasks", "/teacher/availability", "/assignments", "/exams", "/questions", "/doubts", "/videos"] },
  admin: { user: users.admin, pages: ["/admin", "/admin/students", "/admin/teachers", "/admin/batches", "/admin/academics", "/admin/arrangements", "/admin/reports", "/admin/settings", "/admin/parents", "/sessions"] },
};
const vps = Object.fromEntries(Object.entries(viewports).filter(([k]) => !onlyVps || onlyVps.includes(k)));
const dir = path.join(SCREENSHOT_DIR, prefix);
fs.mkdirSync(dir, { recursive: true });

const browser = await launch();
const summary = [];
try {
  for (const [role, flow] of Object.entries(flows)) {
    if (onlyRoles && !onlyRoles.includes(role)) continue;
    for (const [vpName, vp] of Object.entries(vps)) {
      const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
      await signIn(page, flow.user);
      for (const route of flow.pages) {
        const before = consoleErrors.length + failedRequests.length;
        await page.goto(`${BASE_URL}${route}`, { waitUntil: "networkidle0" });
        await new Promise((r) => setTimeout(r, 700));
        const info = await page.evaluate((isPhone) => {
          const overflow = document.documentElement.scrollWidth - document.documentElement.clientWidth;
          const wide = [];
          if (overflow > 0) {
            for (const el of document.querySelectorAll("body *")) {
              const r = el.getBoundingClientRect();
              if (r.right > document.documentElement.clientWidth + 1 && r.width > 0 && getComputedStyle(el).position !== "fixed") {
                wide.push(`${el.tagName.toLowerCase()}.${[...el.classList].join(".")} right=${Math.round(r.right)}`);
                if (wide.length > 5) break;
              }
            }
          }
          const small = [];
          if (isPhone) {
            for (const el of document.querySelectorAll("a[href], button, input:not([type=hidden]), select, [role=tab]")) {
              const r = el.getBoundingClientRect();
              if (r.width === 0 || r.height === 0) continue;
              const cs = getComputedStyle(el);
              if (cs.visibility === "hidden") continue;
              if (el.closest(".sidebar")) continue;
              if ((r.height < 40 || r.width < 40) && !(el.tagName === "A" && cs.display === "inline")) {
                small.push(`${el.tagName.toLowerCase()}${el.className ? "." + String(el.className).split(" ").join(".") : ""} "${(el.textContent || el.getAttribute("aria-label") || "").trim().slice(0, 24)}" ${Math.round(r.width)}x${Math.round(r.height)}`);
              }
            }
          }
          return { overflow, wide, small: small.slice(0, 8), smallCount: small.length, h1: document.querySelector("h1")?.textContent?.trim() };
        }, vp.width < 768);
        const name = `${role}${route.replace(/\//g, "_")}-${vpName}`;
        await page.screenshot({ path: path.join(dir, `${name}.png`), fullPage: true });
        const errs = consoleErrors.length + failedRequests.length - before;
        const line = `${info.overflow > 0 || errs ? "!!" : "ok"} ${role} ${route} @${vp.width} h1=${JSON.stringify(info.h1)} overflow=${info.overflow} small=${info.smallCount} errs=${errs}`;
        console.log(line);
        if (info.wide.length) console.log("   wide: " + info.wide.join(" | "));
        if (info.small.length) console.log("   small: " + info.small.join(" | "));
        summary.push(line);
      }
      for (const e of [...consoleErrors, ...failedRequests]) console.log(`  [${role}/${vpName}] ${e.slice(0, 220)}`);
      await page.close();
    }
  }
} finally {
  await browser.close();
}
console.log(`\nScreenshots: ${dir}`);
