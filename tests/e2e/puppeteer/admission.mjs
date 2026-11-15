// Admin admits a fictional student the tuition-centre way (contact, class, Maths + Arabic only, last school result
// with a PDF report, goal), then checks the student's profile. Creates one "(Demo Student)" per run.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { launch, newPage, signIn, users, viewports, screenshot, BASE_URL } from './browser.mjs';

const pdf = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'ss-admission-')), 'term1-report.pdf');
fs.writeFileSync(pdf, '%PDF-1.4\n% fictional test report\n%%EOF\n');
const pressed = (page, label) => page.$$eval('.admit-subjects button', (bs, l) => bs.find((b) => b.innerText.trim() === l)?.getAttribute('aria-pressed'), label);

const browser = await launch();
let page;
try {
  const opened = await newPage(browser);
  page = opened.page;
  const { consoleErrors, failedRequests } = opened;
  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/admin/students`, { waitUntil: 'networkidle0' });
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.$$eval('a', (as) => as.find((a) => a.innerText.includes('Admit student')).click())]);
  assert.match(page.url(), /\/admin\/students\/new$/);
  await page.waitForSelector('#an');

  const stamp = Date.now().toString(36);
  await page.type('#an', `Hafiz Test ${stamp} (Demo Student)`);
  await page.type('#ae', `hafiz-${stamp}@horizon.example.test`);
  await page.type('#aph', '+971 50 222 0101');
  await page.type('#agn', 'Fictional Parent');
  await page.type('#agp', '+971 50 222 0202');
  await page.type('#asc', 'Fictional Public School');
  const grade = await page.$eval('#ag', (s) => [...s.options].find((o) => /7/.test(o.textContent))?.value ?? [...s.options].find((o) => o.value)?.value);
  await page.select('#ag', grade);

  // Tuition subjects from the centre's simple list; "Add" creates them the first time and selects them.
  for (const name of ['Maths', 'Arabic']) {
    console.log('subject', name, 'pressed =', await pressed(page, name));
    if (await pressed(page, name) === null || await pressed(page, name) === undefined) {
      await page.type('#anew', name);
      await page.waitForFunction(() => [...document.querySelectorAll('button')].some((b) => b.innerText.trim() === 'Add' && !b.disabled));
      await page.$$eval('button', (bs) => bs.find((b) => b.innerText.trim() === 'Add').click());
      await page.waitForFunction((n) => [...document.querySelectorAll('.admit-subjects button')].some((b) => b.innerText.trim() === n && b.getAttribute('aria-pressed') === 'true'), { timeout: 15000 }, name);
    } else if (await pressed(page, name) !== 'true') {
      await page.$$eval('.admit-subjects button', (bs, n) => bs.find((b) => b.innerText.trim() === n).click(), name);
      await page.waitForFunction((n) => [...document.querySelectorAll('.admit-subjects button')].some((b) => b.innerText.trim() === n && b.getAttribute('aria-pressed') === 'true'), {}, name);
    }
  }
  // A marks row was offered for each tuition subject.
  await page.waitForSelector('#ms1');
  assert.deepEqual(await page.$$eval('.admit-mark input[id^="ms"]', (i) => i.map((x) => x.value)), ['Maths', 'Arabic']);
  await page.type('#mm0', '58'); await page.type('#mx0', '100');
  await page.type('#mm1', '41'); await page.type('#mx1', '50');
  await page.type('#art', 'Term 1 2026');
  await page.type('#arp', '72.5');
  const input = await page.$('#arf');
  await input.uploadFile(pdf);
  console.log('uploading report');
  await page.waitForFunction(() => /Attached: term1-report\.pdf/.test(document.body.innerText), { timeout: 15000 });
  await page.type('#agoal', 'Score above 85% in Maths in the final exam.');

  for (const [name, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await new Promise((r) => setTimeout(r, 400));
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `admission overflow at ${name}`);
    await screenshot(page, `admission-${name}`);
  }
  await page.setViewport(viewports.desktop);

  await page.$$eval('button', (bs) => bs.find((b) => b.innerText.trim() === 'Admit student').click());
  await page.waitForFunction(() => /temporary password/i.test(document.body.innerText), { timeout: 20000 });
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.$$eval('button', (bs) => bs.find((b) => b.innerText.trim() === 'Done').click())]);
  assert.match(page.url(), /\/students\/[0-9a-f-]{36}$/);

  await page.waitForFunction(() => /Overall 72\.5%/.test(document.body.innerText), { timeout: 15000 });
  const about = await page.$eval('.student-about', (s) => s.innerText.replace(/\s+/g, ' '));
  console.log('about:', about);
  for (const expected of [/With the centre.*day|With the centre.*since/, /Maths/, /Arabic/, /\+971 50 222 0101/, /Fictional Parent/]) assert.match(about, expected);
  const results = await page.$eval('[aria-labelledby="sr-h"]', (s) => s.innerText.replace(/\s+/g, ' '));
  console.log('school results:', results);
  assert.match(results, /Term 1 2026/);
  assert.match(results, /Maths 58\/100/);
  assert.match(results, /Arabic 41\/50/);
  assert.match(results, /Open report/);
  // The goal from admission appears in the Goals panel (it loads on its own).
  await page.waitForFunction(() => /Score above 85% in Maths/.test(document.body.innerText), { timeout: 15000 });
  for (const [name, viewport] of Object.entries({ desktop: viewports.desktop, mobile: viewports.mobile })) {
    await page.setViewport(viewport);
    await new Promise((r) => setTimeout(r, 400));
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `profile overflow at ${name}`);
    await screenshot(page, `admitted-profile-${name}`);
  }

  const unexpected = failedRequests.filter((r) => !r.includes('/files/download'));
  assert.deepEqual(consoleErrors, [], 'console errors');
  assert.deepEqual(unexpected, [], 'failed requests (photo downloads excluded: known missing local files)');
  console.log('Admission check passed at 375, 768 and 1440px.');
} catch (e) {
  if (page) console.log('failed at', page.url(), '→', await screenshot(page, 'admission-failure'));
  throw e;
} finally {
  await browser.close();
}
