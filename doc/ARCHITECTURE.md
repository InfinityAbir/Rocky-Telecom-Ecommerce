# ARCHITECTURE.md — Rocky Telecom

> Source of truth for architecture. PRD (`doc/rocky-telecom-prd.md` v1.1) is the source of truth for requirements.
> Existing reference `doc/DESIGN-REFERENCE.md` (Dazzle token capture, formerly `doc/DESIGN.md`) is preserved verbatim and used as design-token input. The authored UI/UX spec is `doc/DESIGN.md` (see D-19).

**Stack (per PRD, followed as-is):** ASP.NET Core Web API (.NET 10) + Angular 18+ (standalone, SSR) + SQL Server. Tailwind v4. No Angular Material. No new technologies introduced.

---

## 1. System overview

Single-tenant e-commerce for one showroom (Sadullapur, Gaibandha): public storefront (SSR for SEO) + role-guarded admin SPA module + REST API + SQL Server + background jobs + cache + file storage + external gateways (mock-first).

```
Browser (Angular SSR)  ──HTTPS──▶  Nginx ──▶ Api (Kestrel) ──▶ SQL Server
   │                                  │            ├─▶ Redis / IMemoryCache
   │                                  │            ├─▶ Hangfire (same DB or separate)
   │                                  └─▶ web-ssr (Node) for Angular SSR
   └─▶ CDN / object storage (product images, WebP)
External (behind interfaces): SSLCommerz/bKash/Nagad (mock v1), courier (mock v1), SMS, SMTP, Maps embed, GA4
```

Deployment shapes (PRD §12, both supported): (a) VPS + Nginx + Kestrel + Node SSR; (b) Docker Compose: `api`, `web-ssr`, `sql`, `redis`, `nginx`. Envs: dev / staging / prod. Secrets via env vars / user-secrets, never in repo.

---

## 2. Solution & project structure

```
RockyTelecom.sln
├─ RockyTelecom.Domain            // entities, enums, value objects, domain errors. No deps.
├─ RockyTelecom.Application       // CQRS (MediatR), DTOs, validators (FluentValidation), interfaces, behaviors
├─ RockyTelecom.Infrastructure    // EF Core, Identity, payments, SMS, email, storage, jobs, cache
├─ RockyTelecom.Api               // controllers, middleware, auth, Swagger, health
└─ RockyTelecom.Web               // Angular workspace (SSR)
```

Angular workspace (`RockyTelecom.Web/src/app`):

```
core/       // api client, auth service, guards, interceptors (auth, refresh-on-401, error-toast, loading), seo service
shared/     // product-card, price-pipe (BD lakh format), skeleton, rating, modal, stepper, toast
layout/     // header, footer, mobile-bottom-nav, top-strip (marquee-rtl)
features/   // home, catalog (listing), product (detail), compare, cart, checkout, account, tracking, content (blogs/pages/pre-order/offers/brands/store), forms (support/trade-in/corporate/newsletter)
admin/      // lazy-loaded, own layout, role-guarded: dashboard, catalog, categories/brands, inventory, orders, offers, pre-orders, customers, content, emi, support, users-roles, settings, audit-log
```

**Dependency direction (strict):** `Api → Application → Domain`; `Infrastructure → Application + Domain`; `Domain` depends on nothing. `Web` talks to `Api` only via versioned REST. No domain logic in controllers; no EF types leak into Application DTOs; no direct `Web → DB`.

---

## 3. Layers & responsibilities

