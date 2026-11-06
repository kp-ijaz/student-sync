// Finds elements whose right edge passes the content gutter at phone width (html overflow-x:hidden hides real overflow).
// Usage: node puppeteer/edge-check.mjs [width]
import { BASE_URL, launch, newPage, signIn, users } from "./browser.mjs";
const width = Number(process.argv[2] ?? 375);
const flows = {
  student: { user: users.student, pages: ["/student", "/subjects", "/timetable", "/learning", "/doubts", "/profile", "/progress", "/learning-plan", "/messages", "/assistant"] },
  teacher: { user: users.teacher, pages: ["/teacher", "/sessions", "/sessions/new", "/teacher/students", "/teacher/tasks", "/teacher/availability", "/assignments", "/exams", "/questions", "/videos"] },
  admin: { user: users.admin, pages: ["/admin", "/admin/students", "/admin/teachers", "/admin/batches", "/admin/academics", "/admin/arrangements", "/admin/reports", "/admin/settings", "/admin/parents"] },
};
const browser = await launch();
try {
  for (const [role, flow] of Object.entries(flows)) {
    const { page } = await newPage(browser, { width, height: 812 });
    await signIn(page, flow.user);
    for (const route of flow.pages) {
      await page.goto(`${BASE_URL}${route}`, { waitUntil: "networkidle0" });
      await new Promise((r) => setTimeout(r, 500));
      const bad = await page.evaluate(() => {
        const vw = document.documentElement.clientWidth, out = [];
        const scrollers = (el) => { for (let p = el.parentElement; p; p = p.parentElement) { const o = getComputedStyle(p).overflowX; if ((o === "auto" || o === "scroll") && p !== document.documentElement && p !== document.body) return true; } return false; };
        for (const el of document.querySelectorAll(".content *")) {
          const r = el.getBoundingClientRect();
          if (r.width === 0 || r.right <= vw - 15 || scrollers(el)) continue;
          if (el.closest(".tabbar, .sidebar, .sr-only, .toasts")) continue;
          const parentBad = out.some((o) => o.el.contains(el));
          if (!parentBad) out.push({ el, d: `${el.tagName.toLowerCase()}.${[...el.classList].join(".")} right=${Math.round(r.right)} w=${Math.round(r.width)}` });
        }
        return out.slice(0, 6).map((o) => o.d);
      });
      console.log(`${bad.length ? "!!" : "ok"} ${role} ${route}${bad.length ? "\n   " + bad.join("\n   ") : ""}`);
    }
    await page.close();
  }
} finally { await browser.close(); }
