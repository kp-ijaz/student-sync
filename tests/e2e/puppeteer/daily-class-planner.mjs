// Daily journeys and the one-page "Add classes" form (start / for / days / time chips, per-day subjects in the week list,
// clashes shown on their day's row with quick fixes, student time zone, the phone Create bar).
// Read-only by default; CREATE=1 books a fictional 2-week plan for Hiro and then checks that re-booking it shows clashes.
import assert from 'node:assert/strict';
import { launch, newPage, signIn, users, viewports, screenshot, BASE_URL } from './browser.mjs';

const create = process.env.CREATE === '1';
const summaryText = (page) => page.$eval('.planner-summary', (el) => el.innerText.replace(/\s+/g, ' '));
const tile = (page, group, text) => page.$$eval(`${group} button`, (bs, t) => {
  const b = bs.find((x) => x.innerText.split('\n')[0].trim() === t);
  if (!b) throw new Error(`No choice "${t}" in ${bs.map((x) => x.innerText.split('\n')[0]).join(', ')}`);
  b.click();
}, text);
const setValue = (page, selector, value) => page.$eval(selector, (input, v) => {
  input.value = v;
  input.dispatchEvent(new Event('change', { bubbles: true }));
}, value);
const waitSummary = (page, re, timeout = 20000) => page.waitForFunction((src) => new RegExp(src).test(document.querySelector('.planner-summary').innerText.replace(/\s+/g, ' ')), { timeout }, re.source);
const setDays = (page, wanted) => page.$$eval('.day-picks .day-pick', (bs, w) => bs.forEach((b) => {
  if ((b.getAttribute('aria-pressed') === 'true') !== w.includes(b.querySelector('[aria-hidden]').textContent)) b.click();
}), wanted);
const shots = async (page, name) => {
  for (const [vp, viewport] of Object.entries(viewports)) {
    await page.setViewport(viewport);
    await new Promise((r) => setTimeout(r, 500)); // let the drawer/sidebar transition settle
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `${name} overflow at ${vp}`);
    await screenshot(page, `${name}-${vp}`);
  }
  await page.setViewport(viewports.desktop);
};
async function pickStudent(page, name) {
  await page.goto(`${BASE_URL}/sessions/new`, { waitUntil: 'networkidle0' });
  await page.type('#stsearch', name);
  await page.waitForFunction(() => document.querySelectorAll('.planner-students input[type=radio]').length === 1);
  await page.click('.planner-students input[type=radio]');
  await page.waitForSelector('.easy-picked');
}
async function pickDate(page, iso) {
  await tile(page, '[aria-labelledby="start-l"]', 'Pick a date');
  await page.waitForSelector('#date');
  await setValue(page, '#date', iso);
}
async function chooseSubject(page, prefix, name) {
  const sel = `#${prefix}subject`;
  await page.waitForFunction((s) => { const el = document.querySelector(s); return el && !el.disabled && el.options.length > 1; }, {}, sel);
  let value = await page.$eval(sel, (s, n) => [...s.options].find((o) => o.textContent.trim() === n)?.value, name);
  if (!value) {
    await page.select(sel, 'other');
    await page.waitForFunction((s) => document.querySelectorAll(`${s} optgroup`).length > 0, {}, sel);
    value = await page.$eval(sel, (s, n) => [...s.options].find((o) => o.textContent.trim() === n)?.value, name);
  }
  assert.ok(value, `${name} available`);
  const oldTeacher = await page.$(`#${prefix}teacher`);
  await page.select(sel, value);
  if (oldTeacher) {
    await page.waitForFunction((el) => !el.isConnected, {}, oldTeacher); // teacher list rebuilt for the new subject
    await page.waitForFunction((s) => !document.querySelector(s).disabled, {}, `#${prefix}teacher`);
    const t = await page.$eval(`#${prefix}teacher`, (s) => s.value || [...s.options].find((o) => o.value && o.value !== 'other')?.value);
    await page.select(`#${prefix}teacher`, t);
  }
}

