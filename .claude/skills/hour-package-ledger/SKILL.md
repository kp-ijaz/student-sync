---
name: hour-package-ledger
description: Use for learning-hour packages, purchases/payments, hour balances, deduction on completed billable sessions, refunds/adjustments, ledger history and idempotency — the institute's commercial core.
---

# Hour packages, payments and the hour ledger

> **Flutter is parked this phase (owner decision 2026-11-04).** Student Sync currently ships
> the web portals and the installable PWA only. Treat every Flutter/mobile-app instruction in
> this skill as suspended: do not build, test, mirror or verify anything under
> `mobile/student_sync` until the owner re-enables the app.

## Purpose and triggers

The institute sells prepaid hours (e.g. 30 h) usable across any subject (CLAUDE.md addendum A;
master prompt §6). Use this skill whenever work touches packages, purchases, payment records,
balances, hour consumption, adjustments, expiry, or any screen that shows "remaining hours".

## Inspect first

- `CLAUDE.md` addendum A and N; master prompt §6 and the open decisions in §23 (expiry, absence/late
  charging, billing increment, refunds/transfers, group-class charging). Do not invent these — keep
  them configurable and ask one focused question if a slice cannot proceed safely.
- `src/StudentSync.Domain/Scheduling/Sessions.cs` (`LearningSession` status/duration,
  `AttendanceRecord`), `Domain/Tenancy/Tenancy.cs` (`SubscriptionPlan` — the SaaS plan; **not** the
  student package, do not reuse it), `Domain/Common/Entities.cs` (`RowVersion`).
- `src/StudentSync.Application/Scheduling/SchedulingService.cs` (`SetStatusAsync`, `CancelAsync`),
  `Scheduling/AttendanceService.cs` (`MarkAsync`), `Common/AuditAndNotify.cs`,
  `src/StudentSync.Api/Hosting/Plumbing.cs` (`IdempotencyFilter`, `IdempotencyRecord`).
- `src/StudentSync.Application/Reporting/DashboardService.cs`, `ProgressService.cs` (where balances
  will surface), `Web/Components/Pages/Admin/StudentProfile.razor`, `Student/StudentDashboard.razor`,
  `mobile/student_sync/lib/features/student/dashboard_api.dart`.
- `IMPLEMENTATION_STATUS.md`: "Fees module not implemented, feature flag `fees_module` off".

## What exists / what is missing

Exists: session lifecycle (`Scheduled/Live/Completed/Cancelled`), attendance per participant,
`IdempotencyFilter` for POST replays, `AuditService`, `RowVersion` concurrency, feature flags.
Missing: everything in this domain — package catalogue, purchase, payment transaction, hour
account/ledger, billing state on sessions, balance display, low-balance notification.

## Rules that must hold

- Money and hours are separate: `PaymentTransaction` (amount, currency, method, reference, status,
  provider payload) vs `HourLedgerEntry` (signed hours, reason, reference to purchase / session /
  adjustment). A purchase credits hours only when payment is confirmed server-side.
- Package sizes/prices are configurable records, never constants. 30 h is only an example.
- Deduction happens once per (student, session) when the session is **Completed** and the
  participant's attendance/billing eligibility matches the institute policy. Cancelled sessions and
  (by default) absences deduct nothing; absence/late/cancellation charging and the billing increment
  (exact minutes vs 30-min / 1-h blocks) are institute settings with defaults and audit.
- Balance may never go negative; use a transaction + `RowVersion` on the hour account, and a unique
  index on `(StudentId, SessionId, Kind=Consumption)` so a replay cannot double-deduct.
- Manual adjustments require `InstitutionSettingsManage`, a reason, and an audit record with who/when/how much.
- Every ledger entry is append-only; corrections are new reversing entries.
- Teachers see hours only if the owner grants it (addendum I); students see their own only.

## Workflow

1. Settings: add `BillingPolicy` to institution settings (increment, absence rule, late rule,
   cancellation rule, expiry enabled) with safe defaults (exact minutes; absence/cancellation not charged).
2. Entities: `LearningPackage` (name, hours, price, validity days, status), `PackagePurchase`
   (student, package, hours, price, status, expiry), `PaymentTransaction`, `HourAccount`
   (per student, RowVersion), `HourLedgerEntry` (kind, hours, balance-after, reference ids, reason,
   created-by). Add `BillingStatus` (NotBillable/Pending/Charged/Waived) on `SessionParticipant`.
3. Service `HourLedgerService`: `Credit(purchase)`, `ChargeSession(sessionId)` (idempotent, one
   transaction, computes per-participant duration under the policy), `Adjust`, `Balance`, `History`.
   Call `ChargeSession` from `SchedulingService.SetStatusAsync(Completed)` and from a reconcile job
   in `Application/Jobs/Jobs.cs` for sessions completed before attendance was finalised.
4. Endpoints: `/packages` (admin CRUD), `/students/{id}/purchases`, `/students/{id}/hours`
   (balance + ledger, paged), `/me/hours`; POSTs behind `IdempotencyFilter`.
5. UI: admin package list + "Record purchase/payment" dialog; balance card on student profile,
   student dashboard, Flutter home; ledger table with reason and reference.
6. Notifications: low balance / expiry via `NotificationService.NotifyAsync` with a dedupe key.
7. Seed: two packages, one purchase per demo student, a few ledger entries.

## Security and permissions

- Admin-only writes; students read own; tenant filter + explicit ownership checks.
- Never trust client-provided amounts for credits — derive from the package record.
- Payment-provider callbacks (if ever added) must verify signatures and be idempotent by provider reference.

## Required tests

- Unit: duration → hours under each increment; absence/late/cancel policies; negative-balance guard.
- Integration (Testcontainers): 60-min completed session deducts 1.0 h; 90-min deducts 1.5 h with exact
  minutes; replaying completion deducts once; two concurrent completions produce one entry;
  cancelled session deducts nothing; adjustment is audited; student of other institute → 404;
  teacher cannot adjust (403).
- Browser (puppeteer): record a purchase, complete a class, see the balance fall on admin and student views.

## Acceptance / reporting checklist

- [ ] Addendum N items on packages, exact-once consumption, non-billable sessions and concurrency pass.
- [ ] Policy defaults documented in `DEVELOPMENT.md`/`USER_GUIDE.md`; open §23 decisions listed as assumptions.
- [ ] Report: commands, test counts, screenshots, unverified items.
