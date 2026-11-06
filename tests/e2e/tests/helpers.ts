import { expect, Page } from "@playwright/test";
import fs from "fs";
import path from "path";

/** Demo password comes from the git-ignored .env (DEMO_PASSWORD) or the E2E_PASSWORD variable — never hard-coded. */
export function demoPassword(): string {
  if (process.env.E2E_PASSWORD) return process.env.E2E_PASSWORD;
  const env = fs.readFileSync(path.resolve(__dirname, "../../../.env"), "utf8");
  const line = env.split("\n").find((l) => l.startsWith("DEMO_PASSWORD="));
  if (!line) throw new Error("Set E2E_PASSWORD or DEMO_PASSWORD in .env");
  return line.slice("DEMO_PASSWORD=".length).trim();
}

export const users = {
  admin: "admin@horizon.example.test",
  teacher: "amira@horizon.example.test",
  privateTeacher: "priya@horizon.example.test",
  student: "aisha@horizon.example.test",
  fatima: "fatima@horizon.example.test",
  parent: "parent.khan@horizon.example.test",
  otherAdmin: "admin@northbridge.example.test",
  superAdmin: "superadmin@studentsync.example.test",
};

export async function signIn(page: Page, email: string) {
  await page.goto("/login");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password", { exact: true }).fill(demoPassword());
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page).not.toHaveURL(/\/login/);
  // Wait for the interactive circuit so clicks are handled.
  await page.waitForLoadState("networkidle");
}

export function collectConsoleErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on("console", (m) => { if (m.type() === "error") errors.push(m.text()); });
  page.on("pageerror", (e) => errors.push(e.message));
  return errors;
}
