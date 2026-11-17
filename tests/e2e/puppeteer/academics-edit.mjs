// Academics: add without typing a code, edit, and remove (owner request 2026-11-17), on local fictional data.
import assert from 'node:assert/strict';
import { launch, newPage, signIn, users, viewports, BASE_URL, SCREENSHOT_DIR } from './browser.mjs';

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const toast = (page, re) => page.waitForFunction((src) => new RegExp(src).test(document.body.innerText), { timeout: 15000 }, re.source);
const clickIn = (page, sel, text) => page.$$eval(sel, (els, t) => { const e = els.find((x) => x.innerText.trim().startsWith(t)); if (!e) throw new Error(`no "${t}" (${els.length} candidates)`); e.click(); }, text);
// Clicks a tab until it is selected (the first click can land before the page is interactive).
const openTab = async (page, name) => {
  for (let i = 0; i < 10; i++) {
    await clickIn(page, '[role=tab]', name);
    await wait(400);
    if (await page.$$eval('[role=tab]', (ts, n) => ts.find((t) => t.innerText.trim() === n)?.getAttribute('aria-selected') === 'true', name)) return;
  }
  throw new Error(`tab ${name} did not open`);
};
const browser = await launch();
try {
  const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.mobile);
  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/admin/academics`, { waitUntil: 'networkidle0' });
  const tag = Math.random().toString(36).slice(2, 6);

  // Syllabus: added with only a name; the code appears by itself; then removed.
  await openTab(page, 'Syllabi');
  await clickIn(page, 'button', 'Add syllabus');
  await page.waitForSelector('#cun');
  assert.equal(await page.$('#cuc'), null, 'no code box when adding');
  await page.type('#cun', `Demo Board ${tag}`);
  await page.keyboard.press('Tab'); await wait(300);
  await clickIn(page, '.modal .form-actions button', 'Save');
  await toast(page, /Syllabus saved/);
  await page.waitForFunction((t) => document.body.innerText.includes(`Demo Board ${t}`), {}, tag);
  const row = await page.$$eval('.list li', (li, t) => li.find((x) => x.innerText.includes(`Demo Board ${t}`)).innerText.replace(/\s+/g, ' '), tag);
  console.log('syllabus row:', row);
  assert.match(row, /DB/);
  await page.$$eval('.list li', (li, t) => [...li.find((x) => x.innerText.includes(`Demo Board ${t}`)).querySelectorAll('button')].find((b) => b.innerText.startsWith('Remove')).click(), tag);
  await page.waitForFunction(() => /Only possible when/.test(document.body.innerText) && document.querySelectorAll(".modal .form-actions button").length === 2);
  await page.screenshot({ path: `${SCREENSHOT_DIR}/academics-remove-confirm-mobile.png` });
  await clickIn(page, '.modal .form-actions button', 'Remove');
  await toast(page, /Syllabus removed/);

  // Classes tab: add (order automatic), edit the name, remove.
  await openTab(page, 'Classes');
  await page.waitForSelector('#gname');
  await page.type('#gname', `Demo Level ${tag}`);
  await wait(300);
  await page.click('.grade-add .btn-primary');
  await toast(page, /Class added/);
  await page.waitForFunction((t) => [...document.querySelectorAll('.list li')].some((x) => x.innerText.includes(`Demo Level ${t}`)), {}, tag);
  await page.$$eval('.list li', (li, t) => [...li.find((x) => x.innerText.includes(`Demo Level ${t}`)).querySelectorAll('button')].find((b) => b.innerText.startsWith('Edit')).click(), tag);
  await page.waitForSelector('#ge-n');
  await page.$eval('#ge-n', (el, v) => { el.value = v; el.dispatchEvent(new Event('change', { bubbles: true })); }, `Demo Level ${tag} B`);
  await clickIn(page, '.modal .form-actions button', 'Save');
  await toast(page, /Class saved/);
  await page.waitForFunction((t) => [...document.querySelectorAll('.list li')].some((x) => x.innerText.includes(`Demo Level ${t} B`)), {}, tag);
  await page.screenshot({ path: `${SCREENSHOT_DIR}/academics-classes-mobile.png`, fullPage: false });
  await page.$$eval('.list li', (li, t) => [...li.find((x) => x.innerText.includes(`Demo Level ${t} B`)).querySelectorAll('button')].find((b) => b.innerText.startsWith('Remove')).click(), tag);
  await clickIn(page, '.modal .form-actions button', 'Remove');
  await toast(page, /Class removed/);

  // A class in use explains why it cannot go.
  const used = await page.$$eval('.list li', (li) => li.findIndex((x) => /Class 10/.test(x.innerText)));
  if (used >= 0) {
    await page.evaluate((i) => [...document.querySelectorAll('.list li')[i].querySelectorAll('button')].find((b) => b.innerText.startsWith('Remove')).click(), used);
    await clickIn(page, '.modal .form-actions button', 'Remove');
    await toast(page, /Still used by/);
    console.log('in use:', await page.evaluate(() => [...document.querySelectorAll('.toast')].map((t) => t.innerText).join(' | ').replace(/\s+/g, ' ')));
  }
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'no overflow at 375');
  assert.deepEqual(consoleErrors, [], 'console errors');
  assert.deepEqual(failedRequests.filter((r) => !/academics\/grades\/.* — HTTP 409/.test(r) && !r.includes('/files/download')), [], 'unexpected failed requests');
  console.log('Academics edit/remove check passed.');
} finally {
  await browser.close();
}
