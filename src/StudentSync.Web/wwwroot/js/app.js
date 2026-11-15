// Student Sync browser helpers (no user data is stored here except notes the user explicitly saves for offline use).
window.ss = {
  openUrl: (url) => { window.open(url, "_blank", "noopener"); },
  download: (filename, content, type) => {
    const blob = new Blob([content], { type: type || "text/plain" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob); a.download = filename; a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  },
  setLang: (lang) => { document.cookie = `ss_lang=${lang}; path=/; max-age=31536000; samesite=lax`; },
  // Offline notes: only notes a teacher marked "available offline" and the student chose to save.
  saveOfflineNote: (userId, id, title, html) => {
    const key = `ss-offline-${userId}`;
    const notes = JSON.parse(localStorage.getItem(key) || "{}");
    notes[id] = { title, html, savedAt: new Date().toISOString() };
    localStorage.setItem(key, JSON.stringify(notes));
    localStorage.setItem("ss-offline-owner", userId);
  },
  clearOffline: () => {
    Object.keys(localStorage).filter(k => k.startsWith("ss-offline")).forEach(k => localStorage.removeItem(k));
  },
  currentTime: (el) => (el && el.currentTime) || 0,
  renderMath: (el) => {
    if (!el || !window.renderMathInElement) return;
    window.renderMathInElement(el, { delimiters: [{ left: "\\(", right: "\\)", display: false }, { left: "\\[", right: "\\]", display: true }, { left: "$$", right: "$$", display: true }], throwOnError: false });
  },
  scrollToBottom: (el) => { if (el) el.scrollTop = el.scrollHeight; },
  // Deep links such as /sessions/{id}?tab=notes scroll to that section of the class page once it has loaded.
  scrollToId: (id) => {
    const el = document.getElementById(id);
    if (!el) return false;
    const reduce = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    el.scrollIntoView({ behavior: reduce ? "auto" : "smooth", block: "start" });
    return true;
  },
};

(function () {
  const banner = () => document.getElementById("offline-banner");
  const update = () => { const b = banner(); if (b) b.hidden = navigator.onLine; };
  window.addEventListener("online", update);
  window.addEventListener("offline", update);
  document.addEventListener("DOMContentLoaded", update);
  // Sign-out clears any notes saved for offline reading on this device.
  document.addEventListener("submit", (e) => { if (e.target && e.target.action && e.target.action.endsWith("/account/logout")) window.ss.clearOffline(); });
  if ("serviceWorker" in navigator) {
    window.addEventListener("load", () => navigator.serviceWorker.register("/service-worker.js").catch(() => {}));
  }
})();

// Install banner (InstallBanner.razor). Chromium browsers fire "beforeinstallprompt" when the app can be installed:
// keep the event and offer an Install button. iOS has no such event, so the banner shows the Share-menu steps there.
// Nothing is shown inside the installed app, or for 14 days after "Not now".
(function () {
  const KEY = "ss-install-dismissed", SNOOZE_MS = 14 * 24 * 60 * 60 * 1000;
  let deferred = null;
  const installed = () => window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
  // iPadOS reports itself as a Mac; the touch points tell them apart.
  const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const snoozed = () => { try { return Date.now() - Number(localStorage.getItem(KEY) || 0) < SNOOZE_MS; } catch { return false; } };
  const mode = () => (installed() || snoozed() ? null : deferred ? "prompt" : ios ? "ios" : null);
  const update = () => {
    const m = mode();
    document.querySelectorAll("ss-install-banner").forEach((banner) => {
      banner.hidden = !m;
      banner.querySelectorAll("[data-install-mode]").forEach((el) => { el.hidden = el.dataset.installMode !== m; });
    });
  };
  window.addEventListener("beforeinstallprompt", (e) => { e.preventDefault(); deferred = e; update(); });
  window.addEventListener("appinstalled", () => { deferred = null; update(); });
  document.addEventListener("click", (e) => {
    const button = e.target instanceof Element ? e.target.closest("[data-install-action]") : null;
    if (!button) return;
    if (button.dataset.installAction === "dismiss") {
      try { localStorage.setItem(KEY, String(Date.now())); } catch { /* private mode: hidden for this page only */ }
      deferred = null;
    } else if (deferred) {
      const prompt = deferred;
      deferred = null; // a prompt event can be used once; the browser sends a new one if the person declines
      prompt.prompt();
    }
    update();
  });
  // A custom element so the state is applied whenever Blazor creates the banner (first load, prerender → interactive).
  // Blazor attaches the element before appending its children, so apply again once they exist (otherwise both
  // instruction lines stayed visible).
  customElements.define("ss-install-banner", class extends HTMLElement {
    connectedCallback() { update(); queueMicrotask(update); requestAnimationFrame(update); }
  });
  // Enhanced navigation re-applies the server markup (hidden); restore the state afterwards.
  if (window.Blazor && window.Blazor.addEventListener) window.Blazor.addEventListener("enhancedload", update);
})();

// Tab bars (role="tablist", e.g. PillTabs and .tabs): Left/Right/Home/End move to and select the neighbouring tab,
// following the WAI-ARIA tabs pattern (automatic activation).
document.addEventListener("keydown", (e) => {
  const tab = e.target && e.target.closest && e.target.closest('[role="tab"]');
  const list = tab && tab.closest('[role="tablist"]');
  if (!list || !["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key)) return;
  const tabs = Array.from(list.querySelectorAll('[role="tab"]:not([disabled])'));
  const i = tabs.indexOf(tab);
  const rtl = getComputedStyle(list).direction === "rtl";
  const step = (e.key === "ArrowRight") !== rtl ? 1 : -1;
  const next = e.key === "Home" ? tabs[0] : e.key === "End" ? tabs[tabs.length - 1] : tabs[(i + step + tabs.length) % tabs.length];
  if (!next || next === tab) return;
  e.preventDefault();
  next.focus();
  next.click();
});

// Sign-in: the eye button shows or hides the password (as in the mobile app); the field keeps its value and focus.
document.addEventListener("click", (e) => {
  const btn = e.target && e.target.closest && e.target.closest("[data-toggle-password]");
  if (!btn) return;
  const input = document.getElementById(btn.getAttribute("data-toggle-password"));
  if (!input) return;
  const show = input.type === "password";
  input.type = show ? "text" : "password";
  btn.setAttribute("aria-pressed", show ? "true" : "false");
  btn.setAttribute("aria-label", show ? "Hide password" : "Show password");
  btn.classList.toggle("on", show);
  input.focus({ preventScroll: true });
});