const browser = await launch();
let page;
try {
  for (const role of (process.env.ROLES ?? 'admin,teacher,student').split(',')) {
    const opened = await newPage(browser);
    page = opened.page;
    await signIn(page, users[role]);
    // Every home uses the shared pattern: five shortcut pills, a metric strip and the class agenda.
    const [links, count] = ['.quick-links a', 5];
    await page.waitForSelector(links);
    assert.equal(await page.$$eval(links, (l) => l.length), count);
    if (role === 'admin') assert.equal(await page.$$eval('.dash-kpis .kpi', (m) => m.length), 6); // six number tiles (2026-11-24)
    await shots(page, `daily-${role}`);
    if (role === 'student') { await page.close(); continue; }

    // ---- Basic plan: 2 days for 2 weeks, then "Just once" and "Until a date" ----
    await page.goto(`${BASE_URL}/sessions/new`, { waitUntil: 'networkidle0' });
    assert.match(await summaryText(page), /Choose a student/);
    for (const gone of ['.planner-steps', '#title', '#tz', '#freq', '#interval', '.easy-days', '.choice-grid', '.easy-issue']) assert.equal(await page.$(gone), null, `${gone} should be gone`);
    await pickStudent(page, 'Hiro');
    // Teachers see and book only the subjects on their profile (Amira teaches Mathematics); the admin can pick any.
    await chooseSubject(page, '', role === 'teacher' ? 'Mathematics' : 'Physics');
    const day = new Date(Date.now() + 864e5 * (300 + Math.floor(Math.random() * 2000))).toISOString().slice(0, 10);
    const slot = `0${1 + Math.floor(Math.random() * 4)}:${String(5 * Math.floor(Math.random() * 12)).padStart(2, '0')}`;
    await pickDate(page, day);
    await setValue(page, '#start', slot);
    await tile(page, '[aria-labelledby="dur-l"]', '45 min');
    await tile(page, '[aria-labelledby="span-l"]', '2 weeks');
    await page.waitForFunction(() => document.querySelectorAll('.day-picks .day-pick[aria-pressed=true]').length >= 1);
    await setDays(page, [await page.$eval('.day-picks .day-pick[aria-pressed=true] [aria-hidden]', (s) => s.textContent)]);
    await page.$$eval('.day-picks .day-pick', (bs) => bs.find((b) => b.getAttribute('aria-pressed') !== 'true').click());
    await waitSummary(page, /4 classes ready/);
    assert.match(await page.$eval('.planner-create', (b) => b.innerText), /Create 4 classes/);
    await tile(page, '[aria-labelledby="span-l"]', 'Just once');
    await waitSummary(page, /1 class ready/);
    assert.equal(await page.$('.day-picks'), null, 'days are hidden for a single class');
    await tile(page, '[aria-labelledby="span-l"]', 'Until a date');
    await page.waitForSelector('#until');
    assert.ok(await page.$('#date'), 'From and To dates are shown together');
    await tile(page, '[aria-labelledby="span-l"]', '2 weeks');
    await waitSummary(page, /4 classes ready/);
    assert.match(await page.$eval('.bk-range', (e) => e.innerText.replace(/\s+/g, ' ')), /From .* To .* 2 weeks · 4 classes/);
    assert.equal(await page.$$eval('.wk-row .wk-state.good', (s) => s.length), 2, 'each day row shows it is free');
    await page.setViewport(viewports.mobile);
    await page.waitForFunction(() => getComputedStyle(document.querySelector('.bk-bar')).display === 'flex');
    assert.match(await page.$eval('.bk-bar', (e) => e.innerText.replace(/\s+/g, ' ')), /All dates free .*Create 4 classes/);
    await page.setViewport(viewports.desktop);
    await shots(page, `addclasses-${role}-filled`);

    if (role === 'admin') {
      // ---- Hafiz-style week: different subject and teacher per day ----
      const week = { Mon: 'Mathematics', Tue: 'Science', Wed: 'Mathematics', Fri: 'Social Science', Sat: 'English' };
      await setDays(page, Object.keys(week));
      await tile(page, '[aria-label="Subjects"]', 'Different each day');
      await page.waitForFunction(() => document.querySelectorAll('.wk-row').length === 5);
      for (const [d, name] of Object.entries(week)) await chooseSubject(page, `${d.toLowerCase()}-`, name);
      await waitSummary(page, /10 classes ready/);
      console.log(`admin weekly plan summary: ${await summaryText(page)}`);
      assert.equal(await page.$$eval('.easy-plan-line', (l) => l.length), 4, 'Mon+Wed Maths share one line');
      await shots(page, 'addclasses-admin-weekly');

      if (create) {
        await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.click('.planner-create')]);
        assert.match(page.url(), /\/sessions$/);
        console.log(`admin created Hiro's weekly plan from ${day} at ${slot}`);

        // ---- Booking over the same week again: each clash shows on its day, in one sentence, with quick fixes ----
        await pickStudent(page, 'Hiro');
        await chooseSubject(page, '', 'Physics');
        await pickDate(page, day);
        await setValue(page, '#start', slot);
        await tile(page, '[aria-labelledby="dur-l"]', '45 min');
        await tile(page, '[aria-labelledby="span-l"]', '2 weeks');
        await setDays(page, ['Mon', 'Tue']);
        await page.waitForSelector('.wk-issue', { timeout: 20000 });
        const issue = await page.$eval('.wk-issue', (e) => e.innerText.replace(/\s+/g, ' '));
        console.log('clash card:', issue);
        assert.match(issue, /already has a class then|is busy then/);
        await shots(page, 'addclasses-admin-clash');
        const fixes = await page.$$eval('.wk-issue .wk-fixes button', (bs) => bs.map((b) => b.innerText.trim()));
        console.log('quick fixes:', fixes);
        assert.ok(fixes.length > 0, 'at least one quick fix is offered');
        const before = await page.$$eval('.wk-issue', (c) => c.length);
        await page.$$eval('.wk-issue .wk-fixes button', (bs) => (bs.find((b) => b.innerText.startsWith('Move')) ?? bs[0]).click());
        await page.waitForFunction((n) => !document.querySelector('.easy-status.checking') && document.querySelectorAll('.wk-issue').length < n, { timeout: 20000 }, before);
        console.log('after quick fix:', await summaryText(page));
        await screenshot(page, 'addclasses-admin-clash-fixed-desktop');
      }

      // ---- Anzah-style: student in India, admin enters "8 pm India time" ----
      await pickStudent(page, 'Dev Patel');
      await page.waitForSelector('[aria-label="Time zone of this time"]');
      await pickDate(page, new Date(Date.now() + 864e5 * (520 + Math.floor(Math.random() * 100))).toISOString().slice(0, 10));
      await setValue(page, '#start', '20:00');
      await page.$$eval('[aria-label="Time zone of this time"] button', (bs) => bs[1].click());
      await page.waitForFunction(() => { const s = document.querySelector('#subject'); return !s.disabled && s.options.length > 1; });
      const sub = await page.$eval('#subject', (s) => [...s.options].find((o) => o.value && o.value !== 'other')?.value);
      const oldTeacher = await page.$('#teacher');
      await page.select('#subject', sub);
      await page.waitForFunction((el) => !el.isConnected, {}, oldTeacher);
      await page.waitForFunction(() => !document.querySelector('#teacher').disabled);
      await page.select('#teacher', await page.$eval('#teacher', (s) => s.value || [...s.options].find((o) => o.value && o.value !== 'other')?.value));
      await waitSummary(page, /Dev sees 8:00 PM India time; you see 6:30 PM UAE time/);
      await screenshot(page, 'addclasses-admin-india-time-desktop');
    }

    const unexpected = opened.failedRequests.filter((r) => !r.includes('/files/download'));
    assert.deepEqual(opened.consoleErrors, [], `${role} console errors`);
    assert.deepEqual(unexpected, [], `${role} failed requests (photo downloads excluded: known missing local files)`);
    await page.close();
  }
  console.log('Daily journeys and Add classes checks passed at 375, 768 and 1440px.');
} catch (e) {
  if (page && !page.isClosed()) console.log('failed at', page.url(), '→', await screenshot(page, 'addclasses-failure'));
  throw e;
} finally {
  await browser.close();
}
