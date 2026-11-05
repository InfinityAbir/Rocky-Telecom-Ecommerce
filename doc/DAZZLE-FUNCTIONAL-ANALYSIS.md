# Dazzle (dazzle.com.bd) — functional analysis vs Rocky Telecom

> Source: REA browser capture of the public site on 2026-11-05 (home, product detail, cart, login, brands, category).
> Scope: **behavior and structure only.** Per PRD §14, Rocky uses its own logo, copy, images and colors; nothing from Dazzle's content/assets is copied.
> Observed vs inferred: routes, endpoints and JSON field names are observed. Backend internals, auth flow after login, checkout, and payment are **not observed** (needs a logged-in/ordering session).

## 1. Stack (observed)

| Layer | Dazzle | Rocky (current) |
|---|---|---|
| Frontend | Next.js App Router (RSC `?_rsc=` prefetch, `/_next/image`) | Angular 18 + SSR |
| API shape | Same-origin BFF: `/api/proxy/*` forwards to backend, envelope `{statusCode,status,found,count,totalCount,page,limit,totalPages,data}` | `/api/v1/*`, envelope `{hasError,message,content}` |
| Search | Typesense-style response (`found`, `hits[].document`, `text_match`, `search_time_ms`, `request_params.collection_name`) via `/api/proxy/trending/search` | `/search`, `/search/suggest` (SQL) |
| Media | DigitalOcean Spaces CDN, Cloudflare in front | local disk `IImageStore` |
| Third party | GTM, GA, Facebook pixel, TikTok pixel, Tawk.to live chat, Cloudflare RUM | none yet |
| Theme | light/dark toggle in header | not present |

## 2. Public routes (observed)

`/` · `/categories` · `/categories/{slug}` (phones, tablet, laptop, smart-watch, gadget, accessories, sounds, smart-tv, home-appliance, monitor) · `/brands` · `/brands/{slug}` · `/product/{slug}` · `/product-compare/{slug}` · `/cart` · `/auth/login` · `/auth/forget-password` · `/offer` · `/offer/{campaign-slug}` · `/pre-order` · `/online-exclusive` · `/shop-location` · `/shop-location/{store-slug}` · `/blogs` · `/emi-policy` · `/delivery-policy` · `/shipping-policy`

Product slugs end `-price-in-bangladesh` (SEO pattern).

## 3. Backend endpoints seen (all GET, public)

| Dazzle | Purpose | Key query params |
|---|---|---|
| `/api/proxy/site-settings` | store/global settings | – |
| `/api/proxy/news-scroll` | top ticker: `newsUUID,title,linkUrl,openNewTab` | – |
| `/api/proxy/products` | listing | `brandSlug, subCategorySlug, categorySlug?, page, limit, stockAssort=1` |
| `/api/proxy/showcase-items` | curated home sections (Flash Sale, Trending Now, Clip to Cart) | `showcaseSlug, page, limit` |
| `/api/proxy/trending/search` | trending + search dropdown | `page, perPage` (+ `q`) |
| `/api/proxy/product-specification/{productUuid}` | spec groups: `specGroupUuid, groupName, groupSlug, productSpecifications[]` | – |

Product card fields: `productUuid, productCode, productName, productSlug, productBadge, isTba, endOfLife, allowPreOrder, recognitionBadge, stockStatus, regularPrice, discountedPrice, disRate, thumbnails{mediaFileUrl}` (+ `clipInfo{clipThumbnail,clipUrl}` for video reels).

## 4. Page features (observed)

**Header:** top strip (blogs, EMI policy, phone, store locations) · logo · nav (Brand, Online Exclusive, Offer, Pre-Order) · search with recent + trending dropdown showing price/discount · login · cart · dark-theme toggle · "Explore all" category bar · scrolling news ticker.

**Home:** H1 SEO title · Categories grid · Flash Sale (product cards, Compare link, stock status) · Trending Now · Clip to Cart (short video reels → add to cart) · Shop by Brand · SEO text block.

