// Captures the current web app at phone width (and desktop for the shell) as the "before" set for the
// Flutter-parity work. Usage: node puppeteer/baseline-mobile.mjs [prefix]
import { BASE_URL, launch, newPage, screenshot, signIn, users, viewports } from "./browser.mjs";

const prefix = process.argv[2] ?? "before";
const flows = {
  student: { user: users.student, pages: ["/student", "/subjects", "/timetable", "/learning", "/doubts", "/profile", "/notifications"] },
  teacher: { user: users.teacher, pages: ["/teacher", "/sessions", "/teacher/students", "/teacher/tasks", "/profile"] },
};

const browser = await launch();
try {
  // Sign-in page, signed out.
  for (const [vpName, vp] of Object.entries({ mobile: viewports.mobile, desktop: viewports.desktop })) {
    const { page } = await newPage(browser, vp);
    await page.goto(`${BASE_URL}/login`, { waitUntil: "networkidle0" });
    console.log(await screenshot(page, `${prefix}-login-${vpName}`));
    await page.close();
  }
  for (const [role, flow] of Object.entries(flows)) {
    for (const [vpName, vp] of Object.entries({ mobile: viewports.mobile, desktop: viewports.desktop })) {
      const { page, consoleErrors, failedRequests } = await newPage(browser, vp);
      await signIn(page, flow.user);
      for (const route of flow.pages) {
        await page.goto(`${BASE_URL}${route}`, { waitUntil: "networkidle0" });
        await new Promise((r) => setTimeout(r, 600));
        const name = `${prefix}-${role}-${route.replace(/\//g, "_").replace(/^_/, "") || "home"}-${vpName}`;
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        console.log(`${await screenshot(page, name)}  overflow=${overflow}`);
      }
      if (vpName === "mobile") {
        await page.goto(`${BASE_URL}${flow.pages[0]}`, { waitUntil: "networkidle0" });
        await page.click(".menu-btn");
        await new Promise((r) => setTimeout(r, 400));
        console.log(await screenshot(page, `${prefix}-${role}-drawer-mobile`));
      }
      // First class page reachable from the sessions/timetable list.
      const href = await page.evaluate(() => [...document.querySelectorAll('a[href^="/sessions/"]')].map(a => a.getAttribute('href')).find(h => /^\/sessions\/[0-9a-f-]{36}$/.test(h)));
      if (href) {
        await page.goto(`${BASE_URL}${href}`, { waitUntil: "networkidle0" });
        await new Promise((r) => setTimeout(r, 600));
        console.log(await screenshot(page, `${prefix}-${role}-class-${vpName}`));
      }
      console.log(`${role}/${vpName}: console errors ${consoleErrors.length}, failed requests ${failedRequests.length}`);
      for (const e of [...consoleErrors, ...failedRequests]) console.log("  - " + e.slice(0, 200));
      await page.close();
    }
  }
} finally {
  await browser.close();
}
