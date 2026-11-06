---
name: student-sync-security
description: Use for any change touching authentication, permissions, student/teacher data, files, exam content, hour balances, audit logs or sensitive records — enforces server-side authorization, tenant and student isolation, child safety and secure file access.
---

# Student Sync security

## Purpose and triggers
Student and children's data is highly sensitive (CLAUDE.md §7; addendum sections B, G, H, I, L). Use this skill for
every endpoint, query, file path, background job or UI that reads or writes people, sessions, content, answers,
results, documents, packages/payments, or credentials, and for any auth/account change.

## Inspect first
- `src/StudentSync.Domain/Identity/Identity.cs` — `Permissions` constants and the `RolePermissions` matrix (roles are
  never used directly for decisions; endpoints call `.RequirePermission(Permissions.X)`).
- `src/StudentSync.Api/Endpoints/CoreEndpoints.cs`, `LearningEndpoints.cs` — how groups are protected;
  `src/StudentSync.Api/Hosting/Plumbing.cs` (idempotency, current user), `ErrorHandling.cs` (problem+code mapping).
- `src/StudentSync.Infrastructure/Persistence/AppDbContext.cs` — tenant global query filters on every
  `TenantEntity`, tenant stamping on save and the cross-tenant write guard.
- `src/StudentSync.Infrastructure/Identity/AuthService.cs` — login, rotating refresh tokens (family reuse
  detection), lockout, temporary-password gate (`MustChangePassword`).
- `src/StudentSync.Application/Common/Abstractions.cs` — `ICurrentUser`, `ITenantContext`, `IFileStorage`,
  `IFileAccessTokens` (short-lived, user-bound file links), `IUserAccountService`.
- `src/StudentSync.Application/People/PeopleService.cs` — `VisibleStudentIdsAsync` (teachers only see students they
  teach), credential issuance (`CreatedPersonResponse` with a one-time temporary password).
- `tests/StudentSync.IntegrationTests/ApiTests.cs` — existing isolation tests to copy the pattern from.

## Rules (must hold)
- Authorization is server-side on every request; hiding a button is not authorization.
- Derive tenant and user from the authenticated principal. Never trust a client-supplied tenant, student, teacher
  or session id without verifying it belongs to the caller's institution and permitted scope.
- Students see only their own records and content published to them; never another student's answers, results,
  attendance, files, hours or profile. Teachers see only assigned/visible students. Parents have no login.
- **Student accounts are admin-created only** (addendum B): no public registration endpoint or page; temporary
  password forced to change at first login; plaintext never stored or logged; reset/activate/deactivate/suspend are
  admin actions and audited.
- Files: validate type by signature, size and ownership; serve private files only through `IFileAccessTokens`
  links; never expose storage keys or public URLs. Student books, exam scripts and recordings are private.
- Exam answer keys and results stay hidden until the configured release; attempt snapshots are immutable.
- Hour ledger and payments: server-side transactions, RowVersion concurrency, idempotency keys; no negative
  balances; every adjustment audited with who/when/why.
- Audit (`AuditLog` via the audit service) every privileged or data-changing action; never log passwords, tokens,
  reset codes, file contents or unnecessary personal data.
- AI/OCR: no student document or identifiable data leaves the system without the configured provider, consent and
  redaction; drafts are never student-visible before teacher approval.
- Demo/seed data is fictional (`example.test`); secrets live in `.env`/configuration, never in source.

## Workflow
1. List the data touched and who may read/write it (admin, teacher-of-student, student-self). Map each action to
   an existing `Permissions` constant or add one to the matrix with a unit test.
2. Write the query so scope is enforced in the `Where` clause (visible student set, participant membership,
   author/owner), not after loading.
3. Validate input server-side (`RequestValidationException` in `src/StudentSync.Application/Common/Errors.cs`), map request DTOs explicitly (no over-posting).
4. Add audit records and, for commands that may be retried, idempotency support.
5. Add integration tests: wrong role 403, other institution 404, other student 404, forged/expired file token 403,
   replayed command no duplicate effect.
6. Review the diff for IDOR, mass assignment, injection (EF parameterised only), XSS (Markdown sanitised via
   existing pipeline), open redirects, and secrets.

## Acceptance criteria
- Every new endpoint has `RequirePermission` and tenant/ownership scoping with tests proving isolation.
- No plaintext secrets, tokens or personal data in logs, tests, screenshots or commits.
- Sensitive actions appear in the audit log with a useful summary and no secrets.

## Completion checklist (report exactly)
- Permissions used/added; isolation tests added and their results.
- Any residual risk, deferred hardening, or configuration the owner must provide.
- Related: `api-integration`, `student-sync-testing`, `hour-package-ledger`, `document-chapter-processing`.
