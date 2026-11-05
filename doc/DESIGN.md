# DESIGN.md — Rocky Telecom UI/UX specification

> Source of truth for UI/UX structure and system behaviour. Requirements: `doc/rocky-telecom-prd.md` v1.1 (§3.1, §8, §9). Structure: `doc/ARCHITECTURE.md` (§10). Code rules: `doc/CONVENTIONS.md` (§5).
> Visual tokens were captured from the reference site and are preserved verbatim in **`doc/DESIGN-REFERENCE.md`** (raw Dazzle measurement, not a spec). This file is the authored spec that consumes those tokens.
> Mobile behaviour is binding per FB-01 (see `doc/FEEDBACK.md`).

---

## 1. Design principles

1. **Mobile-first.** Base CSS targets 360–430px; scale up with `sm:`/`md:`/`lg:`. Desktop-first `max-*` is allowed only to hide/show the mobile bottom nav.
2. **Price is the hero.** Every product surface leads with price (BD lakh format) and stock status.
3. **Template-driven honesty.** Specs shown on the product page come only from the category `SpecTemplate`; empty optional fields are hidden, never rendered blank (FR-20).
4. **No layout shift, no blocking.** Skeletons for lists, `NgOptimizedImage` with explicit width/height, SSR first paint must not depend on JS.
5. **Calm, high-contrast shell.** Dazzle-like warm-neutral theme, Urbanist UI type, no component library, no decorative noise.
6. **Every state designed.** Loading, empty, error, offline, and permission-denied states are specified per screen, not improvised.
7. **Accessible by default.** WCAG AA contrast, keyboard-reachable, visible focus, 44px tap targets, `prefers-reduced-motion` honoured.

---

## 2. Theme tokens

Derived from the clean re-capture in `doc/DESIGN-REFERENCE.md` (455 elements, Dark Reader overlay no longer applied) + PRD §8. These are real observed values; the earlier placeholder hexes are superseded (see D-21).

**Shell is dark, content is light.** The reference site uses a dark brown/wood shell (top strip, header, mega menu, footer) above a light content body. The previous draft wrongly assumed a light header — corrected here.

### 2.1 Colour — shell (dark)

| Token | Value | Use |
|---|---|---|
| `--shell-bg` | `#1A1A1A` | Header/footer base behind the wood texture |
| `--shell-surface` | `#2E2B28` | Search field, inputs, cards on dark |
| `--shell-surface-2` | `#393430` | Raised/secondary dark surface |
| `--shell-border` | `rgb(255 255 255 / 10%)` | Hairlines on dark |
| `--on-shell` | `#FFFFFF` | Primary text/icon on dark |
| `--on-shell-muted` | `#D1D5DC` | Secondary text on dark |
| `--on-shell-subtle` | `#EDEDED` | Tertiary text/labels on dark |

### 2.2 Colour — brand & accents

| Token | Value | Use |
|---|---|---|
| `--brand` | `#965C20` | Brand brown (logo lockup, accents) |
| `--brand-deep` | `#6D3F0E` | High-contrast brown text/icon on light |
| `--brand-strong` | `#693B0C` | Pressed/hover brand, wood gradient stop |
| `--gold` | `#D4A97A` | Primary CTA surface (pairs with `--ink` text) |
| `--gold-soft` | `#E9CCAE` | Warm chip/track fills (`rgba(233,204,174,.28/.48)`) |
| `--gold-border` | `#DEB475` | Warm border (lighter variant `#FFD6A7`) |
| `--amber` | `#D4A017` | Flash-sale / urgency |
| `--amber-deep` | `#B57908` | Amber gradient stop |
| `--amber-bright` | `#F5C842` | Amber gradient highlight |
| `--wood-ramp` | `#693B0C → #965C20 → #CB843B → #E9CCAE` | Header/footer wood gradient ramp |

### 2.3 Colour — content (light)

| Token | Value | Use |
|---|---|---|
| `--bg` | `#F3F4F6` | Page background |
| `--bg-warm` | `#FFF7ED` | Warm section band |
| `--surface` | `#FFFFFF` | Cards, sheets, panels |
| `--border` | `#E5E7EB` | Neutral divider |
| `--ink` | `#101828` | Primary text on light / text on gold buttons |
| `--ink-black` | `#111213` | Near-black (`--lt-color-black`) |
| `--ink-soft` | `#2E2B28` | Body text on light |
| `--muted` | `#6A7282` | Secondary text (≥4.5:1 on `--surface`) |
| `--muted-2` | `#747474` | Tertiary text |
| `--muted-3` | `#99A1AF` | Placeholder text |
| `--danger` | `#FF7575` | Sale/discount chip, destructive |
| `--success` | `#03A000` | In stock, success text |
| `--success-deep` | `#087400` | Success surface |

