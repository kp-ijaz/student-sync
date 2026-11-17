// Learning ▸ Subjects: add a subject to a class, rename it, remove it (owner request 2026-11-17), on local fictional data.
import assert from 'node:assert/strict';
import { launch, newPage, signIn, users, viewports, BASE_URL, SCREENSHOT_DIR } from './browser.mjs';

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const toast = (page, re) => page.waitForFunction((src) => [...document.querySelectorAll('.toast')].some((t) => new RegExp(src).test(t.innerText)), { timeout: 15000 }, re.source);
const browser = await launch();
try {
  const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.mobile);
  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/admin/subjects`, { waitUntil: 'networkidle0' });
  await wait(800);
  const name = `Demo Subj ${Math.random().toString(36).slice(2, 6)}`;
  await page.$$eval('button', (b) => b.find((x) => x.innerText.trim() === 'Add subject').click());
  await page.waitForSelector('#ns-g');
  await page.select('#ns-g', await page.$eval('#ns-g', (s) => [...s.options].find((o) => /Class 6/.test(o.textContent)).value));
  await page.select('#ns-c', await page.$eval('#ns-c', (s) => [...s.options].find((o) => o.value).value));
  await page.type('#ns-n', name);
  await wait(300);
  await page.screenshot({ path: `${SCREENSHOT_DIR}/subjects-add-mobile.png` });
  await page.$$eval('.modal .form-actions button', (b) => b.find((x) => x.innerText.trim() === 'Add subject').click());
  await toast(page, /added/);
  await page.waitForFunction((n) => [...document.querySelectorAll('.subject-overview li')].some((li) => li.innerText.includes(n)), {}, name);
  const row = await page.$$eval('.subject-overview li', (li, n) => li.find((x) => x.innerText.includes(n)).innerText.replace(/\s+/g, ' '), name);
  console.log('added:', row.slice(0, 120));
  await page.$$eval('.subject-overview li', (li, n) => [...li.find((x) => x.innerText.includes(n)).querySelectorAll('button')].find((b) => b.innerText.startsWith('Edit')).click(), name);
  await page.waitForSelector('#es-n');
  await page.click('#es-n', { clickCount: 3 });
  await page.type('#es-n', `${name} B`);
  await wait(300);
  await page.$$eval('.modal .form-actions button', (b) => b.find((x) => x.innerText.trim() === 'Save').click());
  await toast(page, /Subject saved/);
  await page.$eval('input[aria-label="Search subjects"]', (el, v) => { el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); }, `${name} B`);
  await page.waitForFunction((n) => [...document.querySelectorAll('.subject-overview li')].some((li) => li.innerText.includes(n)), {}, `${name} B`);
  await page.$$eval('.subject-overview li', (li, n) => [...li.find((x) => x.innerText.includes(n)).querySelectorAll('button')].find((b) => b.innerText.startsWith('Remove')).click(), `${name} B`);
  await page.waitForFunction(() => document.querySelector('.modal') && /Remove Demo Subj/.test(document.querySelector('.modal').innerText));
  await page.$$eval('.modal .form-actions button', (b) => b.find((x) => x.innerText.trim() === 'Remove').click());
  await toast(page, / removed\./);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'no overflow at 375');
  assert.deepEqual(consoleErrors, [], 'console errors');
  assert.deepEqual(failedRequests.filter((r) => !r.includes('/files/download')), [], 'failed requests');
  console.log('Subjects add/edit/remove check passed.');
} finally {
  await browser.close();
}
