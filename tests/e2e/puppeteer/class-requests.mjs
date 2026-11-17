// Owner request 2026-11-17, in a real browser on local fictional data:
//  1. Password boxes show/hide with an eye button (change password, Settings ▸ Integrations); 6-character passwords work.
//  2. Student home ▸ Your teachers: subjects and next class; a teacher opens their upcoming classes; Reschedule sends a request.
//  3. The teacher sees it under Class change requests, picks a free time and accepts; the class moves.
import assert from 'node:assert/strict';
import { launch, newPage, signIn, users, viewports, demoPassword, BASE_URL, SCREENSHOT_DIR } from './browser.mjs';

const API = process.env.API_BASE_URL ?? 'http://localhost:5080/api/v1';
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const noOverflow = (page) => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
const toast = (page, re) => page.waitForFunction((src) => new RegExp(src).test(document.body.innerText), { timeout: 20000 }, re.source);
const login = async (email, password) => (await (await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password, deviceName: 'e2e' }) })).json()).accessToken;

const browser = await launch();
try {
  // ---------- 1. Passwords ----------
  const adminToken = await login(users.admin, demoPassword());
  const h = { 'content-type': 'application/json', authorization: `Bearer ${adminToken}` };
  const tag = Math.random().toString(36).slice(2, 8);
  const email = `pwtoggle-${tag}@horizon.example.test`;
  const created = await (await fetch(`${API}/students`, { method: 'POST', headers: h, body: JSON.stringify({ fullName: `Pwtoggle ${tag} (Demo Student)`, email, studentCode: `PT-${tag}`, timeZoneId: 'Asia/Dubai' }) })).json();
  {
    const { page, consoleErrors } = await newPage(browser, viewports.mobile);
    await page.goto(`${BASE_URL}/login`, { waitUntil: 'networkidle0' });
    await page.type('#email', email);
    await page.type('#password', created.temporaryPassword);
    await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('button[type=submit]')]);
    await page.waitForSelector('#new');
    assert.equal(await page.$$eval('.pw-field .pw-toggle', (b) => b.length), 3, 'current, new and confirm each have an eye button');
    await page.type('#cur', created.temporaryPassword);
    await page.type('#new', 'tulip7');
    await page.type('#confirm', 'tulip7');
    assert.equal(await page.$eval('#new', (i) => i.type), 'password');
    await page.click('#new + .pw-toggle');
    await page.waitForFunction(() => document.querySelector('#new').type === 'text');
    assert.equal(await page.$eval('#new + .pw-toggle', (b) => [b.getAttribute('aria-pressed'), b.getAttribute('aria-label')].join('|')), 'true|Hide password');
    await page.screenshot({ path: `${SCREENSHOT_DIR}/password-toggle-mobile.png` });
    await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0', timeout: 30000 }), page.$$eval('button', (bs) => bs.find((b) => b.innerText.trim() === 'Update password').click())]);
    assert.ok(await login(email, 'tulip7'), 'the 6-character password signs in');
    console.log('passwords: eye buttons work, 6-character password accepted');
    assert.deepEqual(consoleErrors, [], 'console errors (password)');
    await page.close();
  }
  {
    const { page } = await newPage(browser, viewports.desktop);
    await signIn(page, users.admin);
    await page.goto(`${BASE_URL}/admin/settings?tab=integrations`, { waitUntil: 'networkidle0' });
    await page.waitForSelector('#epw');
    console.log('integrations secret boxes with an eye:', await page.$$eval('.pw-field .pw-toggle', (b) => b.length));
    assert.ok(await page.$$eval('.pw-field .pw-toggle', (b) => b.length) >= 4);
    await page.close();
  }

  // ---------- 2. Student: your teachers + reschedule request ----------
  let className;
  {
    const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.mobile);
    await signIn(page, users.student);
    await page.goto(`${BASE_URL}/student`, { waitUntil: 'networkidle0' });
    await page.waitForSelector('.my-teachers .people-list li');
    const rows = await page.$$eval('.my-teachers .people-list li', (li) => li.map((x) => x.innerText.replace(/\s+/g, ' ').trim()));
    console.log('your teachers:', rows.join(' | '));
    assert.ok(rows.some((r) => /Next:/.test(r)), 'a teacher shows the next class');
    assert.equal(await noOverflow(page), true, 'student home overflow at 375');
    await page.screenshot({ path: `${SCREENSHOT_DIR}/my-teachers-mobile.png` });
    // Open Amira (the demo teacher signed in below).
    await page.$$eval('.my-teachers .people-list li button', (b) => b.find((x) => /Amira/.test(x.innerText)).click());
    await page.waitForSelector('.mt-classes .mt-class');
    await page.screenshot({ path: `${SCREENSHOT_DIR}/my-teacher-sheet-mobile.png` });
    const free = await page.$$eval('.mt-class', (cs) => cs.findIndex((c) => c.querySelector('.mt-actions')));
    assert.ok(free >= 0, 'a class can be asked to move');
    className = await page.evaluate((i) => document.querySelectorAll('.mt-class')[i].querySelector('a strong').innerText, free);
    await page.evaluate((i) => [...document.querySelectorAll('.mt-class')[i].querySelectorAll('.mt-actions button')].find((b) => b.innerText.trim() === 'Reschedule').click(), free);
    await page.waitForSelector('#mt-reason');
    await page.$eval('#mt-time', (el) => { el.value = '03:25'; el.dispatchEvent(new Event('change', { bubbles: true })); });
    await page.type('#mt-reason', 'School exam that day (demo)');
    await page.screenshot({ path: `${SCREENSHOT_DIR}/class-request-form-mobile.png` });
    await page.$$eval('.modal button', (bs) => bs.find((b) => b.innerText.includes('Send request')).click());
    await toast(page, /Request sent to your teacher/);
    await page.waitForSelector('.mt-request');
    console.log('student request:', await page.$eval('.mt-request', (e) => e.innerText.replace(/\s+/g, ' ')));
    assert.deepEqual(consoleErrors, [], 'console errors (student)');
    assert.deepEqual(failedRequests.filter((r) => !r.includes('/files/download')), [], 'failed requests (student)');
    await page.close();
  }

  // ---------- 3. Teacher: answer it ----------
  {
    const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.desktop);
    await signIn(page, users.teacher);
    await page.goto(`${BASE_URL}/teacher`, { waitUntil: 'networkidle0' });
    await wait(600);
    console.log('teacher home needs:', (await page.$eval('.home-needs', (e) => e.innerText.replace(/\s+/g, ' ')).catch(() => '')).slice(0, 160));
    await page.goto(`${BASE_URL}/class-requests`, { waitUntil: 'networkidle0' });
    await page.waitForSelector('.cr-card');
    const card = await page.$$eval('.cr-card', (cs) => cs.findIndex((c) => /School exam that day \(demo\)/.test(c.innerText) && c.querySelector('.cr-answer')));
    assert.ok(card >= 0, 'the request is listed');
    await page.screenshot({ path: `${SCREENSHOT_DIR}/class-requests-desktop.png`, fullPage: true });
    await page.setViewport(viewports.mobile); await wait(400);
    assert.equal(await noOverflow(page), true, 'requests page overflow at 375');
    await page.screenshot({ path: `${SCREENSHOT_DIR}/class-requests-mobile.png` });
    await page.setViewport(viewports.desktop); await wait(300);
    await page.evaluate((i) => [...document.querySelectorAll('.cr-card')[i].querySelectorAll('button')].find((b) => b.innerText.includes('Accept and move')).click(), card);
    await toast(page, /Class moved|already has a session|clash/i);
    const outcome = await page.evaluate(() => [...document.querySelectorAll('.toast, [role=status], [role=alert]')].map((t) => t.innerText).join(' | '));
    console.log('teacher answer:', outcome.replace(/\s+/g, ' ').slice(0, 200));
    assert.match(outcome, /Class moved/);
    assert.deepEqual(consoleErrors, [], 'console errors (teacher)');
    assert.deepEqual(failedRequests.filter((r) => !r.includes('/files/download')), [], 'failed requests (teacher)');
    await page.close();
  }
  console.log(`Class requests check passed (moved: ${className}).`);
} finally {
  await browser.close();
}