**Listing:** brand/category/sub-category filtered product grid, paginated (`page/limit`, `totalPages`), stock-first sort (`stockAssort`).

**Product detail:**
- breadcrumb, gallery with thumbnail scroller, color swatches linked to images
- price: offer price + regular price + discount %, EMI "from X/month for 12 months" + "Explore financing"
- selectors: Color · RAM & Storage · Region/Variant (variant label string composed from selection)
- qty stepper, **Add to cart**, **Buy Now**, wishlist, compare
- delivery block: store pickup availability, home delivery, express 4h (Dhaka), standard 1–3 days; estimated delivery, purchase point (loyalty), minimum booking amount
- paid add-ons: warranty/care plans (checkbox, priced, coverage period)
- "Frequently Buy Together" (accessory quick-add) and "Buy More Save More" bundle
- action buttons: Check Availability · Exchange (trade-in) · EMI · Profit Meter · Replacement Calculator
- "N people viewing now" social proof
- order via WhatsApp / Messenger / Call
- tabs: Specification (grouped table) · Description (rich HTML, SEO long-form)

**Cart:** breadcrumb, `Shopping Cart (n)`, empty state, promo-code input (disabled until text), continue shopping.

**Login:** username = email **or** mobile, password with show/hide, forgot-password, "login with phone number" (OTP), LOG IN.

## 5. Gap analysis — Rocky Telecom today

Rocky already has: home, shop/search, product, cart, checkout, order tracking, login, account orders, admin (dashboard/orders/products), and backend for cart, wishlist, compare, brands, categories, search suggest, orders, payments (init/IPN), EMI plans, shipment, admin catalog/orders/settings/audit.

| Dazzle feature | Rocky status | Where |
|---|---|---|
| Category pages `/categories/{slug}` | missing route (only `/shop`) | FE: new route; BE `GET /categories/{slug}` exists |
| Brands index + brand page | missing route | BE `GET /brands` exists |
| Compare page | missing route | BE `GET /products/compare` exists |
| Wishlist UI + header badge | no UI | BE `/wishlist` exists |
| Forgot / OTP login UI | auth.component only covers login/register? verify | BE `otp/*`, `password/reset` exist |
| Offer / campaign pages | missing | needs Campaign entity + endpoints |
| Showcases (Flash Sale, Trending, Clip to Cart) | missing | needs `Showcase`+`ShowcaseItem`; home payload has sections |
| News ticker | missing | needs `NewsTicker` entity + `GET /news` |
| Pre-order list + capture | missing | PRD Phase 6.4 |
| Online-exclusive | missing | flag on product / collection |
| Store locations + store detail | missing | PRD 6.4 |
| Blogs | missing | PRD 6.4 |
| Policy pages (EMI, delivery, shipping…) | missing | PRD 6.5, static CMS pages |
| Spec groups endpoint | exists as `/admin/products/{id}/specs`; public is inside `products/{slug}` | fine |
| Variant selectors (color / storage / region) | PRD has VariantOption — confirm UI | product.component |
| Paid add-ons (care plans) | missing | new `ProductAddon` concept |
| Frequently bought together / bundle | missing | new relation + cart bundle pricing |
| Trade-in "Exchange" | PRD v1.1 cut-order item | form + admin queue |
| Check availability / store pickup | partial (pickup delivery type exists) | per-store stock |
| Live viewers counter | missing | optional; needs realtime or fake-free source |
| WhatsApp / Messenger / Call | missing | trivial links from StoreInfo |
| Dark theme toggle | missing | Tailwind tokens |
| Promo code in cart | BE coupon preview exists | wire FE |
| Search dropdown: recent + trending | partial | add trending endpoint |
| Analytics pixels, live chat | missing | config-driven script slots, consent banner |
| `/api/proxy` BFF | not needed — Angular SSR already same-origin via nginx | skip |

Doc drift: `doc/STATUS.md` still says all phases "Not Started" while the solution has implemented controllers/features. Update it before planning.

## 6. Suggested build order (functional parity, own design)

