import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { signIn, users, collectConsoleErrors, demoPassword } from "./helpers";

test.describe("Sign-in and role navigation", () => {
  test("wrong password shows a generic message", async ({ page }) => {
    await page.goto("/login");
    await page.getByLabel("Email").fill(users.student);
    await page.getByLabel("Password", { exact: true }).fill("not-the-password-1A");
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page.getByRole("alert")).toContainText("email or password is incorrect");
  });

  for (const [role, email, home, heading] of [
    ["admin", users.admin, "/admin", "Institution dashboard"],
    // Dashboards greet with the first name as the heading (mobile HomeGreeting pattern); the greeting line sits above it.
    ["teacher", users.teacher, "/teacher", "Amira"],
    ["student", users.student, "/student", "Aisha"],
    ["super admin", users.superAdmin, "/platform", "Platform overview"],
  ] as const) {
    test(`${role} lands on their dashboard without console errors`, async ({ page }) => {
      const errors = collectConsoleErrors(page);
      await signIn(page, email);
      await expect(page).toHaveURL(new RegExp(home + "$"));
      await expect(page.getByRole("heading", { level: 1 })).toContainText(heading);
      expect(errors, errors.join("\n")).toEqual([]);
    });
  }

  test("student cannot open admin pages", async ({ page }) => {
    await signIn(page, users.student);
    await page.goto("/admin/students");
    await expect(page.getByText("You don't have access to this page")).toBeVisible();
  });

  test("sign out returns to the sign-in page and protects pages", async ({ page }) => {
    await signIn(page, users.student);
    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page).toHaveURL(/\/login/);
    await page.goto("/student");
    await expect(page).toHaveURL(/\/login/);
  });
});

test.describe("Teaching workflows", () => {
  test("admin schedules a one-to-one lesson without any batch", async ({ page }) => {
    await signIn(page, users.admin);
    await page.goto("/sessions/new?type=OneToOne");
    await expect(page.getByRole("button", { name: /One-to-one/ })).toHaveAttribute("aria-pressed", "true");
    await page.getByLabel("Teacher *").selectOption({ label: "Priya Nair (Demo Teacher)" });
    await page.getByLabel(/Hiro Tanaka/).check();
    const title = `E2E one-to-one ${Date.now()}`;
    await page.getByLabel("Title *").fill(title);
    const date = new Date(Date.now() + 1000 * 60 * 60 * 24 * (70 + Math.floor(Math.random() * 200)));
    await page.getByLabel("Date *").fill(date.toISOString().slice(0, 10));
    await page.getByLabel("Start time *").fill("05:15");
    await page.getByRole("button", { name: /Preview & check conflicts/ }).click();
    await expect(page.getByText("No conflicts found")).toBeVisible();
    await page.getByRole("button", { name: "Schedule session" }).click();
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(title);
    await expect(page.getByText("No batch")).toBeVisible();
    await expect(page.getByText("One-to-one").first()).toBeVisible();
  });

  test("teacher marks attendance for a completed batch session", async ({ page }) => {
    await signIn(page, users.teacher);
    await page.goto("/sessions?status=Completed");
    await page.getByLabel("Status").selectOption("Completed");
    await page.getByRole("button", { name: "Previous period" }).click();
    const first = page.locator(".session-item a.title").first();
    await first.click();
    await page.getByRole("tab", { name: "Attendance" }).click();
    await page.getByRole("button", { name: "Mark all present" }).click();
    await page.getByRole("button", { name: "Save attendance" }).click();
    await expect(page.getByText(/Attendance saved for \d+ student/)).toBeVisible();
  });

  test("student asks a doubt and sees it routed to their teacher", async ({ page }) => {
    await signIn(page, users.fatima);
    await page.goto("/doubts/new");
    await page.getByLabel("Question title *").fill(`E2E doubt ${Date.now()}`);
    await page.getByLabel("Describe what you don't understand *").fill("Why do noble gases not react?");
    await page.getByLabel("Subject").selectOption({ label: "Chemistry" });
    await page.getByRole("button", { name: "Send to teacher" }).click();
    await expect(page.locator(".badge", { hasText: /^Assigned$/ }).first()).toBeVisible();
  });

  test("teacher sees AI drafts as not visible to students until approved", async ({ page }) => {
    await signIn(page, users.teacher);
    await page.goto("/teacher");
    const draftLink = page.locator("a[href^='/ai/drafts/']").first();
    if (await draftLink.count()) {
      await draftLink.click();
      await expect(page.getByText(/not visible to students until you approve/i)).toBeVisible();
    }
  });

  test("parent accounts are told the institute has no parent portal", async ({ page }) => {
    await page.goto("/login");
    await page.getByLabel("Email").fill(users.parent);
    await page.getByLabel("Password", { exact: true }).fill(demoPassword());
    await page.getByRole("button", { name: "Sign in" }).click();
    await expect(page.getByRole("alert")).toContainText("Parent accounts are not used");
  });

  test("side menu groups open and lead to their pages", async ({ page }) => {
    await signIn(page, users.student);
    const communication = page.getByRole("button", { name: "Communication" });
    await expect(communication).toHaveAttribute("aria-expanded", "false");
    await communication.click();
    await expect(communication).toHaveAttribute("aria-expanded", "true");
    await page.locator(".sidebar").getByRole("link", { name: "Announcements" }).click();
    await expect(page).toHaveURL(/\/announcements$/);
    await expect(page.locator(".nav a.active")).toHaveText("Announcements");
  });

  test("student sees their classes as a month calendar", async ({ page }) => {
    await signIn(page, users.student);
    await page.goto("/sessions");
    await expect(page.getByRole("button", { name: "Month" })).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".calendar")).toBeVisible();
  });

  test("pre-recorded video stays locked for students until the teacher unlocks it", async ({ page, browser }) => {
    const title = `E2E revision video ${Date.now()}`;
    await signIn(page, users.teacher);
    await page.goto("/videos");
    await page.getByRole("button", { name: "Add video" }).click();
    const option = page.locator('#vs optgroup[label="Upcoming classes"] option', { hasText: "G10 Maths" }).first();
    await option.waitFor({ state: "attached" });
    await page.locator("#vs").selectOption({ label: await option.innerText() });
    await page.getByLabel("Title *").fill(title);
    await page.getByRole("button", { name: "After the class" }).click();
    await page.locator("#vf").setInputFiles({ name: "lesson.mp4", mimeType: "video/mp4", buffer: Buffer.from([0, 0, 0, 0x18, ...Buffer.from("ftypisom"), 0, 0, 0, 0]) });
    await page.getByRole("button", { name: "Upload video" }).click();
    const teacherRow = page.locator("li.video-row", { hasText: title });
    await expect(teacherRow).toContainText("Locked");

    const student = await (await browser.newContext()).newPage();
    await signIn(student, users.student);
    await student.goto("/videos");
    const studentRow = student.locator("li.video-row", { hasText: title });
    await expect(studentRow).toContainText("Locked");
    await expect(studentRow.getByRole("button", { name: "Watch" })).toHaveCount(0);

    await teacherRow.getByLabel(/Access for/).selectOption("Unlocked");
    await expect(page.getByText("Video unlocked for students.")).toBeVisible();
    await student.reload();
    await expect(studentRow).toContainText("Unlocked by teacher");
    await expect(studentRow.getByRole("button", { name: "Watch" })).toBeVisible();

    await teacherRow.getByRole("button", { name: "Delete" }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Delete" }).click();
    await expect(teacherRow).toHaveCount(0);
  });
});

