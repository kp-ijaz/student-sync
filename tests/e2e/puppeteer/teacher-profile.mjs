// Admin adds a teacher with subjects, a qualification and contact details, then sees them in the list.
// Creates one fictional "(Demo Teacher)" account per run with a unique example.test email.
import assert from 'node:assert/strict';
import { launch, newPage, signIn, users, viewports, screenshot, BASE_URL } from './browser.mjs';

const browser = await launch();
try {
  const { page, consoleErrors, failedRequests } = await newPage(browser);
  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/admin/teachers`, { waitUntil: 'networkidle0' });
  await page.$$eval('button', (bs) => bs.find((b) => b.innerText.includes('Add teacher')).click());
  await page.waitForSelector('#tn');

  const stamp = Date.now().toString(36);
  await page.type('#tn', `Sahla Test ${stamp} (Demo Teacher)`);
  await page.type('#te', `sahla-${stamp}@horizon.example.test`);
  await page.type('#tph', '+971 50 555 0101');
  // Whole subject (every class) in one pick, then a single class of another subject.
  await page.select('#tsadd', 'all:Mathematics');
  await page.waitForFunction(() => [...document.querySelectorAll('.teacher-form .badge.removable')].some((b) => /Mathematics\s+\d+ classes/.test(b.innerText)));
  assert.equal(await page.$eval('#tsadd', (s) => s.value), '', 'the add box resets after a pick');
  const arabic = await page.$eval('#tsadd', (s) => [...s.querySelectorAll('optgroup:not(:first-child) option')].find((o) => o.textContent.trim() === 'Arabic')?.value);
  await page.select('#tsadd', arabic);
  await page.waitForFunction(() => [...document.querySelectorAll('.teacher-form .badge.removable')].some((b) => b.innerText.includes('Arabic')));
  assert.equal(await page.$eval('#tsadd', (s) => s.value), '', 'the add box resets after a pick');
  await page.$$eval('button', (bs) => bs.find((b) => b.innerText.includes('Add qualification')).click());
  await page.waitForSelector('#qt0');
  await page.type('#qt0', 'B.Ed');
  await page.type('#qi0', 'University of Calicut');
  await page.type('#qy0', '2016');
  await page.type('#tex', '6');
  await page.type('#tla', 'English, Malayalam, Arabic');

  for (const [name, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await new Promise((r) => setTimeout(r, 400));
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `teacher form overflow at ${name}`);
    await screenshot(page, `teacher-form-${name}`);
  }
  await page.setViewport(viewports.desktop);

  await page.$$eval('.modal button', (bs) => bs.find((b) => b.innerText.trim() === 'Save teacher').click());
  await page.waitForFunction(() => /temporary password/i.test(document.body.innerText), { timeout: 15000 });
  await page.keyboard.press('Escape');
  await page.type('input[aria-label="Search teachers"]', stamp);
  await page.keyboard.press('Tab');
  await page.waitForFunction((s) => [...document.querySelectorAll('tbody tr')].some((r) => r.innerText.includes(s) && r.innerText.includes('Arabic') && r.innerText.includes('B.Ed')), { timeout: 15000 }, stamp);
  const row = await page.$$eval('tbody tr', (rows, s) => rows.find((r) => r.innerText.includes(s)).innerText, stamp);
  console.log('teacher row:', row.replace(/\s+/g, ' '));
  assert.match(row, /Mathematics · \d+ classes/);
  assert.match(row, /6 years/);
  await screenshot(page, 'teacher-list-desktop');

  const unexpected = failedRequests.filter((r) => !r.includes('/files/download'));
  assert.deepEqual(consoleErrors, [], 'console errors');
  assert.deepEqual(unexpected, [], 'failed requests (photo downloads excluded: known missing local files)');
  console.log('Teacher profile check passed at 375, 768 and 1440px.');
} finally {
  await browser.close();
}
