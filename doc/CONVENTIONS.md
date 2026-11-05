# CONVENTIONS.md — Rocky Telecom

> Source of truth for coding practices. Stack: .NET 10 + Angular 18+ SSR + SQL Server + Tailwind v4 (per PRD, no additions).

---

## 1. General

- PRD (`doc/rocky-telecom-prd.md`) decides *what*; ARCHITECTURE decides *structure*; this file decides *how code is written*.
- Preserve existing work; no rewrites without cause; no later-phase code while implementing current STATUS phase.
- English only (code, comments, URLs, commit messages). BDT money; UTC dates; BD phone regex everywhere.
- Every backend response uses envelope `{ hasError, message, content }` — including errors (via middleware). No bare arrays/primitives at top level.

## 2. Naming

| Area | Rule | Example |
|---|---|---|
| C# types/methods | PascalCase | `CheckoutQuoteCommand`, `OrderNo.Generate()` |
| C# locals/params | camelCase | `salePrice` |
| DB tables | Pascal plural (EF default); columns Pascal | `ProductVariants`, `RegularPrice` |
| API routes | kebab-plural, versioned | `/api/v1/products`, `/api/v1/admin/flash-sales` |
| Angular files | kebab-case + type suffix | `product-card.component.ts`, `price.pipe.ts` |
| Angular classes | PascalCase + suffix | `ProductCardComponent`, `CartService` |
| Signals/stores | `<noun>State` / `<noun>Store` | `cartState` |
| Slugs | lowercase-hyphen, immutable after publish | `iphone-15-pro-max-256gb` |
| SKUs | `RT-{BRAND}-{MODEL}-{VARIANT}` uppercase | `RT-APPLE-IP15PM-TB256` |
| OrderNo | `RT-YYMMDD-XXXX` (sequence per day) | `RT-260929-0042` |

## 3. Project / folder conventions

- Backend: `RockyTelecom.{Domain,Application,Infrastructure,Api}`; features grouped by module (`Catalog/`, `Orders/`, `Payments/`, …) holding `Command/Query + Handler + Validator + Dto` together (vertical slices).
- Frontend: `core/ shared/ layout/ features/<feature>/ admin/<module>/`; one lazy route per feature; shared components only if used ≥2 places.
- No cross-imports violating `Api → Application → Domain`; `Infrastructure → Application/Domain`. Frontend `features/*` never import from another feature — share via `shared/` or `core/`.

## 4. Backend (.NET)

- **CQRS:** One MediatR `IRequest<TResponse>` per use case; handler in Application; controller only maps HTTP → command/query → `Send()` → envelope. No logic in controllers.
- **DTOs:** Suffix `Dto` (`ProductDetailDto`); entities never returned directly. Reads use projections + `AsNoTracking`.
- **Validation:** FluentValidation per command/query (phone regex `^(?:\+?88)?01[3-9]\d{8}$`, price ≥ 0, qty ≥ 1, slug pattern). Pipeline `ValidationBehavior` short-circuits → envelope error. Never rely on client validation.
- **Money:** `decimal(18,2)`; compute server-side only (FR-02); display formatted by frontend `price` pipe (BD lakh `2,09,990`).
- **EF Core:** Fluent config per entity in Infrastructure (`ProductConfig` etc.); migrations only from Infrastructure; UTC `DateTime`; `RowVersion` on `ProductVariant`; indexes per ARCHITECTURE §5; no lazy loading; always paginate (`page, pageSize ≤ 100`, listing default 24).
- **Auth:** `[Authorize(Roles="Admin,Staff")]` etc. on `/admin/**`; Staff/Editor scopes enforced server-side (never UI-only). JWT in `Authorization: Bearer`; refresh in httpOnly SameSite cookie.
- **Idempotency:** `POST /orders` reads `Idempotency-Key` header; duplicate key returns original order (200, not new). IPN handlers dedupe `(Gateway, TrxId)`.
- **Errors:** Throw domain exceptions (`NotFoundException`, `ValidationException`, `ConflictException`) → middleware maps to `{ hasError: true, message, content: null }` + correct HTTP code. No stack traces to client. Log with correlation ID (Serilog → file + Seq).
- **Jobs:** Hangfire methods idempotent + retry-safe; flash-sale revert, reservation expiry (15 min), abandoned-cart, SMS fan-out.
- **Files:** Whitelist extensions (`.jpg .jpeg .png .webp`) + max size (config); rename to GUID; ImageSharp → WebP + thumb/medium/large; serve via `IImageStore`, never from API process folder.

## 5. Frontend (Angular)

