// "Make it very easy" checks (owner request 2026-11-16), on local fictional demo data only:
//  1. Admin → Academics → "Add a class": choose class + syllabus, usual subjects appear ticked, one save.
//  2. Teacher → Home and Tasks: mark a finished class's attendance in place (Present / Late / Absent).
// Screenshots at 375 and 1440 px; fails on page overflow, console errors or failed requests.
import assert from 'node:assert/strict';
import { launch, newPage, signIn, users, viewports, screenshot, BASE_URL, SCREENSHOT_DIR } from './browser.mjs';

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const noOverflow = (page) => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
const browser = await launch();
try {
  // ---------- 1. Add a class ----------
  {
    const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.mobile);
    await signIn(page, users.admin);
    await page.goto(`${BASE_URL}/admin/academics`, { waitUntil: 'networkidle0' });
    await page.$$eval('button', (bs) => bs.find((b) => b.innerText.includes('Add a class')).click());
    await page.waitForSelector('#qcg');
    const grade = await page.$eval('#qcg', (s) => [...s.options].find((o) => /\b(7|VII)\b/.test(o.textContent))?.value ?? [...s.options].find((o) => o.value)?.value);
    const syllabus = await page.$eval('#qcc', (s) => [...s.options].find((o) => /kerala/i.test(o.textContent))?.value ?? [...s.options].find((o) => o.value)?.value);
    assert.ok(grade && syllabus, 'the local catalogue has a class and a syllabus');
    await page.select('#qcg', grade);
    await page.select('#qcc', syllabus);
    await page.waitForFunction(() => document.querySelector('.modal fieldset legend')?.innerText.startsWith('Subjects in'), { timeout: 15000 });
    const chips = await page.$$eval('.modal .pick', (bs) => bs.map((b) => `${b.innerText.trim()}=${b.getAttribute('aria-pressed')}${b.disabled ? '(exists)' : ''}`));
    console.log('suggested:', chips.join(', '));
    assert.ok(chips.length > 0, 'subjects are suggested');
    // A custom subject is added and ticked.
    const custom = `Spoken English ${Date.now().toString(36)} (demo)`;
    await page.type('#qcn', custom);
    await page.keyboard.press('Enter');
    await page.waitForFunction((n) => [...document.querySelectorAll('.modal .pick')].some((b) => b.innerText.trim() === n && b.getAttribute('aria-pressed') === 'true'), {}, custom);
    assert.equal(await noOverflow(page), true, 'add-a-class overflow at 375');
    await page.screenshot({ path: `${SCREENSHOT_DIR}/add-class-mobile.png` }); // viewport: the dialog, not the long page behind it
    await page.setViewport(viewports.desktop); await wait(300);
    await page.screenshot({ path: `${SCREENSHOT_DIR}/add-class-desktop.png` });
    const save = await page.$$eval('.modal .form-actions .btn-primary', (bs) => bs[0].innerText.trim());
    console.log('save button:', save);
    await page.$$eval('.modal .form-actions .btn-primary', (bs) => bs[0].click());
    await page.waitForFunction(() => /saved with its subjects/.test(document.body.innerText), { timeout: 15000 });
    assert.deepEqual(consoleErrors, [], 'console errors (admin)');
    assert.deepEqual(failedRequests.filter((r) => !r.includes('/files/download')), [], 'failed requests (admin)');
    await page.close();
  }

  // ---------- 2. Quick attendance ----------
  {
    const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.mobile);
    await signIn(page, users.teacher);
    await page.goto(`${BASE_URL}/teacher`, { waitUntil: 'networkidle0' });
    await wait(800);
    const onHome = await page.$$eval('.qa-card', (cs) => cs.length);
    console.log('attendance cards on Home:', onHome);
    assert.equal(await noOverflow(page), true, 'teacher home overflow at 375');
    await screenshot(page, 'quick-attendance-home-mobile');

    await page.goto(`${BASE_URL}/teacher/tasks`, { waitUntil: 'networkidle0' });
    await wait(800);
    const cards = await page.$$eval('.qa-card', (cs) => cs.map((c) => ({ title: c.querySelector('.qa-title')?.innerText, rows: c.querySelectorAll('.qa-row').length })));
    console.log('attendance cards on Tasks:', JSON.stringify(cards.slice(0, 5)));
    assert.equal(await noOverflow(page), true, 'teacher tasks overflow at 375');
    await screenshot(page, 'quick-attendance-tasks-mobile');
    const small = await page.$$eval('.qa-card button, .qa-card a', (els) => els.filter((e) => { const r = e.getBoundingClientRect(); return r.width && (r.height < 43.5 || r.width < 43.5); }).map((e) => e.innerText.trim()));
    assert.deepEqual(small, [], 'quick attendance touch targets are at least 44 px on phones');

    const one = cards.findIndex((c) => c.rows === 1);
    if (one >= 0) {
      // One student: a single tap saves.
      await page.evaluate((i) => document.querySelectorAll('.qa-card')[i].querySelector('.qa-mark.present').click(), one);
      await page.waitForFunction(() => /Attendance saved for/.test(document.body.innerText), { timeout: 15000 });
      console.log('one-tap save ok for', cards[one].title);
    } else if (cards.length) {
      await page.evaluate(() => [...document.querySelector('.qa-card').querySelectorAll('button')].find((b) => b.innerText.trim() === 'All present').click());
      await page.waitForFunction(() => { const c = document.querySelector('.qa-card'); return [...c.querySelectorAll('.qa-mark.present')].every((b) => b.getAttribute('aria-pressed') === 'true') && ![...c.querySelectorAll('button')].find((b) => b.innerText.trim() === 'Save attendance').disabled; });
      await page.evaluate(() => [...document.querySelector('.qa-card').querySelectorAll('button')].find((b) => b.innerText.trim() === 'Save attendance').click());
      await page.waitForFunction(() => /Attendance saved for/.test(document.body.innerText), { timeout: 15000 });
      console.log('all-present save ok for', cards[0].title);
    } else console.log('no finished classes waiting for attendance in the local data — nothing to mark');
    await wait(500);
    assert.equal(await page.$$eval('.qa-card', (cs) => cs.length), Math.max(0, cards.length - 1), 'the marked class leaves the list');

    await page.setViewport(viewports.desktop); await wait(300);
    await screenshot(page, 'quick-attendance-tasks-desktop');
    assert.deepEqual(consoleErrors, [], 'console errors (teacher)');
    assert.deepEqual(failedRequests.filter((r) => !r.includes('/files/download')), [], 'failed requests (teacher)');
    await page.close();
  }
  console.log('Easy flows check passed.');
} finally {
  await browser.close();
}