| Layer | Owns | Must NOT |
|---|---|---|
| Domain | Entities & enums (§5 model), value objects (Money BDT, Phone, Slug, SKU), domain rules (FR-02/03/05/07/09/10/11), Order state machine | Reference EF, MediatR, HTTP |
| Application | Use cases as MediatR commands/queries, DTOs, FluentValidation, interfaces (`IPaymentGateway`, `ICourierService`, `ISmsSender`, `IEmailSender`, `IImageStore`, `ICache`), pipeline behaviors (validation, logging, transaction), slug/redirect, idempotency, price recompute | Reference EF DbContext directly, do I/O except via interfaces |
| Infrastructure | EF Core DbContext + configs + migrations, Identity stores, gateway/courier mocks + real impls, Hangfire jobs, Redis/MemoryCache, Serilog, ImageSharp resize-to-WebP, SMTP/SMS clients | Contain HTTP routing or UI logic |
| Api | Controllers (thin), auth/JWT setup, middleware (exception→envelope, rate limit), Swagger, `/health`, versioning `/api/v1` | Business rules, price math, direct DB writes bypassing Application |
| Web | Routes, SSR, components, signals+services state, interceptors, SEO service, lazy routes, `@defer`, `NgOptimizedImage` | Trust client-computed prices; call DB directly |

MediatR pipeline order: `Validation → Logging → Transaction`. Every mutating command runs in one EF transaction; on failure rollback + envelope error.

---

## 4. Modules (bounded areas)

1. **Catalog:** Category/Brand/Product/Variant/Image/SpecTemplate/SpecGroup/SpecField/ProductSpec/VariantOption. Template-driven specs (FR-16..FR-20). Public read (cached) + admin write.
2. **Search & Listing:** Suggest (`products, brands, categories`), typo tolerance, trending, filters (brand, price, RAM, storage, color, availability, rating, offer), sort, server pagination 24/page. `NumericValue` for range filter/sort (FR-19).
3. **Cart:** Guest (`GuestToken`, localStorage) + server cart; merge on login; qty/remove/coupon/delivery preview; stock re-check.
4. **Checkout & Orders:** Quote → `POST /orders` (idempotency key, FR-11) → payment init → IPN confirm → status machine `Placed→Confirmed→Packed→Shipped→Delivered / Cancelled / Returned` + `OrderStatusHistory`. Cancel only before `Shipped` (FR-09, stock return). OrderNo `RT-YYMMDD-XXXX` (FR-08).
5. **Payments:** `IPaymentGateway` per gateway (SSLCommerz, bKash, Nagad, COD). Phase 1: sandbox mock only; real impls later behind same interface + `payment toggles (mock/live)` in Settings. Success only from server IPN, never browser redirect (FR-10); signature verify + amount match.
6. **Inventory:** Stock per variant, `rowversion` atomic decrement (FR-03), 15-min reservation at checkout start, low-stock alerts, adjust log.
7. **Offers:** FlashSale + FlashSaleItem (auto-revert via job, FR-04), Coupon (percent/fixed, min cart, max discount, usage/per-user limits, expiry — FR-07), Banner placements.
8. **Identity & Account:** ASP.NET Identity + JWT (15 min) + refresh (httpOnly SameSite cookie). Roles: Admin, Staff (orders+stock only, no pricing/roles), Editor (blogs/banners only), Customer. Phone+password, email optional, OTP verify, forgot/reset, lockout after 5 fails. Saved addresses, wishlist, reviews (verified buyers only, admin moderates — FR-13).
9. **Content & Forms:** Blogs, static/policy pages, FAQ, StoreInfo (single row, one branch), banners, PreOrder, TradeIn/Support/Corporate/Newsletter forms.
10. **EMI:** Banks/months/interest/min-price; calculator only in v1 (shown iff price ≥ minimum — FR-15).
11. **Ops:** DeliveryZone (charge by zone), Shipment (courier booking/tracking), Settings (site info, courier config, SMTP/SMS), AuditLog (who changed what), Dashboard aggregates.

---

## 5. Database approach

