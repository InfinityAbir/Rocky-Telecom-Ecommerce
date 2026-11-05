# STATUS.md — Rocky Telecom implementation roadmap

> Source of truth for implementation progress. Requirements: `doc/rocky-telecom-prd.md` v1.1. Architecture: `doc/ARCHITECTURE.md`. Conventions: `doc/CONVENTIONS.md`.
> All phases: **Not Started** (Step 1; no application code written).
> Existing Dazzle token capture renamed to `doc/DESIGN-REFERENCE.md`; authored UI/UX spec is `doc/DESIGN.md` (0.8 resolved — see D-19).

**How to use:** implement strictly in order. Each phase lists objective, tasks (small, grouped), dependencies, completion criteria. Mark status `Not Started / In Progress / Done`. Do not work ahead. After each phase: build + tests (+ Playwright if browser-facing) → verify → update this file.

**MVP scope note (per PRD §11/§13):** MVP = Phases 0–4 + minimal admin (dashboard, catalog, orders, settings). Reviews, trade-in, Q&A, blog extras default to v1.1 if timeline slips — cut in that order, never cut payments/IPN correctness, stock atomicity, or SEO slugs.

---

## Phase 0 — Setup & skeleton — Status: Not Started

**Objective:** Runnable solution skeleton, DB, Angular workspace, CI, tokens, seed data. No features.

**Tasks:**
- 0.1 Scaffold `RockyTelecom.sln` (Domain, Application, Infrastructure, Api) + EF Core + SQL Server connection + migrations pipeline.
- 0.2 Identity + JWT (15 min) + refresh httpOnly cookie + roles (Admin, Staff, Editor, Customer) + lockout-after-5 + `/health`.
- 0.3 Global exception middleware → `{ hasError, message, content }` envelope + Serilog (file + Seq) + correlation ID + Swagger + API versioning `/api/v1`.
- 0.4 `ICache` abstraction (Redis + `IMemoryCache` fallback), Hangfire wiring, `IImageStore` (local disk v1), rate-limit on auth/OTP/suggest.
- 0.5 Angular 18+ standalone + SSR + Tailwind v4 workspace, routing shell, layout stubs (header/footer/mobile-nav/top-strip), SEO service stub, typed API client stub.
- 0.6 Tailwind theme tokens from the clean capture in `doc/DESIGN-REFERENCE.md` per `doc/DESIGN.md` §2 (dark wood shell `#1A1A1A`/`#2E2B28`, gold CTA `#D4A97A`, brand `#965C20`, ink `#101828`, bg `#F3F4F6`) + Urbanist font + breakpoints 640/768/1024/1280/1536.
- 0.7 Seed: roles, admin user, single `StoreInfo`, 3-zone `DeliveryZone`, demo category/brand/template, mock payment/courier toggles ON.
- 0.8 **Docs gate:** ~~resolve `doc/DESIGN.md` collision~~ **resolved 2026-10-13 (D-19)**: capture renamed to `doc/DESIGN-REFERENCE.md`, UI/UX spec authored at `doc/DESIGN.md`. ~~Do not start Phase 1 until approved.~~ Approved by owner.
- 0.9 CI (GitHub Actions: build + test), dev/staging/prod env + secrets strategy, Docker Compose (`api`, `web-ssr`, `sql`, `redis`, `nginx`) draft.

**Dependencies:** PRD v1.1, ARCHITECTURE.md, CONVENTIONS.md approval.
**Completion criteria:** `dotnet build` + `ng build` green; EF migration applies to fresh SQL Server; `/health` OK; SSR home stub renders via `curl`; seed creates admin login; CI passes.

---

## Phase 1 — Catalog, listing, product, search, SEO — Status: Not Started

**Objective:** Browse → search → product detail with template-driven specs and SEO.

**Tasks:**
- 1.1 Category/Brand CRUD (admin) + public `GET /categories`, `GET /brands` (cached 60s).
- 1.2 SpecTemplate/SpecGroup/SpecField + VariantOption per category; admin UI renders product form from template (no free-form keys).
- 1.3 Product + Variant + Image CRUD, draft/publish, duplicate (stock 0), CSV import/export with row-level error report (never partial-silent).
- 1.4 Public `GET /products` (filters, sort, 24/page pagination + URL state), product card data (badge, discount %, stock status).
- 1.5 `GET /products/{slug}`: gallery, variant selector (price+stock per variant), Official/Unofficial/Used + warranty, EMI calculator hook, delivery estimate, tabs (Specs/Desc/Reviews-stub/Q&A-stub), related + recently viewed.
- 1.6 Search suggest (`products, brands, categories`, typo tolerance) + trending + recent (localStorage).
- 1.7 `GET /home` single-call payload + home sections (hero, icon row, flash-sale section read-only, trending/new/popular/hot-deal/featured, promo banners, brand strip, blogs-3, trust badges, SEO block).
- 1.8 SEO: clean slugs, canonical, OG, JSON-LD `Product`, sitemap.xml, robots.txt; SSR verified via `curl`.

