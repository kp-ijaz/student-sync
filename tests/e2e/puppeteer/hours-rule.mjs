// Classes must fit the student's hours (owner decision 2026-11-17), in a real browser on local fictional data:
// a new student without hours cannot be booked ("No hours added yet" + Add hours), then with 1.5 h added four 45-minute
// classes do not fit, "Book only the first 2 classes" trims the plan and Create books exactly those.
import assert from 'node:assert/strict';
import { launch, newPage, signIn, users, viewports, demoPassword, BASE_URL, SCREENSHOT_DIR } from './browser.mjs';

const API = process.env.API_BASE_URL ?? 'http://localhost:5080/api/v1';
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const summary = (page) => page.$eval('.planner-summary', (el) => el.innerText.replace(/\s+/g, ' '));
const waitSummary = (page, re) => page.waitForFunction((src) => new RegExp(src).test(document.querySelector('.planner-summary').innerText.replace(/\s+/g, ' ')), { timeout: 20000 }, re.source);
const tile = (page, group, text) => page.$$eval(`${group} button`, (bs, t) => bs.find((x) => x.innerText.split('\n')[0].trim() === t).click(), text);
const setValue = (page, selector, value) => page.$eval(selector, (input, v) => { input.value = v; input.dispatchEvent(new Event('change', { bubbles: true })); }, value);

// Fictional student, created through the API (admins create students; this one starts with no hours).
const token = (await (await fetch(`${API}/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email: users.admin, password: demoPassword(), deviceName: 'e2e' }) })).json()).accessToken;
const h = { 'content-type': 'application/json', authorization: `Bearer ${token}` };
const tag = Math.random().toString(36).slice(2, 8);
const name = `Hoursrule ${tag} (Demo Student)`;
const student = await (await fetch(`${API}/students`, { method: 'POST', headers: h, body: JSON.stringify({ fullName: name, email: `hoursrule-${tag}@horizon.example.test`, studentCode: `HR-${tag}`, timeZoneId: 'Asia/Dubai' }) })).json();
assert.ok(student.id, 'student created');

const browser = await launch();
try {
  const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.desktop);
  await signIn(page, users.admin);
  await page.goto(`${BASE_URL}/sessions/new`, { waitUntil: 'networkidle0' });
  await page.type('#stsearch', `Hoursrule ${tag}`);
  await page.waitForFunction(() => document.querySelectorAll('.planner-students input[type=radio]').length === 1);
  await page.click('.planner-students input[type=radio]');
  await page.waitForSelector('.easy-picked');
  const sel = '#subject';
  await page.waitForFunction((s) => { const el = document.querySelector(s); return el && !el.disabled && el.options.length > 1; }, {}, sel);
  await page.select(sel, 'other');
  await page.waitForFunction((s) => document.querySelectorAll(`${s} optgroup`).length > 0, {}, sel);
  const physics = await page.$eval(sel, (s) => [...s.options].find((o) => o.textContent.trim() === 'Physics')?.value);
  await page.select(sel, physics);
  await page.waitForFunction(() => { const t = document.querySelector('#teacher'); return t && !t.disabled && t.options.length > 1; });
  const teacher = await page.$eval('#teacher', (s) => s.value || [...s.options].find((o) => o.value && o.value !== 'other')?.value);
  await page.select('#teacher', teacher);
  const day = new Date(Date.now() + 864e5 * (900 + Math.floor(Math.random() * 900))).toISOString().slice(0, 10);
  await tile(page, '[aria-labelledby="start-l"]', 'Pick a date');
  await page.waitForSelector('#date');
  await setValue(page, '#date', day);
  await setValue(page, '#start', '05:10');
  await tile(page, '[aria-labelledby="dur-l"]', '45 min');
  await tile(page, '[aria-labelledby="span-l"]', '2 weeks');
  await page.waitForFunction(() => document.querySelectorAll('.day-picks .day-pick[aria-pressed=true]').length >= 1);
  await page.$$eval('.day-picks .day-pick', (bs) => bs.find((b) => b.getAttribute('aria-pressed') !== 'true').click());

  // 1. No hours at all.
  await waitSummary(page, /No hours added yet/);
  assert.equal(await page.$eval('.planner-create', (b) => b.disabled), true, 'Create is disabled without hours');
  assert.ok(await page.$(`.hours-fix a[href="/students/${student.id}#fees"]`), 'Add hours goes to the Fees section');
  console.log('no hours:', (await summary(page)).match(/No hours added yet.*?classes\./)?.[0] ?? (await summary(page)).slice(0, 120));
  for (const [label, vp] of [['mobile', viewports.mobile], ['desktop', viewports.desktop]]) {
    await page.setViewport(vp); await wait(400);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `overflow at ${label}`);
    await page.screenshot({ path: `${SCREENSHOT_DIR}/hours-none-${label}.png`, fullPage: label === 'desktop' });
  }
  await page.setViewport(viewports.desktop);

  // 2. 1.5 h added: 4 × 45 min = 3 h needed, 2 fit.
  await fetch(`${API}/students/${student.id}/packages`, { method: 'POST', headers: h, body: JSON.stringify({ hours: 1.5, price: 750 }) });
  await setValue(page, '#start', '05:15'); // re-check
  await waitSummary(page, /Only 1\.5 h free/);
  console.log('short:', (await summary(page)).match(/Only 1\.5 h free.*?\./)?.[0]);
  await page.screenshot({ path: `${SCREENSHOT_DIR}/hours-short-mobile.png` });
  await page.$$eval('.hours-fix button', (bs) => bs.find((b) => /Book only the first 2 classes/.test(b.innerText)).click());
  await waitSummary(page, /2 classes ready/);
  assert.match(await summary(page), /Uses 1\.5 h of Hoursrule's 1\.5 h free hours/);
  assert.equal(await page.$eval('.planner-create', (b) => b.disabled), false);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('.planner-create')]);
  const fees = await (await fetch(`${API}/students/${student.id}/fees`, { headers: h })).json();
  console.log('after booking: hours left', fees.hoursLeft);
  assert.deepEqual(consoleErrors, [], 'console errors');
  assert.deepEqual(failedRequests.filter((r) => !r.includes('/files/download')), [], 'failed requests');
  console.log('Hours rule check passed.');
} finally {
  await browser.close();
}