Contrast: white on `--shell-surface` ≈ 15.2:1 ✔; `--ink` on `--surface` ≈ 16.8:1 ✔; `--muted` on `--surface` ≈ 4.8:1 ✔. `--gold`/`--amber` are never used for small text — they are fills that carry `--ink` text.

### 2.4 Typography

- Family: **Urbanist** (400/500/600/700), self-hosted `woff2`, `font-display: swap`. Georgia is permitted only for editorial blog headings.
- Scale: 12 / 14 / 15 / 16 / 18 / 20 / 24 / 30 / 38px.
- Line height: 1.5 body, 1.3 headings, 1.2 display.
- Display/heading weights 700; subheading 600; body 400; small 500.

### 2.5 Spacing, radius, elevation, motion

- Spacing scale: 4, 8, 12, 16, 20, 24, 32, 40, 50.
- Radius (observed): 8 (chips/tabs), 9 (primary button), 10 (search input), 12, 13, 15, 20, 999 (pill).
- Shadows (observed): resting `0 4px 6px -1px rgb(0 0 0 / 10%), 0 2px 4px -2px rgb(0 0 0 / 10%)`; warm glow `0 0 8px 4px rgb(233 204 174 / 32%)`; flash-sale glow `0 0 8px 0 rgb(212 160 23 / 55%)`.
- Motion (observed): 0.15s micro, 0.2s quick, 0.3s standard, 0.35s overlay; `cubic-bezier(0.4, 0, 0.2, 1)`. Keyframes: `border-move`, `marquee-rtl`, `shimmer`, `spin`, `pulse`, `bounce`. All guarded by `@media (prefers-reduced-motion: reduce)` → duration 0.01ms.
- Container: max-width 1420px; 16px mobile gutter, 50px desktop (`section` padding `0 50px` in the capture); `header`/`main` full-bleed at 1521px.
- Breakpoints: 640 / 768 / 1024 / 1280 / 1536 (observed as 40/48/64/80/96rem).

### 2.6 Signature components (from capture)

| Component | Spec |
|---|---|
| Primary button ("EXPLORE ALL") | bg `--gold`, color `--ink`, radius 9px, padding 14px 18px, 12.5px/700, letter-spacing 0.3125px, gap 8px |
| Tab / pill button ("Newest" / "Popular") | radius 8px, padding 8px 16px, 14px/700. Active = `rgba(233,204,174,.48)` fill + white text; inactive = `#F3F4F6` fill + `#364153` text |
| Search input | bg `--shell-surface`, radius 10px, 16px/400, padding `10px 20px 10px 44px` (44px left pad reserves the icon) |
| Newsletter input | transparent on dark, 14px/400, padding 16px 12px |

---

## 3. Application shell

### 3.1 Top strip (marquee)

- Dismissible bar above the header. **Dark shell** (`--shell-bg` over the wood gradient), `--on-shell` text. Content: scrolling offers (`marquee-rtl`), hotline as `tel:` tap-to-call, "Store location" link.
- Mobile: single line, shortened copy, no marquee animation when `prefers-reduced-motion`; dismiss persists for the session (`sessionStorage`).

### 3.2 Header

- Sticky, **dark shell** (`--shell-bg` + wood gradient) with `--shell-border` hairline and `--on-shell` text; the logo lockup and accents use `--gold`/`--brand`.
- Desktop (≥1024px): logo · category mega-menu trigger · search field · Brand / Offer / Pre-order links · wishlist · cart badge · profile.
- Mobile (<1024px): condenses to logo + search icon (opens full-screen search) + cart. Category nav lives in the drawer.
- Search field opens a suggest panel: products, brands, categories + recent (localStorage, max 8) + trending. Keyboard: `↑/↓` move, `Enter` open, `Esc` close. Debounce 250ms; empty query shows recent/trending only.
- Cart/profile badges are live via signals; badge count 0 renders nothing.

### 3.3 Mega menu / category nav

- Desktop: hover/click opens a panel with parent categories and sub-category columns; deep links to listing URLs.
- Mobile: bottom-sheet drawer with accordion levels and a back action per level.

### 3.4 Mobile bottom nav (binding)

Fixed bar, 5 items: **Home · Offer · Category · Pre-order · Profile**. Active item highlighted (`--color-accent`). Padding includes `env(safe-area-inset-bottom)`; content area reserves matching bottom padding so nothing is occluded. Hidden ≥1024px.

### 3.5 Footer

Showroom address + map link, social links, company/help/policy link columns, newsletter form, payment icons. **Dark shell**, same treatment as the header. Columns collapse to accordions on mobile.

---

## 4. Screen specifications