**Dependencies:** Phase 0 done + DESIGN spec approved (0.8).
**Completion criteria:** Admin can create template → product with variants/images → publish (required-field gate enforced); storefront listing filters/sort/paginate correctly; variant switch updates price/stock; search suggest <400ms p95 stub; Lighthouse plan toward ≥80; no N+1 (verified via query count). Mobile (per FB-01): listing 2-col card grid at 360–430px without horizontal scroll; filters open as drawer/bottom-sheet; product gallery swipeable; header condenses + bottom nav visible; verified in Chrome DevTools mobile + one real Android device.

---

## Phase 2 — Auth, cart, wishlist — Status: Not Started

**Objective:** Accounts + guest/server cart merge.

**Tasks:**
- 2.1 Register/login (phone+password, email optional), OTP send/verify, forgot/reset, refresh-on-401, lockout.
- 2.2 Profile, addresses (BD validation), order-history shell.
- 2.3 Guest cart (localStorage) + server cart API (`GET/POST/PUT/DELETE /cart`, `POST /cart/merge`), qty/remove/coupon preview/delivery preview, stock re-check.
- 2.4 Wishlist (`/me/wishlist`), header cart/profile badges, mobile bottom nav wiring.
- 2.5 Rate limits + phone regex `^(?:\+?88)?01[3-9]\d{8}$` enforced server-side.

**Dependencies:** Phase 1 (product/variant APIs for cart lines).
**Completion criteria:** Guest adds to cart → registers/logs in → cart merges without loss/duplication; OTP flow works with mock SMS; refresh rotation works; Playwright: guest→login merge journey green.

---

## Phase 3 — Checkout, orders, tracking, notifications — Status: Not Started

**Objective:** Quote → order → track → invoice, with COD live and stock safety.

**Tasks:**
- 3.1 `POST /checkout/quote` (server price/delivery recompute preview).
- 3.2 `POST /orders` with Idempotency-Key, `RT-YYMMDD-XXXX` numbering, guest-phone orders, stock reserve 15 min (`rowversion`), address snapshot.
- 3.3 Delivery types: home (zone charge) / pickup (Sadullapur fixed); coupon application (percent/fixed, min/max/limits/expiry).
- 3.4 Order state machine + `OrderStatusHistory`, cancel-before-Shipped + stock return, admin status change + notes + print invoice + invoice PDF.
- 3.5 Tracking page `/order-tracking` (order no + phone) + account order detail.
- 3.6 SMS + email on placed/confirmed/shipped/delivered (mock providers, Hangfire fan-out).

**Dependencies:** Phases 1–2. **Must NOT** build real gateway here (Phase 4).
**Completion criteria:** Double-submit with same idempotency key creates exactly one order; prices differ from client → server wins; oversell attempt under concurrency never goes negative; cancel returns stock; tracking timeline correct; Playwright checkout journey green.

---

## Phase 4 — Payments (mock-first) + EMI + courier mock — Status: Not Started

**Objective:** Pluggable payments and courier behind interfaces; sandbox mock only.

**Tasks:**
- 4.1 `IPaymentGateway` + mock SSLCommerz (fake hosted page + deterministic IPN) + COD; bKash/Nagad stubs behind same interface.
- 4.2 `POST /payments/{gateway}/init`, `POST /payments/{gateway}/ipn` (signature verify, amount match, idempotent dedupe by Gateway+TrxId); payment success only via IPN.
- 4.3 Payment toggles mock/live in Settings; order `PaymentStatus` transitions; refund stub (admin).
- 4.4 EMI plans CRUD + calculator (bank/months/interest/min-price; show iff price ≥ min).
- 4.5 `ICourierService` mock: zone quote, book (fake consignment/tracking), track; `Shipment` table wiring.

**Dependencies:** Phase 3 order flow.
**Completion criteria:** Full mock payment round-trip (init → IPN → paid) works; browser-redirect-only (no IPN) never marks paid; amount-tampered IPN rejected; EMI hidden below min-price; courier mock returns tracking code.

---

## Phase 5 — Admin panel (minimal → full) — Status: Not Started

**Objective:** Client can run catalog/offers/orders/content without dev.

