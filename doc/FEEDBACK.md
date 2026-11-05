# FEEDBACK.md — Rocky Telecom

> Structured intake for client / supervisor / tester / user feedback. Do NOT implement on receipt — follow workflow Step 8: compare with PRD, check architecture/design/status/decisions, classify (new requirement / clarification / change / bug), assess impact, then await approval.

## Intake process

1. Append row to `Log` below (one row per item). Leave `Status = Triaged-pending-approval` until owner approves.
2. If approved as a requirement change: update PRD-tied docs first (ARCHITECTURE / DESIGN-spec / STATUS phase), link commit/PR, then implement via Implement→Verify cycle.
3. Never edit `doc/rocky-telecom-prd.md` or `doc/DESIGN.md` reference without explicit approval.

## Log

| ID | Feedback | Source | Date | Impact | Status | Related feature / phase |
|---|---|---|---|---|---|---|
| FB-01 | "The ui should mobile responsive also" | User (chat) | 2026-09-29 | docs-only clarification — already in PRD scope (§3.1 mobile bottom nav, §8 breakpoints 640/768/1024/1280/1536 + 16px mobile gutter, §9 mobile browsers + Lighthouse mobile ≥80); no DB/API change; makes mobile acceptance explicit in STATUS/CONVENTIONS + future DESIGN-spec | Approved-docs-updated | Storefront responsive / Phases 0, 1, 7 |
| — | *(no further feedback yet)* | — | — | — | — | — |

### Field guide

- **Feedback:** Verbatim or linked note + reporter expectation. Attach screenshots/URLs where relevant.
- **Source:** Client owner / supervisor / tester / user (+ name/channel).
- **Date:** YYYY-MM-DD.
- **Impact:** `none / docs-only / Phase-N scope change / architecture change / DB migration / timeline risk` + one-line why. Check: PRD conflict? ARCHITECTURE? DESIGN? STATUS dependency? DECISIONS precedent? Payment/stock/SEO sensitivity?
- **Status:** `Triaged-pending-approval / Approved-docs-updated / Implementing / Done / Rejected-with-reason`.
- **Related feature / phase:** e.g. `Checkout / Phase 3`, `Admin catalog / Phase 5`.

## Example (template only, not real feedback)

| ID | Feedback | Source | Date | Impact | Status | Related feature / phase |
|---|---|---|---|---|---|---|
| FB-EXAMPLE | "Add bKash refund from admin" | Client owner (call) | 2026-10-05 | Phase-4 scope change; needs gateway refund API + Payment transitions | Triaged-pending-approval | Payments / Phase 4 |