Each screen lists: purpose · layout · states · mobile deltas · API.

### 4.1 Home — SSR — `GET /api/v1/home`

- **Sections in order:** hero slider → category icon row → flash sale (countdown + Newest/Popular tabs) → Trending Now → promo banner → New Arrivals → Most Popular → Hot Deal of the Day → promo banner → Featured → Shop by Brand → Latest blogs (3) → trust badges → SEO text block (collapsed, "Read more").
- Below-fold sections use `@defer` (viewport) so first paint stays light.
- **States:** skeleton = header + hero shimmer + card shimmer rows. Empty section → section is omitted entirely (never an empty titled band). Error → section-level retry chip, rest of page still renders.
- **Mobile:** hero 1-up swipeable with dots; category icons horizontally scrollable; product rows horizontal scroll, 2-col grid for grids; flash-sale countdown stays visible on scroll within the section.

### 4.2 Catalog listing (category / brand / offer / search results) — SSR — `GET /api/v1/products`

- Layout: breadcrumb, title + result count, filter rail (desktop) / drawer (mobile), sort select, product grid 24/page, pagination (numbered, URL state).
- **Filters:** brand, price range, RAM, storage, colour, availability, rating, offer. Active filters render as removable chips; "Clear all" resets.
- **Sort:** newest, price low→high, price high→low, popularity, discount.
- **URL state is authoritative:** `?page&sort&brand&minPrice&maxPrice&ram&storage&color&availability&rating&offer` — reload/back/forward and share must reproduce the same list.
- **Product card:** image (1:1), discount % badge, condition badge (Hot/New/Top Selling/Official), stock status, sale price + struck regular price, Add to Cart, compare checkbox (max 4), wishlist heart.
- **States:** loading skeletons matching card size; empty → illustration + "no products match" + Clear filters; error → retry.
- **Mobile (FB-01):** 2-column card grid at 360–430px, no horizontal scroll; filters open as bottom-sheet/drawer with an apply button showing the result count.

### 4.3 Product detail — SSR — `GET /api/v1/products/{slug}`

- Gallery (zoom on desktop; swipeable with dots on mobile), title, brand, badges, stock, SKU.
- **Variant selector:** colour swatches, RAM, storage. Selecting a variant updates gallery, price, and stock without a full reload; unavailable combinations are disabled.
- Official / Unofficial / **Used** tag + warranty type and months.
- EMI calculator block (bank, months, monthly amount) — **shown only when price ≥ plan minimum** (FR-15).
- Delivery estimate by district (from `DeliveryZone`).
- Tabs: **Specs** (from template groups; RAM/storage read from selected variant — FR-18) · **Description** · **Reviews** (stub in Phase 1) · **Q&A** (stub in Phase 1).
- CTAs: Buy Now, Add to Cart, Pre-order (when `IsPreOrder`), Add to Compare, Share.
- Related products + recently viewed (localStorage).
- **SEO:** canonical to `/product/{slug}`, OG tags, JSON-LD `Product` with offers/price/availability/aggregateRating.
- **States:** variant switch shows inline price/stock shimmer; out-of-stock disables Buy Now and shows "Notify/Pre-order"; missing optional specs are hidden.
- **Mobile:** sticky bottom action bar (price + Add to Cart) above the bottom nav; gallery swipeable; spec tables become stacked label/value rows.

### 4.4 Compare — `/product-compare/{a}-vs-{b}`

- Up to 4 products, same category only (FR-14). Spec table with differing rows highlighted. Shareable URL; incompatible category → explanatory block with a link back.

### 4.5 Cart — guest localStorage + `GET/POST/PUT/DELETE /api/v1/cart`, `POST /cart/merge`

- Guest cart lives in localStorage and renders identically to the server cart.
- Line item: image, name, variant, unit price, qty stepper (≥1), remove, line total, per-line stock warning.
- Summary: subtotal, coupon field (preview only), delivery preview (zone picker), total. **No client price math beyond display** — totals come from `POST /checkout/quote` (Phase 3).
- **States:** qty change optimistic then reconciled with server; stock re-check on load and before checkout; empty → CTA back to catalog; coupon error inline.
- **Mobile:** summary collapses to a sticky bottom bar with expandable detail.

### 4.6 Checkout — Phase 3 (shell only in Phase 2)

- Stepper: Address → Delivery → Payment → Review. Guest checkout allowed (FR-06); address form validates BD phone server-side too.
- Defined here so Phase 2 cart can hand off cleanly; behaviour is implemented in Phase 3.

### 4.7 Order tracking — Phase 3 — `/order-tracking`

- Lookup by order no + phone; timeline Placed → Confirmed → Packed → Shipped → Delivered / Cancelled / Returned.

### 4.8 Account — Phase 2

