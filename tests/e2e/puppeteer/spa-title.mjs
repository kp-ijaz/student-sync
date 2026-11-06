// Client-side navigation keeps the app-bar title of the NEW page (PageHeader owner token), tab by tab.
import { BASE_URL, launch, newPage, signIn, users, viewports } from "./browser.mjs";
const browser = await launch();
try {
  const { page, consoleErrors } = await newPage(browser, viewports.mobile);
  await signIn(page, users.student);
  await page.goto(`${BASE_URL}/subjects`, { waitUntil: "networkidle0" });
  const read = () => page.evaluate(() => ({ title: document.querySelector(".appbar-title")?.getAttribute("data-title"), tab: document.querySelector(".tabbar a.selected")?.textContent?.trim(), url: location.pathname }));
  console.log("start", JSON.stringify(await read()));
  let bad = 0;
  for (const [label, expectTitle] of [["Learning", "Learning"], ["Doubts", "Doubt Centre"], ["Subjects", "My subjects"], ["Home", "Home"], ["Learning", "Learning"]]) {
    await page.click(`.tabbar a[title="${label}"]`);
    await page.waitForFunction((l) => document.querySelector(".tabbar a.selected")?.textContent?.trim() === l, {}, label);
    await new Promise((r) => setTimeout(r, 400));
    const r = await read();
    const ok = r.title === expectTitle && r.tab === label;
    if (!ok) bad++;
    console.log(`${ok ? "ok " : "!! "} click ${label}: ${JSON.stringify(r)} (expected title "${expectTitle}")`);
  }
  // Drawer link (SPA) to a page with a PageHeader, then Back.
  await page.click(".menu-btn"); await new Promise((r) => setTimeout(r, 400)); await page.click('.sidebar a[href="/timetable"]');
  await page.waitForFunction(() => location.pathname === "/timetable"); await new Promise((r) => setTimeout(r, 400));
  const t = await read(); console.log(`${t.title === "Timetable" && t.tab === "Subjects" ? "ok " : "!! "} drawer → timetable: ${JSON.stringify(t)}`); if (!(t.title === "Timetable" && t.tab === "Subjects")) bad++;
  await page.goBack({ waitUntil: "networkidle0" }); await new Promise((r) => setTimeout(r, 400));
  const b = await read(); console.log(`${b.title === "Learning" ? "ok " : "!! "} back → learning: ${JSON.stringify(b)}`); if (b.title !== "Learning") bad++;
  console.log(`console errors: ${consoleErrors.length}`); consoleErrors.forEach((e) => console.log("  - " + e.slice(0, 160)));
  process.exitCode = bad || consoleErrors.length ? 1 : 0;
} finally { await browser.close(); }