- **EF Core + SQL Server.** Code-first, migrations in Infrastructure. One DbContext (+ Identity tables). Conventions in `CONVENTIONS.md`.
- **Key types:** Money `decimal(18,2)` BDT (FR-01); `RowVersion` (byte[]) on `ProductVariant` (and `Order` where contended); UTC `DateTime` everywhere; slugs as `nvarchar(200)` unique.
- **Entities:** Exactly PRD §5 list. No extra tables in v1 (no loyalty, affiliate, multi-warehouse).
- **Indexes (minimum):** `Product(Slug)` unique; `Product(CategoryId, BrandId, IsActive)`; `Order(OrderNo)` unique; `Order(UserId, CreatedAt)`; `ProductVariant(Sku)` unique; plus `ProductSpec(ProductId, SpecFieldId)` unique, `Cart(UserId)`, `Cart(GuestToken)`, `Review(ProductId, IsApproved)`, `FlashSaleItem(VariantId)`, `AuditLog(Entity, EntityId, At)`.
- **Concurrency:** Stock decrement via `rowversion` retry (max 3) + reservation rows with expiry job. Never trust client prices — server recomputes (FR-02).
- **Slugs:** Immutable after publish; change creates 301 redirect row (`SlugRedirect(OldSlug, NewSlug)`) — small addition implied by FR-05, documented here to avoid silent 404s.
- **Seeding:** Admin user + roles, one `StoreInfo` row, `DeliveryZone` 3-zone flat default, demo category/brand/template, mock payment/courier toggles ON.
- **Backup:** Daily DB backup, restore tested (PRD NFR).

---

## 6. API boundaries

- **Base:** `/api/v1`, REST, JSON. Version in URL; breaking change → `/api/v2`, keep v1 read-only shim during transition.
- **Envelope (mandatory):** `{ hasError: bool, message: string, content: any }` for all responses, including errors via global exception middleware.
- **Areas (per PRD §6):** `home`, `products/categories/brands`, `search`, `cart`, `checkout/quote + orders`, `orders/track`, `payments/{gateway}/init+ipn`, `auth`, `me`, `blogs/store/pages/banners`, form posts, `/admin/**` (mirror CRUD, `[Authorize(Roles=...)]`).
- **Key contracts:**
  - `GET /home` — single aggregated payload (hero, categories, flash sale, sections, brands, blogs, trust badges, SEO block). Cached 60s, invalidated on admin change.
  - `GET /products` — filters/sort/pagination (`page, pageSize=24`, URL-state compatible). `GET /products/{slug}`, `GET /products/compare?slugs=a,b` (max 4, same category — FR-14).
  - `POST /checkout/quote` (price/delivery preview, no side effects) → `POST /orders` (Idempotency-Key header required).
  - `POST /payments/{gateway}/ipn` — public but signature-verified; idempotent handler.
  - Rate-limited: `auth/*`, `otp/*`, `search/suggest`.
- **Web ↔ Api:** Generated typed client (OpenAPI → Angular service). Interceptors: attach JWT, refresh-on-401 once + retry, error toast, loading spinner. No client price math beyond display.

---

## 7. AuthN / AuthZ

- Identity (PBKDF2 default) + JWT access (15 min, Authorization header) + opaque refresh (httpOnly, SameSite=Lax/Strict, secure in prod).
- Phone validation `^(?:\+?88)?01[3-9]\d{8}$` (FR-12) server-side; OTP for verification; lockout after 5 failed logins; password reset via OTP/short-lived token.
- Roles: `Admin` (all), `Staff` (orders, stock, shipments; denied: pricing, payment settings, user-roles), `Editor` (blogs, banners, FAQ, pages only), `Customer` (self scope). Guest checkout allowed; order tied to phone (FR-06).
- Admin audit: every `/admin/**` mutation writes `AuditLog`. CSRF-safe: JWT in header (not cookie) + SameSite refresh cookie.

---

## 8. Integrations (all behind interfaces; mock-first)

| Need | Abstraction | Phase 1 | Later swap |
|---|---|---|---|
| Payments | `IPaymentGateway { InitAsync, HandleIpnAsync, VerifySignature }` | Sandbox mock (fake hosted page + deterministic IPN) | Real SSLCommerz/bKash/Nagad impls, config toggle, no checkout code change |
| Courier | `ICourierService { QuoteAsync, BookAsync, TrackAsync }` | Mock (zone-flat charge, fake consignment/tracking) | Steadfast / Pathao / RedX (pick TBD — Open Question) |
| SMS/OTP | `ISmsSender` | Log-to-file + mock provider | BulkSMSBD / SSL Wireless / Alpha SMS |
| Email | `IEmailSender` | SMTP pickup / Mailtrap | SendGrid / Mailgun via SMTP |
| Images | `IImageStore` | Local disk | DO Spaces / S3 (same interface), ImageSharp → WebP + sizes |
| Maps | Embed only | Google embed / Leaflet+OSM iframe | — |
| Analytics | — | GA4 + Meta Pixel + GTM snippets via settings | — |