- Standalone components; `signal()`/`computed()` state in services; `async` pipe preferred; no manual subscriptions without `takeUntilDestroyed`.
- Interceptors order: `auth → refresh-on-401 (single retry) → error-toast → loading`. 401 triggers one refresh then retry; second 401 → logout + redirect `/login`.
- Forms: Reactive Forms + explicit validators mirroring backend (phone, required, min); server errors shown per-field when `content` carries field map.
- Images: `NgOptimizedImage` (`ngSrc`, `width/height`, `priority` only for hero/LCP); lazy below fold; `srcset` via stored sizes.
- SEO service per route: `title, meta description, canonical, OG, JSON-LD`; slugs in URL (`/product/{slug}`, `/product-compare/{a}-vs-{b}`); SSR must `curl`-render without JS.
- Styling: Tailwind v4 utilities + CSS vars from theme tokens; font Urbanist; radii 8/10/12/15/pill; motion 150/200/300ms `cubic-bezier(0.4,0.2,1)` + `prefers-reduced-motion` guard; breakpoints 640/768/1024/1280/1536; container max ~1420px.
- Responsive (mobile-first, per FB-01 approval): base styles target mobile (360–430px); scale up via `sm:`/`md:`/`lg:` — never desktop-first `max-*` except for mobile-nav hide/show. Product grid 2-col on mobile, 3–4+ on desktop; filters become bottom-sheet/drawer on mobile; product gallery swipeable with dots; tables (compare, orders) become stacked cards or horizontal scroll with sticky first column on mobile.
- Touch & mobile nav: tap targets ≥44×44px with ≥8px gaps; mobile bottom nav (Home, Offer, Category, Pre-order, Profile) fixed with `env(safe-area-inset-bottom)` padding, active-state highlight, badge counts; header condenses to logo + search icon + cart on mobile; top-strip marquee shortens, hotline becomes tap-to-call link.
- Loading/error: skeleton `shimmer` for cards/lists; explicit empty states; toasts for mutations; no blocking spinners on SSR first paint.
- No client price math beyond display; quote/order totals always from `POST /checkout/quote` and order response.

## 6. API conventions

- `GET` list: `?page=&pageSize=&sort=&brand=&minPrice=&maxPrice=&ram=&storage=&color=&availability=&rating=&offer=&q=`; response `content: { items[], total, page, pageSize }` inside envelope.
- `POST /orders`: requires `Idempotency-Key` header; `201` on create, `200` with original on replay.
- Errors: `400` validation (message + field map in `content`), `401` unauth, `403` forbidden, `404` not found, `409` conflict (e.g. stock), `429` rate-limited. All still enveloped.
- Rate limits: `auth/*`, `otp/*`, `search/suggest` (fixed window, documented in Swagger 429 response).
- Swagger: every controller documented with response envelope schema + auth requirement.

## 7. Database conventions

- PK `Id` (int identity, except Identity string GUIDs); FK `<Entity>Id`; audit fields `CreatedAt` (+ `UpdatedAt` where mutated); soft-delete only where PRD implies (none in v1 — use `IsActive` flags).
- Unique: `Product.Slug`, `ProductVariant.Sku`, `Order.OrderNo`, `ProductSpec(ProductId, SpecFieldId)`.
- Spec storage: `ProductSpec(Value nvarchar, NumericValue decimal nullable)`; `NumericValue` required when field `IsFilterable` and DataType Number.
- Seed via `HasData`/migration seed only for roles/zones/store-info/templates skeleton; product data via admin/CSV, never seed.
- Daily backup; migration names `YYYYMMDD_<Area>_<Change>`; never edit applied migration — add new one.

## 8. Testing

- Backend: xUnit + FluentAssertions; unit (domain rules, order-no, coupon math, idempotency) + integration (EF InMemory/SQLite for handlers, WebApplicationFactory for API envelope/auth). Every FR rule gets a test (FR-02…FR-22).
- Frontend: Vitest/Jest unit for pipes/services/guards; Playwright E2E for guest→cart→checkout, login-merge, tracking, admin publish→storefront-visible. E2E required when browser workflow touched.
- Thresholds before phase sign-off: build green, all tests green, no new lint errors, E2E for touched journeys green.

## 9. Security

- HTTPS/HSTS/CSP in prod; secure cookies; lockout after 5 fails; OTP short TTL + attempt cap; password via Identity hasher only.
- All admin writes → `AuditLog(Entity, EntityId, Action, Before, After, At, UserId)`; PII minimal; no card data (gateway hosted page); IPN signature + amount-match verified.
- Parameterized queries only (EF, no raw SQL with interpolation); upload whitelist; `robots.txt`/sitemap without leaking admin routes; OWASP Top-10 check in Phase 7.

## 10. Git / CI

- Branches: `main` (prod), `develop`, `feat/<phase>-<slug>`; PR per phase-task with STATUS checkbox; squash merge; commit `<type>(<scope>): <subject>` (types: feat, fix, docs, chore, test, refactor).
- CI (Actions): `dotnet build+test`, `ng build+test`, Playwright on E2E-tagged PRs; block merge on red. Secrets in env/user-secrets only.
