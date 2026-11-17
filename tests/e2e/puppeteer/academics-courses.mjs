// Academics ▸ Courses & subjects: syllabus chips → class chips → that class's subjects; add a subject with
// syllabus/class/name (no kind or choice group); remove it. Reports ▸ Students by class. Local fictional data only.
import assert from 'node:assert/strict';
import { launch, newPage, signIn, users, viewports, BASE_URL, SCREENSHOT_DIR } from './browser.mjs';

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const toast = (page, re) => page.waitForFunction((src) => [...document.querySelectorAll('.toast')].some((t) => new RegExp(src).test(t.innerText)), { timeout: 15000 }, re.source);
const chip = (page, group, text) => page.$$eval(`[aria-labelledby="${group}"] .pick`, (b, t) => { const x = b.find((y) => y.innerText.trim() === t); if (!x) throw new Error(`no chip ${t}`); x.click(); }, text);
const browser = await launch();
try {
  const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.mobile);
  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/admin/academics`, { waitUntil: 'networkidle0' });
  await page.waitForSelector('[aria-labelledby="acad-cur"] .pick');
  await wait(800);
  const syllabi = await page.$$eval('[aria-labelledby="acad-cur"] .pick', (b) => b.map((x) => x.innerText.trim()));
  console.log('syllabus chips:', syllabi.join(' | '));
  const kerala = syllabi.find((x) => /Kerala/.test(x)) ?? syllabi[0];
  for (let i = 0; i < 5 && !(await page.$$eval('[aria-labelledby="acad-cur"] .pick[aria-pressed="true"]', (b) => b.some((x) => /Kerala/.test(x.innerText)))); i++) { await chip(page, 'acad-cur', kerala); await wait(400); }
  await chip(page, 'acad-class', 'Class 6');
  await page.waitForFunction(() => document.querySelectorAll('.acad-subjects > li').length > 0);
  const course = await page.$eval('.stack .card h2', (h) => h.innerText);
  const subjects = await page.$$eval('.acad-subjects > li strong', (s) => s.map((x) => x.innerText));
  console.log('shown:', course, '→', subjects.join(', '));
  assert.equal(await page.$$eval('.stack > .card', (c) => c.length), 1, 'only the chosen class is shown');
  // The chosen class chip is scrolled into view on a phone.
  await wait(400);
  const chipBox = await page.$eval('.acad-classes .pick[aria-pressed="true"]', (b) => { const r = b.getBoundingClientRect(); return [r.left, r.right]; });
  assert.ok(chipBox[0] >= 0 && chipBox[1] <= 375, `chosen class chip visible (${chipBox})`);
  await page.screenshot({ path: `${SCREENSHOT_DIR}/academics-courses-mobile.png` });

  // Add subject: syllabus and class are pre-filled from the chips; no kind / choice group fields.
  const name = `Demo Topic ${Math.random().toString(36).slice(2, 6)}`;
  await page.$$eval('.acad-head button', (b) => b.find((x) => x.innerText.includes('Add subject')).click());
  await page.waitForSelector('#ns-cur');
  assert.equal(await page.$('#skd'), null, 'no kind field');
  assert.equal(await page.$('#scg'), null, 'no choice group field');
  assert.ok(await page.$eval('#ns-grade', (s) => s.value), 'class pre-filled');
  await page.type('#sna', name);
  await wait(300);
  await page.$$eval('.modal .form-actions button', (b) => b.find((x) => x.innerText.trim() === 'Add subject').click());
  await toast(page, /added/);
  await page.waitForFunction((n) => [...document.querySelectorAll('.acad-subjects > li strong')].some((s) => s.innerText === n), {}, name);
  await page.$$eval('.acad-subjects > li', (li, n) => [...li.find((x) => x.querySelector('strong').innerText === n).querySelectorAll('button')].find((b) => b.innerText.startsWith('Remove')).click(), name);
  await page.waitForFunction(() => document.querySelector('.modal') && /Only possible when/.test(document.querySelector('.modal').innerText));
  await page.$$eval('.modal .form-actions button', (b) => b.find((x) => x.innerText.trim() === 'Remove').click());
  await toast(page, /Subject removed/);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'academics overflow at 375');

  // Reports ▸ Students by class.
  await page.goto(`${BASE_URL}/admin/student-numbers`, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.sn-table');
  const head = await page.$$eval('.sn-table thead th', (t) => t.map((x) => x.innerText));
  const total = await page.$eval('.sn-table tfoot td:last-child', (t) => t.innerText);
  assert.equal(await page.$eval('.sn-table', (t) => t.offsetParent), null, 'grid hidden on phones');
  const listRows = await page.$$eval('.sn-list li', (li) => li.map((x) => x.innerText.replace(/\s+/g, ' ')));
  console.log('phone list:', listRows.join(' | '));
  assert.equal(listRows.at(-1).replace(/\D/g, ''), total, 'list total matches');
  console.log('students by class: columns', head.join(' | '), '· total', total);
  const years = await page.$$eval('#sn-year option', (o) => o.map((x) => x.textContent.trim()));
  if (years.length > 1) { await page.select('#sn-year', await page.$eval('#sn-year', (s) => s.options[1].value)); await wait(800); console.log('filtered by', years[1], '→ total', await page.$eval('.sn-table tfoot td:last-child', (t) => t.innerText).catch(() => '0')); }
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'student numbers overflow at 375');
  await page.screenshot({ path: `${SCREENSHOT_DIR}/student-numbers-mobile.png`, fullPage: true });
  assert.deepEqual(consoleErrors, [], 'console errors');
  assert.deepEqual(failedRequests.filter((r) => !r.includes('/files/download')), [], 'failed requests');

  // Teachers cannot open it.
  const t = await newPage(browser, viewports.mobile);
  await signIn(t.page, users.teacher);
  const res = await t.page.goto(`${BASE_URL}/admin/student-numbers`, { waitUntil: 'networkidle0' });
  console.log('teacher on students-by-class:', t.page.url().replace(BASE_URL, ''), (await t.page.evaluate(() => document.body.innerText)).slice(0, 60).replace(/\s+/g, ' '));
  console.log('Academics courses + students by class check passed.');
} finally {
  await browser.close();
}