1. Routing parity: `/categories/:slug`, `/brands`, `/brands/:slug`, `/product-compare/:slugs`, `/wishlist`, forgot-password, policy pages (static content from admin).
2. Home depth: showcases (flash sale w/ countdown, trending, newest), news ticker, brand strip, category grid; add `GET /showcases/{slug}` + `GET /news`.
3. Product depth: variant selectors verified, add-ons (care plans), frequently-bought-together, bundle, EMI per-month display, delivery estimate block, WhatsApp/Messenger/Call buttons, spec tab grouped.
4. Search: trending + recent dropdown with price/discount.
5. Cart: promo code wired; Buy Now shortcut to checkout.
6. Campaigns/offers + pre-order + online-exclusive + store locations + blogs.
7. Exchange/trade-in form, availability check, dark theme, analytics slots.

Not recommended to replicate: `/api/proxy` BFF layer, live-viewers number (fake social proof), copying Dazzle's copy, images, product text or care-plan names.

## 7. Deep dive — how each feature works

Evidence labels: **[observed]** = seen in network/UI during capture; **[code]** = read from Dazzle's shipped client JS; **[inferred]** = my reading, not directly confirmed.

### 7.1 Listing filters (category / brand pages) — fully decoded

State lives in the **URL query string**; every change re-requests the API. [observed + code]

| UI control | URL param | API param (`/api/proxy/products`) |
|---|---|---|
| category (path) | `/categories/{slug}` | `categorySlug` |
| sub-category | – | `subCategorySlug` |
| brand pills (All + A–Z brands of that category) | `brand=apple` | `brandSlug` |
| budget range slider (min/max from facets) | `minDiscountedPrice`, `maxDiscountedPrice` | same names |
| stock status (In Stock / Out of Stock) | `stockStatus=1` | `stockStatus` |
| Color, RAM & Storage (multi-select checkboxes) | `attributes=guid1,guid2` | `attributes` (comma-joined attribute GUIDs) |
| sort | UI values `newest`, `price_asc`, `price_desc` | `latest=1` / `discountedPrice=low-to-high` / `discountedPrice=high-to-low` |
| search text | `search` | `search` |
| paging | `page` | `page`, `limit=12` |
| always | – | `stockAssort=1` (in-stock products first) |

**Facet endpoint:** `GET /api/proxy/products/attributes?categorySlug=&brandSlug=&stockStatus=&stockAssort=` returns
`data:[{attributeName:"Color", items:[{attributeGuid, attributeVariation}]}, {attributeName:"RAM & Storage", ...}]` plus `priceData:{regularPrice:{minPrice,maxPrice}, discountedPrice:{minPrice,maxPrice}}`.
Facets are **dependent**: re-fetched after brand/stock changes, so options only show what exists for the current selection. Client merges groups case-insensitively (`"RAM & Storage"` and `"Ram & storage"` collapse into one) and dedupes by GUID. [code]

Also on brand select: `GET /api/proxy/banner-escalate/{brand}?brand=1` (brand banners) and `GET /api/proxy/showcase-escalate/{brand}?categorySlug=` (`topSelling[]`, `trending[]` carousels for that brand). Page 2 is **prefetched** automatically (`page=2` fires right after page 1). Response carries `totalCount, totalPages`.

Category page layout: breadcrumb → brand pills → filter sidebar (Budgets, Stock Status, Color, RAM & Storage) → banners → "Top Selling" and "Trending" carousels (badge %, label such as Hot Product / Top Selling / High Demand, wishlist, compare, quick view, add to cart) → grid.

### 7.2 Product page