Payment rules enforced in Application layer: amount-match check, idempotent IPN (dedupe by `(Gateway, TrxId)`), order paid only after verified IPN.

---

## 9. Caching, jobs, observability

- **Cache:** `ICache` abstraction; Redis in prod/compose, `IMemoryCache` fallback v1. Keys: `home:payload`, `cat:tree`, `brands:list` — 60s TTL, explicit invalidation on admin write. No per-user data in shared cache.
- **Hangfire jobs:** flash-sale auto-revert (FR-04), reservation expiry (15 min), abandoned-cart email, order SMS/email fan-out, sitemap rebuild.
- **Logging:** Serilog → file + Seq; MediatR logging behavior (command, elapsed, user). Correlation ID header propagated.
- **Health:** `/health` (API), `/health/ready` (DB + cache + storage reachable). No PII in logs; no card data stored (gateway hosted page).

---

## 10. Frontend architecture notes (binding)

- Angular 18+ standalone + signals + `@angular/ssr`; Tailwind v4; no component library.
- One lazy route per feature; `@defer` below fold; `NgOptimizedImage` everywhere; SEO service sets title/meta/canonical/OG/JSON-LD (`Product` schema) per route; clean slugs `/product/{slug}`, compare `/product-compare/{slug1}-vs-{slug2}`.
- State: signals + services; NgRx only if a feature outgrows service state (decision logged if taken).
- SSR + edge cache for public pages; `curl`-testable HTML for SEO (verified pre-launch).

---

## 11. Architectural rules (non-negotiable)

1. Follow PRD; do not invent requirements or tables.
2. Thin controllers → MediatR → Domain. No business logic in Api/Web.
3. Server recomputes price/discount/delivery; client values are display-only (FR-02).
4. Stock changes are atomic (`rowversion`); reservation expiry returns stock.
5. Payment success only via verified IPN (FR-10); IPN handlers idempotent.
6. `POST /orders` requires Idempotency-Key; duplicate key returns original order, never a second order.
7. Slugs immutable after publish; rename → 301 (FR-05).
8. Specs only via templates; no free-form keys (FR-16); RAM/Storage on variant, mirrored into spec table view (FR-18).
9. Required spec fields block publish (FR-20); CSV import validates all rows, returns row-level errors, never partial-silent (FR-22).
10. Every admin mutation is audited; Staff/Editor scopes enforced server-side, not just UI-hidden.
11. No N+1: projections + `AsNoTracking` on reads; pagination everywhere; 24/page default on listing.
12. File uploads: whitelist type+size, rename, strip EXIF where feasible, no exec, served from storage/CDN not API process.

---

## 12. Open Questions (architecture-relevant; do not guess)

1. Courier partner (Steadfast / Pathao / RedX)? — Blocks real `ICourierService` impl; default 3-zone flat charge assumed.
2. Delivery charge rule: flat-per-zone vs weight-based? Default assumed: 3-zone flat (Gaibandha / Rangpur division / outside).
3. ~~Real Dazzle hex tokens (capture had Dark Reader on) — must re-capture clean before locking Tailwind theme~~ — **resolved 2026-10-13** (D-21): clean capture adopted in `doc/DESIGN-REFERENCE.md`, real tokens locked in `doc/DESIGN.md` §2.
4. Domain + hosting budget / Windows vs Linux target? — Affects compose vs IIS/Nginx choice.
5. EMI: calculator-only confirmed as default; real bank processing explicitly out.
6. Reviews / trade-in / Q&A / blog in v1 or v1.1? Default assumed: v1.1 (behind feature flags if stubbed).
7. Storage target v1: local disk vs Spaces/S3 from day one?
8. Redis from day one vs `IMemoryCache` single-instance v1? Default: abstraction + memory fallback, Redis in compose.

See also PRD §14. Decisions requiring approval are listed in the Step-1 summary.
