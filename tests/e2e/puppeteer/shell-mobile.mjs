// Phone/tablet shell check: app bar title, bell + avatar, floating tab bar, drawer, overflow, console errors.
// Usage: node puppeteer/shell-mobile.mjs [prefix]
import fs from "node:fs";
import path from "node:path";
import { BASE_URL, SCREENSHOT_DIR, launch, newPage, signIn, users, viewports } from "./browser.mjs";

const prefix = process.argv[2] ?? "after";
const vps = { mobile: viewports.mobile, tablet: viewports.tablet, edge: { width: 1024, height: 800 }, desktop: viewports.desktop };
const flows = {
  student: { user: users.student, pages: ["/student", "/subjects", "/learning", "/doubts", "/profile", "/timetable"] },
  teacher: { user: users.teacher, pages: ["/teacher", "/sessions", "/teacher/students", "/teacher/tasks"] },
  admin: { user: users.admin, pages: ["/admin", "/admin/students"] },
};
const shot = async (page, name, full = false) => {
  fs.mkdirSync(SCREENSHOT_DIR, { recursive: true });
  const file = path.join(SCREENSHOT_DIR, `${name}.png`);
  await page.screenshot({ path: file, fullPage: full });
  return file;
};
const browser = await launch();
let problems = 0;
try {
  for (const [role, flow] of Object.entries(flows)) {
    for (const [vpName, vp] of Object.entries(vps)) {
      const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
      await signIn(page, flow.user);
      for (const route of flow.pages) {
        await page.goto(`${BASE_URL}${route}`, { waitUntil: "networkidle0" });
        await new Promise((r) => setTimeout(r, 500));
        const info = await page.evaluate(() => {
          const bar = document.querySelector(".tabbar");
          const barVisible = bar && getComputedStyle(bar).display !== "none";
          const sel = bar?.querySelector("a.selected")?.textContent?.trim() ?? null;
          const title = document.querySelector(".appbar-title");
          const titleVisible = title && getComputedStyle(title).display !== "none" ? title.textContent.trim() : null;
          const h1 = document.querySelector("h1");
          const h1Visible = h1 ? (h1.getBoundingClientRect().width > 2) : null;
          const menu = document.querySelector(".menu-btn");
          return { overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth, barVisible, sel, titleVisible, h1Visible, menuVisible: menu && getComputedStyle(menu).display !== "none" };
        });
        const name = `${prefix}-${role}${route.replace(/\//g, "_")}-${vpName}`;
        await shot(page, name);
        const ok = info.overflow <= 0 && (vp.width >= 1024 ? !info.barVisible && !info.titleVisible : info.barVisible);
        if (!ok) problems++;
        console.log(`${ok ? "ok " : "!! "} ${role} ${route} @${vp.width}: overflow=${info.overflow} bar=${info.barVisible} selected=${JSON.stringify(info.sel)} title=${JSON.stringify(info.titleVisible)} h1Visible=${info.h1Visible} menu=${info.menuVisible}`);
      }
      if (vpName === "mobile") {
        await page.goto(`${BASE_URL}${flow.pages[0]}`, { waitUntil: "networkidle0" });
        await shot(page, `${prefix}-${role}-home-full-mobile`, true);
        await page.click(".menu-btn");
        await new Promise((r) => setTimeout(r, 400));
        await shot(page, `${prefix}-${role}-drawer-mobile`);
      }
      if (consoleErrors.length || failedRequests.length) problems++;
      console.log(`${role}/${vpName}: console errors ${consoleErrors.length}, failed requests ${failedRequests.length}`);
      for (const e of [...consoleErrors, ...failedRequests]) console.log("  - " + e.slice(0, 200));
      await page.close();
    }
  }
} finally {
  await browser.close();
}
console.log(problems ? `PROBLEMS: ${problems}` : "ALL OK");
process.exit(problems ? 1 : 0);