- Register/login (phone + password, email optional), OTP verify, forgot/reset, profile, address book (BD validation), order-history shell, wishlist.

### 4.9 Admin SPA — Phase 5 (lazy `/admin`, role-guarded)

- Own layout (sidebar + topbar), detached from storefront chrome. Phase 1 delivers the catalog admin screens required to create template → product → variant → image → publish.

### 4.10 Content pages

Pre-order, Offers, Brands, Store location (map embed), Blogs, About, Career, FAQ, Support ticket, Corporate inquiry, Trade-in, Feedback, and policy pages (Terms, Privacy, Refund, Warranty, Exchange, Delivery, EMI, Cancellation, Cookies, Pre-order, Membership). Long-form pages use a constrained measure (~72ch); policy pages share one template driven by stored content.

---

## 5. Components (shared)

| Component | Behaviour |
|---|---|
| `product-card` | Grid + row variants; image, badges, price block, stock, quick actions |
| `price` pipe | BD lakh grouping (`209990` → `2,09,990`), `৳` prefix |
| `skeleton` | `shimmer` block sized to its target to avoid CLS |
| `rating` | Read-only stars + count; input mode for review form (Phase 6) |
| `modal` / `bottom-sheet` | Focus trap, `Esc` close, scroll lock, 300ms motion |
| `stepper` | Qty and checkout steps; min/max guards |
| `toast` | success/error/info; auto-dismiss 4s; `aria-live=polite` |
| `countdown` | Flash-sale; SSR renders a stable zero-state then hydrates |
| `badge`, `chip`, `breadcrumb`, `pagination`, `empty-state`, `error-retry` | Shared primitives |

---

## 6. Cross-cutting behaviour

### 6.1 Loading & errors

- Lists and cards use skeletons; mutations use toasts; navigations keep the shell (no full-page spinner).
- Envelope errors `{ hasError, message, content }` map to: field map → per-field errors; otherwise toast with `message`.
- `401` → interceptor refreshes once and retries; second `401` → logout + redirect `/login?returnUrl=`.
- `403` → permission screen; `404` → not-found page with search; `429` → "too many attempts, try later"; `409` (stock) → inline stock conflict on the affected line.

### 6.2 Forms

Reactive Forms with validators mirroring backend: required, phone `^(?:\+?88)?01[3-9]\d{8}$`, price ≥ 0, qty ≥ 1, slug pattern. Server-side field errors render under the matching field.

### 6.3 Images & SEO

`NgOptimizedImage` everywhere with `ngSrc`, `width`/`height`, `priority` only on the hero/LCP image, lazy below the fold, `srcset` from stored sizes. SEO service sets title, meta description, canonical, OG, and JSON-LD per route; slugs are clean (`/product/{slug}`). SSR output must be `curl`-verifiable without JS.

### 6.4 Accessibility

Landmarks (`header`/`nav`/`main`/`footer`), one `h1` per page, labelled inputs, `aria-live` for toasts and cart count, visible focus rings, skip-to-content link, alt text on all product imagery, keyboard-operable gallery/switcher/drawer.

### 6.5 Responsive acceptance (binding — FB-01)

Verified at 360 / 390 / 768 / 1024 / 1536px: no horizontal scroll; tap targets ≥44×44px with ≥8px gaps; bottom nav safe-area correct; checkout completable one-handed; mobile Playwright suite green; Lighthouse mobile ≥80 on home, listing, product.

---

## 7. Phase coverage map

| Phase | Screens delivered |
|---|---|
| 0 | Shell (top strip, header, footer, bottom-nav stubs), token theme, home stub, route skeletons, SEO/API client stubs |
| 1 | Home, catalog listing, product detail, compare, search suggest, admin catalog forms, SEO/sitemap |
| 2 | Auth screens, account/profile/addresses, cart, wishlist, header badges |
| 3 | Checkout, order success, order tracking, account order history |
| 4 | Payment gateway hosted-page handoff + IPN result screens |
| 5 | Admin SPA (dashboard, orders, offers, content, roles, settings, audit) |
| 6 | Compare depth, flash-sale UI, reviews, blogs, pre-order, forms, policy pages |
| 7 | A11y/perf pass, responsive acceptance matrix, final polish |

---

## 8. Open questions (do not guess)

1. ~~Real Dazzle hex values~~ — **resolved 2026-10-13**: clean re-capture adopted as `doc/DESIGN-REFERENCE.md`; real tokens locked in §2 (D-21).
2. Showroom opening hours, phone, email, social links — needed for the footer, top strip, and store page.
3. AI logo asset — header/footer currently use the "Rocky Telecom" text logo placeholder.
4. Courier partner and flat-per-zone vs weight charging — affects the delivery-estimate block copy.
