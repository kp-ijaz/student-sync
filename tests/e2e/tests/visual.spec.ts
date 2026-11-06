import { test, expect } from "@playwright/test";

// Pixel baselines for screens without live data. Update intentionally with `npx playwright test visual --update-snapshots`.
test.describe("Visual regression @responsive", () => {
  test("sign-in page", async ({ page }) => {
    await page.goto("/login");
    await page.waitForLoadState("networkidle");
    await expect(page).toHaveScreenshot("login.png", { fullPage: true, maxDiffPixelRatio: 0.01 });
  });

  test("offline page", async ({ page }) => {
    await page.goto("/offline.html");
    await expect(page).toHaveScreenshot("offline.png", { fullPage: true, maxDiffPixelRatio: 0.01 });
  });
});
