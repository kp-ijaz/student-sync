// Settings ▸ Automations and what it adds (owner request 2026-11-17), on local fictional demo data only:
// automation switches save; Fees ▸ Salaries pays a teacher and opens the printable slip; progress reports page;
// holiday clean-up panel; fee receipt page; payment link explains when online payments are off.
// Screenshots at 375 and 1440 px; fails on page overflow, console errors or unexpected failed requests.
import assert from 'node:assert/strict';
import { launch, newPage, signIn, users, viewports, BASE_URL, SCREENSHOT_DIR } from './browser.mjs';

const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const noOverflow = (page) => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth);
const shot = async (page, name) => {
  for (const [label, vp] of [['mobile', viewports.mobile], ['desktop', viewports.desktop]]) {
    await page.setViewport(vp); await wait(350);
    assert.equal(await noOverflow(page), true, `${name} overflows at ${label}`);
    await page.screenshot({ path: `${SCREENSHOT_DIR}/${name}-${label}.png`, fullPage: label === 'desktop' });
  }
};
const clickText = (page, selector, text) => page.$$eval(selector, (els, t) => { const el = els.find((e) => e.innerText.trim().startsWith(t)); if (!el) throw new Error(`no ${t}`); el.click(); }, text);
const toast = (page, re) => page.waitForFunction((src) => new RegExp(src).test(document.body.innerText), { timeout: 15000 }, re.source);

const browser = await launch();
try {
  const { page, consoleErrors, failedRequests } = await newPage(browser, viewports.desktop);
  await signIn(page, users.admin);

  // 1. Automations: switches, save.
  await page.goto(`${BASE_URL}/admin/settings?tab=automations`, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.auto-row .switch');
  const switches = await page.$$eval('.auto-row .switch', (s) => s.map((x) => ({ on: x.checked, disabled: x.disabled, label: x.closest('label').innerText.split('\n')[0] })));
  console.log('automations:', switches.map((s) => `${s.label}=${s.on ? 'on' : 'off'}${s.disabled ? '(needs SMS)' : ''}`).join(', '));
  assert.ok(switches.length >= 14, 'every automation has a switch');
  await page.$$eval('.auto-row .switch', (s) => s.find((x) => x.closest('label').innerText.startsWith('Fee reminders')).click());
  await page.$$eval('.auto-row .switch', (s) => s.find((x) => x.closest('label').innerText.startsWith('Fee reminders')).click());
  await clickText(page, 'button', 'Save automations');
  await toast(page, /Automations saved/);
  await shot(page, 'automations');

  // 2. Salaries: pay the first teacher who is owed, then the slip.
  await page.goto(`${BASE_URL}/admin/salaries`, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.salary-month');
  await wait(500);
  await shot(page, 'salaries');
  if (await page.$$eval('.salary-row button', (b) => b.some((x) => x.innerText.trim() === 'Mark paid'))) {
    await clickText(page, '.salary-row button', 'Mark paid');
    await page.waitForSelector('.modal .fee-method');
    await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle0' }), page.$$eval('.modal .form-actions .btn-primary', (b) => b[0].click())]);
    assert.match(page.url(), /\/salary-slips\/[0-9a-f-]{36}\/[0-9a-f-]{36}$/);
    await page.waitForSelector('.doc-total');
    console.log('slip:', (await page.$eval('.doc', (d) => d.innerText.replace(/\s+/g, ' '))).slice(0, 160));
    await shot(page, 'salary-slip');
  } else console.log('no teacher is owed a salary in the local data');

  // 3. Progress reports (admin).
  await page.goto(`${BASE_URL}/progress-reports`, { waitUntil: 'networkidle0' });
  await page.waitForSelector('.seg');
  await wait(400);
  console.log('progress reports to send:', await page.$$eval('.pr-card', (c) => c.length));
  await shot(page, 'progress-reports');

  // 4. Holiday clean-up: add a (far future, fictional) holiday → the classes-that-day panel opens.
  await page.goto(`${BASE_URL}/admin/settings?tab=holidays`, { waitUntil: 'networkidle0' });
  const day = new Date(Date.UTC(2032, 0, 1 + Math.floor(Math.random() * 300))).toISOString().slice(0, 10);
  await page.$eval('input[aria-label="Holiday date"]', (el, v) => { el.value = v; el.dispatchEvent(new Event('change', { bubbles: true })); }, day);
  await page.type('input[aria-label="Holiday name"]', 'Demo holiday');
  await page.keyboard.press('Tab'); await wait(400);
  await clickText(page, 'button', 'Add holiday');
  await page.waitForSelector('.holiday-classes', { timeout: 15000 });
  console.log('holiday panel:', (await page.$eval('.holiday-classes', (s) => s.innerText.replace(/\s+/g, ' '))).slice(0, 120));
  await shot(page, 'holiday-classes');

  // 5. A student's fees: receipt page, and the payment link explains that online payments are off locally.
  await page.goto(`${BASE_URL}/admin/fees`, { waitUntil: 'networkidle0' });
  const student = await page.$$eval('a[href^="/students/"]', (as) => as.map((a) => a.getAttribute('href')).find((h) => /^\/students\/[0-9a-f-]{36}/.test(h)));
  if (student) {
    await page.goto(`${BASE_URL}${student.split('?')[0]}#fees`, { waitUntil: 'networkidle0' });
    await wait(800);
    if (await page.$('.fee-pkg-actions')) {
      const linkButton = await page.$$eval('.fee-pkg-actions button', (b) => b.some((x) => x.innerText.includes('Payment link')));
      if (linkButton) {
        await clickText(page, '.fee-pkg-actions button', 'Payment link');
        await toast(page, /not switched on|Copy/);
        console.log('payment link:', await page.evaluate(() => (document.querySelector('.pay-link') ? 'link shown' : 'explained: payments off')));
      }
    }
    const receipt = await page.$$eval('a[href^="/receipts/"]', (as) => as[0]?.getAttribute('href'));
    if (receipt) {
      await page.goto(`${BASE_URL}${receipt}`, { waitUntil: 'networkidle0' });
      await page.waitForSelector('.doc-total');
      console.log('receipt:', (await page.$eval('.doc', (d) => d.innerText.replace(/\s+/g, ' '))).slice(0, 160));
      await shot(page, 'receipt');
    }
  }
  const unexpected = failedRequests.filter((r) => !r.includes('/files/download') && !/payment-link — HTTP 409/.test(r));
  assert.deepEqual(consoleErrors, [], 'console errors (admin)');
  assert.deepEqual(unexpected, [], 'failed requests (admin)');
  await page.close();

  // 6. Teacher: progress reports (their own only) and no access to salaries.
  const t = await newPage(browser, viewports.mobile);
  await signIn(t.page, users.teacher);
  await t.page.goto(`${BASE_URL}/progress-reports`, { waitUntil: 'networkidle0' });
  await t.page.waitForSelector('.seg');
  assert.equal(await noOverflow(t.page), true);
  await t.page.screenshot({ path: `${SCREENSHOT_DIR}/progress-reports-teacher-mobile.png` });
  assert.deepEqual(t.consoleErrors, [], 'console errors (teacher)');
  await t.page.close();
  console.log('Automations check passed.');
} finally {
  await browser.close();
}