- Data: SSR HTML with the full variant tree embedded; **switching color/storage/region makes no API call** (observed: zero requests after clicking 512GB) → pure client state. [observed]
- Selected variant composes a label: `iPhone 17 Pro Max Cosmic Orange 512GB JP/MEA (Dual e-Sim)` and updates price (256GB 149,490 → 512GB 186,990). [observed]
- Specs: `GET /api/proxy/product-specification/{productUuid}` → groups (Body, Display, Platform, Memory, Camera, Battery…) each with `productSpecifications[]`. Spec tab is rendered from this; description tab is rich HTML from CMS.
- Related list: `GET /products?subCategorySlug=…&stockAssort=1` (same sub-category).
- **Add to cart:** `POST /api/proxy/api/tokenized/v1/verify-order-product` body `{productUuid, variantUuid}` → `{statusCode:200,status:"success",message:"Validation successful."}`. Server only validates that the product+variant is orderable (stock/active); price is not sent. [observed]
- Extra blocks: delivery options (store pickup w/ availability modal, home delivery, express 4h Dhaka, standard 1–3 days), EMI (monthly amount shown, "begin at X per month"), care-plan add-ons (checkbox, price, coverage), Frequently-Buy-Together quick add, "Buy More Save More" bundle, minimum booking amount (৳15,000), purchase points, Check Availability / Exchange / EMI / Profit Meter / Replacement Calculator modals, WhatsApp/Messenger/Call, "N people viewing". Modal contents **not captured**.

### 7.3 Cart

- Cart is **client-side for guests** [inferred: no cart API calls observed; page rendered line + qty + totals right after reload of a new navigation]. Line: variant label, unit price, stock status, − qty +, remove.
- Summary: Subtotal, Total; inputs **Promo code** and **Gift Voucher** (APPLY disabled until text). Validation endpoints not observed (likely need login).
- CTA: Continue Shopping, Checkout.

### 7.4 Checkout

- Requires login: banner "Login Required – You must log in to place an order" with LOG IN NOW. [observed]
- Steps shown: 1 Delivery Method (Home Delivery / Store Pickup) → 2 Delivery Address (Existing Address (n) / New Address: Full Name*, Mobile*, Email optional, Full Address*, District* dropdown, Police Station/Area* dropdown enabled after district) → 3 Delivery Service & Payment (delivery services list appears after district+area; warning shown until then) → 4 Payment Method → 5 Payment Gateway (bKash, SSLCommerz buttons) → remarks → 6 Order summary (lines, qty stepper, subtotal, delivery fee, total, **Booking amount ৳15,000**) → T&C checkbox (Terms, Delivery, Refund, Cancellation) → `CONFIRM ORDER — ৳total`. "Secured by SSL Encryption".
- District/area/delivery-service/order-create endpoints are behind login → **not observed**.

### 7.5 Compare

`/product-compare/{slug}`: up to **6 products**, counter "1/6", per-product Regular + Special price, remove button, "Add product…" typeahead, full spec table. Same-category enforcement not verified.

### 7.6 Search

- Header box: opens dropdown with Recent Searches (localStorage, "No recent searches") + Trending Searches (10 products from `/api/proxy/trending/search?page=1&perPage=10`, fetched once on load). **Typing did not trigger a request within 2.5 s** → no live suggest observed; Enter navigates to `/search?q=iphone 17`. [observed]
- Results page: "Results for “q”", "N products found", cards with discount %, name, offer/regular price, category label. Backed by the Typesense-style index (`found, hits, out_of, search_time_ms`). 46 results for "iphone 17" mix phones and accessories.

### 7.7 Home sections (data sources)

News ticker `news-scroll` · Categories grid · Flash Sale + Trending Now = `showcase-items?showcaseSlug=` (admin-curated showcases, supports `clip-to-cart` with `clipInfo.clipUrl` mp4 + thumbnail) · Shop by Brand (`products?brandSlug=apple&limit=12`).

### 7.8 Footer & policy pages

Newsletter subscribe · branches (2 in Jamuna Future Park, Dhaka) · social links · app-store link · Company: About, Career, Our Brand, Blogs, Press Coverage, Order Tracking, Trade In, Product Disclaimer, Membership, Pre-Order policy · Help: FAQ, Support System, Announcement, Corporate, Feedback, Sitemap, Affiliate, Cookies, Data Protection, Loyalty policy · Legal: Terms, Refund, Privacy, Warranty, Exchange, Delivery, EMI, Cancellation, Newsletter.

