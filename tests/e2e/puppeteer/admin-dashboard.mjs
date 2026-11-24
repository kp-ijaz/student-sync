// Admin home (owner request 2026-11-24): six number tiles and one grid of cards (four charts, needs you, classes, fees). Checks that every
// chart card ends up with a chart or a plain empty message (never a stuck placeholder), that chart figures have text
// next to them, and that nothing overflows at 375/768/1440. Read-only: it changes no data.
import { BASE_URL, launch, newPage, signIn, users, viewports, screenshot } from "./browser.mjs";

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => { throw new Error(msg); };

const browser = await launch();
const { page, consoleErrors, failedRequests } = await newPage(browser);
try {
  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/admin`, { waitUntil: "networkidle0" });
  await page.waitForSelector(".dash-grid");
  await page.waitForFunction(() => !document.querySelector(".dash-grid .dash-sk"), { timeout: 20000 });

  const cards = await page.$$eval(".dash-grid > .card", (cs) => cs.map((c) => ({
    title: c.querySelector("h2")?.textContent.trim(),
    link: c.querySelector(".card-head a")?.getAttribute("href"),
    chart: c.querySelector(".donut") ? "ring" : c.querySelector(".mbars") ? "bars" : c.querySelector(".dash-bars") ? "rows" : "empty",
    text: c.textContent.replace(/\s+/g, " ").trim().slice(0, 260),
  })));
  console.log(JSON.stringify(cards, null, 1));
  const titles = cards.map((c) => c.title);
  for (const t of ["Money in and out", "Attendance", "Students by class", "Enquiries this month", "Needs you", "Coming up", "Fees due"])
    if (!titles.includes(t)) fail(`Missing chart card: ${t}`);
  if (cards.some((c) => !c.link && c.title !== "Needs you")) fail("Every card should link to its page");

  const metrics = await page.$$eval(".dash-kpis .kpi", (ms) => ms.map((m) => m.textContent.replace(/\s+/g, " ").trim()));
  console.log("metrics:", metrics.join(" | "));
  for (const m of ["Students", "Teachers", "Classes", "Income", "Fees due"])
    if (!metrics.some((x) => x.includes(m))) fail(`Missing number: ${m}`);
  if (!metrics.some((x) => /Profit|Loss/.test(x))) fail("Missing number: Profit");

  // Every ring legend row carries its own figure (never colour alone).
  const legends = await page.$$eval(".donut-legend li", (ls) => ls.map((l) => l.textContent.replace(/\s+/g, " ").trim()));
  if (legends.some((l) => !/\d/.test(l))) fail(`Legend row without a figure: ${legends.join(" / ")}`);

  // Small laptop and wide laptop too: the grid follows the content width, which changes with the docked side menu.
  const sizes = { ...viewports, laptop: { width: 1024, height: 768 }, wide: { width: 1280, height: 800 }, small: { width: 360, height: 740 } };
  for (const [size, viewport] of Object.entries(sizes)) {
    await page.setViewport(viewport);
    await wait(600);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    if (overflow > 0) fail(`Dashboard overflows by ${overflow}px at ${size}`);
    // Nothing inside a tile or card may be cut off or spill out of it.
    const spill = await page.evaluate(() => [...document.querySelectorAll(".kpi, .dash-grid > .card")].filter((c) => c.scrollWidth > c.clientWidth + 1).map((c) => c.textContent.trim().slice(0, 30)));
    if (spill.length) fail(`Content spills at ${size}: ${spill.join(" / ")}`);
    await screenshot(page, `admin-dashboard-${size}`);
  }
  if (consoleErrors.length) fail(`Console errors: ${consoleErrors.join(" | ")}`);
  if (failedRequests.length) fail(`Failed requests: ${failedRequests.join(" | ")}`);
  console.log("admin-dashboard: OK");
} finally {
  await browser.close();
}