test.describe("Admin-only accounts and the learning plan", () => {
  test("sign-in has no registration and /register is not found", async ({ page }) => {
    await page.goto("/login");
    await expect(page.getByRole("link", { name: /register/i })).toHaveCount(0);
    await expect(page.getByText("sign-in details from the institute office")).toBeVisible();
    await page.goto("/register");
    // The route is gone: the not-found page (or, after enhanced navigation, sign-in) and never a registration form.
    await expect(page.getByRole("button", { name: "Send registration" })).toHaveCount(0);
    await expect(page.locator("h1").first()).toContainText(/couldn't find that page|welcome back/i);
  });

  test("student adds and withdraws a weak subject on the learning plan", async ({ page }) => {
    await signIn(page, users.student);
    await page.goto("/learning-plan");
    await expect(page.getByRole("heading", { name: "My learning plan" })).toBeVisible();
    const note = `E2E hard topic ${Date.now()}`;
    await page.locator("#lp-subject").selectOption({ label: "Social Science" });
    await page.locator("#lp-note").fill(note);
    await page.getByRole("button", { name: "Add subject" }).click();
    await expect(page.getByText(note)).toBeVisible();
    await page.getByRole("button", { name: "Remove Social Science" }).click();
    await expect(page.getByText(note)).toHaveCount(0);
  });
});

test.describe("Tenant isolation in the browser", () => {
  test("another institution's admin cannot open a Horizon session by URL", async ({ page, browser }) => {
    await signIn(page, users.teacher);
    await page.goto("/sessions");
    const href = await page.locator(".session-item a.title").first().getAttribute("href");
    const other = await browser.newPage();
    await signIn(other, users.otherAdmin);
    await other.goto(href!);
    await expect(other.getByRole("heading", { name: "Not found" })).toBeVisible();
    await other.close();
  });
});

test.describe("Responsive layout @responsive", () => {
  for (const [name, email, url] of [
    ["student dashboard", users.student, "/student"],
    ["teacher dashboard", users.teacher, "/teacher"],
    ["admin dashboard", users.admin, "/admin"],
    ["sessions", users.admin, "/sessions"],
    ["student calendar", users.student, "/sessions"],
    ["student videos", users.student, "/videos"],
    ["student learning plan", users.student, "/learning-plan"],
    ["admin students", users.admin, "/admin/students"],
    ["admin teachers", users.admin, "/admin/teachers"],
    ["admin reports", users.admin, "/admin/reports"],
    ["admin parents", users.admin, "/admin/parents"],
  ] as const) {
    test(`${name} has no horizontal overflow`, async ({ page }) => {
      if (email) await signIn(page, email);
      await page.goto(url);
      await page.waitForLoadState("networkidle");
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow).toBeLessThanOrEqual(0);
      // html/body use overflow-x: hidden, so scrollWidth alone misses content pushed past the edge: check element edges
      // (outside intentional scrollers) and that stacked tables never need to scroll sideways on phones.
      const offenders = await page.evaluate(() => {
        const vw = document.documentElement.clientWidth;
        const inScroller = (el: Element) => { for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) { const o = getComputedStyle(p).overflowX; if (o === "auto" || o === "scroll") return true; } return false; };
        const past = [...document.querySelectorAll(".content *")].filter((el) => el.getBoundingClientRect().right > vw + 0.5 && !inScroller(el) && !el.closest(".sr-only"));
        const stuck = [...document.querySelectorAll("table.stack-mobile")].filter((t) => getComputedStyle(t.querySelector("thead") ?? t).display === "none" && t.parentElement!.scrollWidth > t.parentElement!.clientWidth + 1);
        return [...past, ...stuck].slice(0, 5).map((el) => `${el.tagName.toLowerCase()}.${[...el.classList].join(".")}`);
      });
      expect(offenders).toEqual([]);
    });
  }
});