### 7.9 Cross-cutting

Currency formatting: header/search `৳52,490`, product `BDT 149,490`, India-style grouping on cards (`1,49,490`). Discount badge = `disRate`. Product badge emoji strings from admin. Dark mode via Tailwind `dark:` classes. Images via `/_next/image` optimizer.

### 7.10 How products get added (admin) — reconstructed, NOT observed

Dazzle's admin panel is behind a login and was not accessed. Below is the data model implied by the public API and pages. It tells you what their admin form must collect. All of it is **inferred**.

| Group | Fields implied by public data |
|---|---|
| Identity | `productUuid`, `productCode` (e.g. `AI1PM-CC46C3`: short brand/model prefix + hash), `productName`, `productSlug` (name + `-price-in-bangladesh`, editable: some slugs differ) |
| Taxonomy | category → sub-category (e.g. Phones → iPhone), brand |
| Pricing | `regularPrice`, `discountedPrice`; `disRate` is computed server-side (2 decimals) |
| Flags | `productBadge` (free text with emoji: "🌟 New Arrival", "🛍️ Top Selling"), `isTba`, `endOfLife`, `allowPreOrder`; `recognitionBadge` ("Offer Running", "Few Left Only!") looks computed from offer/stock |
| Stock | per-variant stock → `stockStatus` "In Stock" / "" / out; `stockAssort` sorts in-stock first |
| Media | gallery images + per-colour image sets, thumbnail (`thumbnails.fileUuid`, `mediaFileUrl`), optional clip video + clip thumbnail; uploaded to object storage as `YYYY-MM/<name>-<uuid>.<ext>` |
| Variants | one `variantUuid` per combination of attributes (Color × RAM & Storage × Region/Variant); each has its own price and stock; label auto-composed |
| Attributes | global per category: attribute group (`Color`, `RAM & Storage`) → options (`attributeGuid`, `attributeVariation`). These same records power listing facets |
| Specs | spec groups (Body, Display, Platform, Memory, Main Camera, Selfie camera, Sound, Comms, Battery, Features) → rows `{specUuid, specification, specificationValue}` |
| Content | rich-text description (SEO headings, long-form), short highlight bullets, SEO title/meta |
| Commerce extras | minimum booking amount, estimated delivery days, purchase points, EMI eligibility, care-plan add-ons (name, price, coverage), frequently-bought-together set, bundle offer |
| Merchandising | membership in showcases (`flash-sale`, `trending-now-newest`, `clip-to-cart`), brand banners, ticker news, campaigns/offers |
| Search | indexed into the search engine (fields: brand, category, price, stock, active, featured, `is_free_shipping`) |

**Rocky comparison:** your `Product`, `ProductVariant`, `VariantOption`, `SpecTemplate/SpecGroup/SpecField/ProductSpec`, `ProductImage` already match the core. Missing vs Dazzle: product badge text, TBA / end-of-life flags, booking amount, add-ons, bundles, showcases, per-colour image sets, clip video. Admin endpoints for create/update/publish/duplicate/specs/images already exist in `AdminCatalogControllers.cs`.

## 8. Still NOT analyzed (blocked by login or by safety)

I did not and will not create accounts, log in with real credentials, or place/submit orders on a live store. So these remain unknown: register/OTP/forgot-password flows, account area (profile, addresses, orders, wishlist, reviews), order creation payload and statuses, district/area/delivery-service APIs, coupon/voucher validation, bKash/SSLCommerz redirect/IPN handling, trade-in/pre-order/corporate/feedback form submissions, newsletter subscribe, quick-view modal data, EMI/profit-meter/replacement-calculator logic, offer campaign pages, store-location pages, blog pages, admin panel (not public).
If you want these, options: (a) you give me a test account on a store you control, (b) I capture the remaining read-only public pages (offer, pre-order, online-exclusive, shop-location, blogs, policies), (c) I design them from your PRD instead of mirroring Dazzle.