**Tasks:**
- 5.1 Admin shell (`/admin` lazy, role guards) + Dashboard (sales today/week/month, orders-by-status, low stock, top products, chart).
- 5.2 Catalog admin: product/variant/spec/image/bulk grid/duplicate/CSV (from 1.3, hardened), categories/brands.
- 5.3 Inventory: stock per variant, adjust log, low-stock alerts.
- 5.4 Orders admin: list/filter/status/print/refund/notes; pre-orders convert-to-order.
- 5.5 Offers: flash sale (start/end/products/%) + coupons + banners; flash-sale auto-revert job.
- 5.6 Content: banners, blogs, pages, FAQ, store info; EMI banks; support queues (tickets/trade-in/corporate); customers list/block/history.
- 5.7 Users & roles (Admin/Staff/Editor scopes enforced server-side) + Settings (site info, delivery zones, courier config, payment toggles, SMTP/SMS) + AuditLog viewer.

**Dependencies:** Phases 1–4 APIs. Can start UI shell in parallel after Phase 1, but full wiring needs Phase 4.
**Completion criteria:** Non-dev admin can add product → publish → it appears on storefront; flash sale expiry reverts price automatically; Staff cannot change pricing/roles (403 verified); every mutation appears in AuditLog.

---

## Phase 6 — Extras: compare, flash-sale UI, reviews, blogs, pre-order, forms — Status: Not Started

**Objective:** Remaining storefront depth (deferrable to v1.1 in order below).

**Tasks:**
- 6.1 Compare (max 4, same category): spec table, highlight diffs, shareable `/product-compare/{a}-vs-{b}`.
- 6.2 Flash-sale storefront: countdown, tabs Newest/Popular, Hot Deal of the Day.
- 6.3 Reviews (verified buyers only, admin moderation) + ratings in listing filters.
- 6.4 Blogs list/detail (3 on home), pre-order capture + product pre-order button, brands page, store location map.
- 6.5 Forms: support ticket, trade-in request, corporate inquiry, newsletter, feedback; policy pages (Terms/Privacy/Refund/Warranty/Exchange/Delivery/EMI/Cancellation/Cookies/Pre-order/Membership); About/Career/FAQ.

**Dependencies:** Phases 1, 5. **Cut order if late:** 6.3 → 6.5 trade-in → 6.5 Q&A → 6.4 blog to v1.1.
**Completion criteria:** Compare URL shareable and validates category match; countdown ends → price reverts; unverified review blocked; all forms persist + appear in admin queues.

---

## Phase 7 — Harden + launch — Status: Not Started

**Objective:** Secure, fast, SEO-clean launch.

**Tasks:**
- 7.1 Security: HTTPS/HSTS/CSP/secure cookies, upload whitelist, OWASP Top-10 review, PII minimization, no card storage.
- 7.2 Perf: SSR + edge cache, WebP/srcset/lazy, projections/`AsNoTracking`, home-cache invalidation, p95 ≤400ms API, LCP ≤2.5s 4G, Lighthouse ≥80.
- 7.3 SEO/A11y: sitemap/robots/canonical/structured data re-verified, WCAG AA contrast/keyboard/alt/aria.
- 7.3b Mobile acceptance (per FB-01): test matrix 360px / 390px / 768px + desktop; no horizontal scroll; tap targets ≥44px; bottom nav safe-area correct; checkout completable one-handed; mobile Playwright suite + Lighthouse mobile ≥80 on home/listing/product.
- 7.4 Reliability: daily backup + restore test, idempotent order/payment re-test, load test, UAT, deploy (VPS or Compose), monitoring (Seq + health).
- 7.5 Final review (workflow Step 9) + manual testing checklist (Step 10).

**Dependencies:** All prior phases.
**Completion criteria:** Checklists green; backup restored once; load test meets targets; UAT signed; prod deploy with mock→COD live, real gateways behind toggles OFF until merchant approval.

---

## Open Questions (block scope/timeline)

1. Courier partner? 2. Zone-flat vs weight charge? 3. Domain + hosting budget + Windows/Linux target? 4. EMI calculator-only (assumed yes)? 5. Clean Dazzle hex re-capture? 6. Showroom hours/phone/email/social? 7. Reviews/trade-in/Q&A/blog in v1 vs v1.1? 8. Storage day-one target? 9. Redis day-one vs memory fallback? 10. ~~`doc/DESIGN.md` collision resolution (blocks 0.8 → Phase 1)~~ — resolved 2026-10-13 (D-19).
11. ~~Clean Dazzle hex re-capture?~~ — resolved 2026-10-13 (D-21); real tokens locked in DESIGN.md §2.
12. Deployment scaffold = Docker Compose — resolved 2026-10-13 (D-20).

## Change log

| Date | Change |
|---|---|
| 2026-09-29 | Initial roadmap from PRD v1.1 (Step 1). All phases Not Started. No code. |
| 2026-09-29 | FB-01 approved: mobile-responsive made explicit (Phase 1 + 7.3b criteria). No scope/API/DB change. |
| 2026-10-13 | D-19/D-20/D-21: DESIGN.md collision resolved (capture → `DESIGN-REFERENCE.md`); Compose chosen for Phase 0; clean re-capture adopted and real theme tokens locked. Phase 0–2 implementation started. |