test.describe("Accessibility (axe, WCAG 2.1 AA)", () => {
  for (const [name, email, url] of [
    ["sign-in", null, "/login"],
    ["student dashboard", users.student, "/student"],
    ["teacher dashboard", users.teacher, "/teacher"],
    ["admin dashboard", users.admin, "/admin"],
    ["new session form", users.admin, "/sessions/new"],
    ["student calendar", users.student, "/sessions"],
    ["teacher videos", users.teacher, "/videos"],
    ["student learning plan", users.student, "/learning-plan"],
    ["admin student profile", users.admin, "/admin/students"],
    ["teacher question bank", users.teacher, "/questions"],
    ["teacher new diagnostic", users.teacher, "/exams/new?purpose=Diagnostic"],
    ["admin teachers", users.admin, "/admin/teachers"],
    ["admin reports", users.admin, "/admin/reports"],
    ["admin parents", users.admin, "/admin/parents"],
  ] as const) {
    test(`${name} has no serious or critical violations`, async ({ page }) => {
      if (email) await signIn(page, email);
      await page.goto(url);
      await page.waitForLoadState("networkidle");
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
      const serious = results.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
      expect(serious.map((v) => `${v.id}: ${v.nodes.length} node(s) — ${v.help}`), JSON.stringify(serious.map(v => v.nodes.slice(0, 3).map(n => n.target)), null, 1)).toEqual([]);
    });
  }
});

// Phones/tablets use the mobile app's shell: floating four-tab bar, page title in the app bar, drawer for the rest.
test.describe("Mobile shell @responsive", () => {
  test("student sees the four tabs, the page title in the app bar and the drawer", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "mobile", "phone-width shell only");
    await signIn(page, users.student);
    await page.goto("/subjects");
    await page.waitForLoadState("networkidle");
    const tabs = page.locator(".tabbar a");
    await expect(tabs).toHaveCount(4);
    await expect(page.locator(".tabbar a.selected")).toHaveText("Subjects");
    await expect(page.locator(".tabbar a.selected")).toHaveAttribute("aria-current", "page");
    await expect(page.locator(".appbar-title")).toHaveAttribute("data-title", "My subjects");
    await expect(page.locator(".topbar .desk-only").first()).toBeHidden();
    await page.locator(".tabbar a", { hasText: "Learning" }).click();
    await expect(page).toHaveURL(/\/learning$/);
    await expect(page.locator(".tabbar a.selected")).toHaveText("Learning");
    await page.getByRole("button", { name: "Open navigation" }).click();
    await expect(page.locator(".sidebar").getByRole("link", { name: "Doubt Centre" })).toBeVisible();
  });

  test("desktop keeps the side menu and no tab bar", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "desktop", "desktop only");
    await signIn(page, users.teacher);
    await page.goto("/teacher/tasks");
    await expect(page.locator(".tabbar")).toBeHidden();
    await expect(page.locator(".appbar-title")).toBeHidden();
    await expect(page.locator(".nav a.active")).toHaveText("Tasks");
  });
});
