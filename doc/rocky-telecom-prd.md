# Rocky Telecom — Product Requirements Document

**Version:** 1.1 | **Date:** 29 Sep 2026 | **Status:** Requirements confirmed by client owner
**Stack:** ASP.NET Core Web API (.NET 10) + Angular (SSR) + SQL Server
**Reference:** dazzle.com.bd (layout, UX pattern, color theme; own logo, copy, images)
**Business:** Rocky Telecom, single showroom at Sadullapur, Gaibandha

---

## 1. Overview

Rocky Telecom = mobile and electronics showroom. Build full e-commerce site: browse, compare, buy online, track order, pay by EMI or local gateway. Site also promotes physical showroom (Sadullapur, Gaibandha; pickup).

### 1.1 Goals
- Sell phones, tablets, laptops, smartwatches, accessories online
- Rank on Google for "[model] price in Bangladesh" queries (SSR + clean slugs)
- Drive showroom footfall (store address + map, showroom pickup, pre-order)
- Give client admin panel to run catalog, offers, orders without dev

### 1.2 Confirmed Decisions
| Topic | Decision |
|---|---|
| Scope | Full e-commerce: cart, checkout, orders |
| Stack | ASP.NET Core Web API + Angular (SSR) |
| Catalog | All device types: official, unofficial, used |
| Product entry | Client enters all products via admin panel (plus CSV import) |
| Payments | COD, bKash, Nagad, SSLCommerz. Phase 1: SSLCommerz sandbox mock only. Real gateways later behind same interface. |
| Courier | Integrated, with delivery charge. Mock first, real API later. |
| Branches | One: Sadullapur, Gaibandha |
| Theme | Follow Dazzle color theme |
| Logo | AI generated later. Text logo "Rocky Telecom" as placeholder. |
| Language | English only v1 |

### 1.3 Non-goals (v1)
- Native mobile app
- Multi-vendor marketplace
- Multi-language (English only v1; Bangla later)
- Live chat, AI recommendation

### 1.4 Success metrics
| Metric | Target |
|---|---|
| Lighthouse mobile perf | ≥ 80 |
| Home page LCP | ≤ 2.5s on 4G |
| Cart → order conversion | ≥ 2% |
| Cart abandonment | ≤ 70% |
| Search-to-product click | ≥ 40% |
| API p95 latency | ≤ 400ms |

---

## 2. Users

| Role | Needs |
|---|---|
| Guest | Browse, search, compare, add to cart, checkout as guest |
| Customer | Account, saved addresses, order history, wishlist, tracking |
| Admin | Full control of catalog, orders, offers, content |
| Staff (sales) | Manage orders, stock. No pricing or user roles. |
| Content editor | Blogs, banners only |

---

## 3. Scope — Features

### 3.1 Storefront

**Global**
- Top strip: marquee offers (CSS `marquee-rtl`), hotline, store location link
- Header: logo, search with recent + trending + live suggest, Brand / Offer / Pre-order links, cart, profile
- Category nav with mega menu (sub-categories)
- Mobile bottom nav: Home, Offer, Category, Pre-order, Profile
- Footer: showroom address, social, company + help + policy links, newsletter, payment icons

**Home** (single `GET /api/home` call)
1. Hero banner slider (admin managed)
2. Category icon row
3. Flash sale + countdown, tabs Newest / Popular
4. Trending Now, New Arrivals, Most Popular, Hot Deal of the Day, Featured
5. Promo banners between sections
6. Shop by Brand strip
7. Latest blogs (3)
8. Trust badges: 100% genuine, fast delivery, EMI, replacement warranty, best price
9. SEO text block with internal links (collapsed, "Read more")

**Listing (category / brand / offer / search)**
- Filters: brand, price range, RAM, storage, color, availability, rating, offer
- Sort: newest, price low/high, popularity, discount
- Pagination (server side, 24/page) or infinite scroll with URL state
- Product card: image, discount %, badge (Hot / New / Top Selling / Official), stock status, price (sale + regular strike), Add to Cart, compare checkbox, wishlist

**Product detail**
- Gallery (zoom), title, brand, badges, stock, SKU
- Variant selector: color, storage, RAM. Price + stock update per variant.
- Official / Unofficial / Used tag with warranty type
- EMI calculator (bank, months, monthly amount)
- Delivery estimate by district
- Tabs: Specs, Description, Reviews, Q&A
- Related products, recently viewed
- Buy Now, Add to Cart, Pre-order (if flagged), Add to Compare, Share
- JSON-LD `Product` schema, OG tags

**Compare**
- Up to 4 products, spec table, highlight differences, shareable URL `/product-compare/{slug1}-vs-{slug2}`

**Search**
- Suggest endpoint (products, brands, categories), typo tolerance, recent searches (localStorage), trending (admin or computed)

**Cart**
- Guest cart in localStorage, merged into server cart on login
- Qty change, remove, coupon, delivery charge preview, stock re-check

**Checkout**
- Guest or logged in
- Address: name, phone (BD format), division / district / thana, address line
- Delivery: home delivery (courier, charge by zone) or showroom pickup (single fixed address: Sadullapur, Gaibandha)
- Payment: COD, bKash, Nagad, SSLCommerz (cards/bank/MFS). Phase 1 uses mock sandbox gateway. EMI shown as calculator only.
- Order summary, coupon, place order
- Order success page + SMS + email

**Account**
- Register / login (phone + password, email optional), OTP verify, forgot password
- Profile, addresses, order history, order detail, invoice PDF, wishlist, reviews

**Order tracking**
- `/order-tracking` by order no + phone. Timeline: Placed → Confirmed → Packed → Shipped → Delivered / Cancelled / Returned.

**Content pages**
- Pre-order, Offers, Brands, Store location (map), Blogs, About, Career, FAQ, Support ticket form, Corporate inquiry, Trade-in request form, Feedback
- Policy pages: Terms, Privacy, Refund, Warranty, Exchange, Delivery, EMI, Cancellation, Cookies, Pre-order, Membership

### 3.2 Admin panel (Angular, separate lazy module `/admin`, role guarded)

| Module | Functions |
|---|---|
| Dashboard | Sales today/week/month, orders by status, low stock, top products, chart |
| Spec Templates | Define per category: groups + fields (type, unit, required, filterable, comparable). Set once, reuse for every product. |
| Catalog | Client-operated. CRUD product, variants, specs, images (multi-upload, reorder), condition (new/used grade), warranty type, SEO fields, bulk import/export CSV (specs + variants), draft/publish, duplicate product, spec form auto-built from category template |
| Categories / Brands | CRUD, logo, order, parent-child |
| Inventory | Stock per variant, adjust log, low stock alert |
| Orders | List, filter, status change, print invoice, refund, notes |
| Offers | Flash sale (start/end, products, %), coupons, banners |
| Pre-orders | Requests list, convert to order |
| Customers | List, block, order history |
| Content | Banners, blogs, static pages, FAQ, store info |
| EMI | Banks, months, interest, min price |
| Support | Tickets, trade-in, corporate inquiries |
| Users & roles | Admin, staff, editor permissions |
| Settings | Site info, delivery charge by zone, courier config, payment toggles (mock/live), SMTP, SMS |
| Audit log | Who changed what |

---

## 4. Functional Requirements (key rules)

| ID | Rule |
|---|---|
| FR-01 | Price stored `decimal(18,2)` BDT. Display BD lakh format (`2,09,990`). |
| FR-02 | Server recomputes all prices, discounts, delivery at order create. Never trust client. |
| FR-03 | Stock decrement atomic with `rowversion` concurrency. Reserve stock 15 min at checkout start. |
| FR-04 | Flash sale price auto-reverts at end time (background job). |
| FR-05 | Slug unique, immutable after publish; changes create 301 redirect. |
| FR-06 | Guest checkout allowed. Order tied to phone. Account auto-suggested after. |
| FR-07 | Coupon: percent or fixed, min cart, max discount, usage limit, per-user limit, expiry. |
| FR-08 | Order number format `RT-YYMMDD-XXXX`. |
| FR-09 | Cancel allowed until status = Shipped. Stock returned on cancel. |
| FR-10 | Payment success only from gateway server callback (IPN), never from browser redirect. |
| FR-11 | Idempotency key on `POST /orders` to stop double submit. |
| FR-12 | Phone validation: `^(?:\+?88)?01[3-9]\d{8}$`. |
| FR-13 | Product review only by verified buyers; admin moderates. |
| FR-14 | Compare max 4, same category only. |
| FR-15 | EMI shown only if price ≥ bank minimum. |
| FR-16 | Specs are template-driven. Product form renders fields from its category `SpecTemplate`. No free-form keys, so compare and filters stay consistent. |
| FR-17 | Variant axes = Color, RAM, Storage (ROM), plus optional Region, SIM type. Each combination = one variant with own SKU, price, stock, images. |
| FR-18 | RAM and Storage live on variant, not in product specs. Spec table on product page reads them from the selected variant. |
| FR-19 | Filterable fields store `NumericValue` (GB, mAh, inch, MP) for range filter and sort. |
| FR-20 | Required spec fields block publish. Optional fields may be empty and are hidden on page. |
| FR-21 | Duplicate product copies specs, images, variants (stock 0). Client edits differences only. |
| FR-22 | CSV import validates against template, returns row-level error report, never partial silent import. |

---

## 5. Data Model

```
Category(Id, ParentId, Name, Slug, Icon, SortOrder, IsActive, SeoTitle, SeoDesc)
Brand(Id, Name, Slug, LogoUrl, IsActive)
Product(Id, Name, Slug, BrandId, CategoryId, ShortDesc, Description, Type[Official|Unofficial|Used], Condition[New|LikeNew|Good|Fair] (used only), WarrantyType,
        IsFeatured, IsPreOrder, IsActive, Badge, WarrantyMonths, SeoTitle, SeoDesc, CreatedAt)
ProductVariant(Id, ProductId, Sku, Color, ColorHex, Ram, Storage, Region?, SimType?, RegularPrice, SalePrice, Stock, RowVersion)
ProductImage(Id, ProductId, VariantId?, Url, SortOrder)
SpecTemplate(Id, CategoryId, Name)                          // one per category (Phones, Laptops...)
SpecGroup(Id, TemplateId, Name, SortOrder)                  // Display, Performance, Camera, Battery, Network, Design, Software
SpecField(Id, GroupId, Key, Label, DataType[Text|Number|Bool|Option], Unit, Options, IsRequired, IsFilterable, IsComparable, SortOrder)
ProductSpec(Id, ProductId, SpecFieldId, Value, NumericValue?, SortOrder)   // Value shown; NumericValue used for filter/sort
VariantOption(Id, CategoryId, Name[Color|RAM|Storage|Region|SimType], SortOrder)   // allowed variant axes per category
FlashSale(Id, Title, StartAt, EndAt, IsActive)
FlashSaleItem(Id, FlashSaleId, VariantId, SalePrice)
Coupon(Id, Code, Type, Value, MinCart, MaxDiscount, UsageLimit, PerUserLimit, StartAt, EndAt)
Banner(Id, Placement, ImageUrl, Link, SortOrder, StartAt, EndAt)
EmiPlan(Id, BankName, Months, InterestPct, MinPrice, IsActive)

User(Id, Name, Phone, Email, PasswordHash, IsPhoneVerified, IsBlocked, CreatedAt)   // ASP.NET Identity
Address(Id, UserId, Name, Phone, Division, District, Thana, Line, IsDefault)
Cart(Id, UserId?, GuestToken, UpdatedAt)
CartItem(Id, CartId, VariantId, Qty)
Wishlist(Id, UserId, ProductId)
Review(Id, ProductId, UserId, Rating, Body, IsApproved)

Order(Id, OrderNo, UserId?, GuestPhone, Status, SubTotal, Discount, DeliveryFee, Total,
      PaymentMethod, PaymentStatus, DeliveryType[Home|Pickup], AddressSnapshot, Note, IdempotencyKey, CreatedAt)
OrderItem(Id, OrderId, VariantId, NameSnapshot, PriceSnapshot, Qty)
OrderStatusHistory(Id, OrderId, Status, ChangedBy, At, Note)
Payment(Id, OrderId, Gateway, TrxId, Amount, Status, RawResponse, At)

StoreInfo(Id, Name, Address, Phone, MapLat, MapLng, OpeningHours)   // single row, one branch
DeliveryZone(Id, Name, Districts, Charge, EstimatedDays)
Shipment(Id, OrderId, Courier, ConsignmentId, TrackingCode, Status, Charge, At)
BlogPost(Id, Title, Slug, Cover, Body, PublishedAt, IsPublished)
PreOrder(Id, ProductId, Name, Phone, Note, Status)
TradeInRequest / SupportTicket / CorporateInquiry
AuditLog(Id, UserId, Entity, EntityId, Action, Before, After, At)
```

Indexes: `Product(Slug)` unique, `Product(CategoryId, BrandId, IsActive)`, `Order(OrderNo)` unique, `Order(UserId, CreatedAt)`, `ProductVariant(Sku)` unique.

---

## 6. API (REST, JSON, versioned `/api/v1`)

Response envelope: `{ hasError, message, content }` (matches your existing convention).

| Area | Endpoints |
|---|---|
| Home | `GET /home` |
| Catalog | `GET /products`, `GET /products/{slug}`, `GET /products/compare?slugs=`, `GET /categories`, `GET /brands` |
| Search | `GET /search/suggest?q=`, `GET /search/trending` |
| Cart | `GET/POST/PUT/DELETE /cart`, `POST /cart/merge` |
| Checkout | `POST /checkout/quote`, `POST /orders` |
| Orders | `GET /orders`, `GET /orders/{id}`, `POST /orders/{id}/cancel`, `GET /orders/track` |
| Payment | `POST /payments/{gateway}/init`, `POST /payments/{gateway}/ipn` |
| Auth | `POST /auth/register`, `/login`, `/refresh`, `/otp/send`, `/otp/verify`, `/forgot`, `/reset` |
| Account | `GET/PUT /me`, `/me/addresses`, `/me/wishlist` |
| Content | `GET /blogs`, `/blogs/{slug}`, `/store`, `/pages/{slug}`, `/banners` |
| Forms | `POST /pre-orders`, `/support`, `/trade-in`, `/corporate`, `/newsletter` |
| Admin | `/admin/**` mirror CRUD per module, `[Authorize(Roles=...)]` |

---

## 7. Architecture

```
RockyTelecom.sln
 ├─ RockyTelecom.Domain          // entities, enums, value objects
 ├─ RockyTelecom.Application     // CQRS (MediatR), DTOs, validators (FluentValidation), interfaces
 ├─ RockyTelecom.Infrastructure  // EF Core, Identity, payments, SMS, email, storage, jobs
 ├─ RockyTelecom.Api             // controllers, middleware, auth, swagger
 └─ RockyTelecom.Web             // Angular workspace
```

**Backend**
- .NET 10, EF Core, SQL Server, ASP.NET Identity + JWT (15 min) + refresh token (httpOnly cookie)
- MediatR pipeline: validation, logging, transaction
- Hangfire: flash sale end, stock reservation expiry, abandoned cart mail, order SMS
- Redis (or `IMemoryCache` v1): home payload, category tree, brand list, 60s TTL
- Serilog → file + Seq
- Rate limit on auth, OTP, search suggest
- Global exception middleware → envelope
- Image store: local disk or DigitalOcean Spaces / S3, resize to WebP (ImageSharp)

**Frontend**
- Angular 18+ standalone, signals, `@angular/ssr`, Tailwind v4 (no Angular Material)
- Structure:
```
src/app
 ├─ core/       (interceptors, auth, guards, api client, seo service)
 ├─ shared/     (product-card, price-pipe, skeleton, rating, modal)
 ├─ layout/     (header, footer, mobile-nav, top-strip)
 ├─ features/   (home, catalog, product, compare, cart, checkout, account, content)
 └─ admin/      (lazy, own layout)
```
- State: signals + services; NgRx only if needed
- HTTP interceptors: auth, refresh-on-401, error toast, loading
- Lazy route per feature, `@defer` for below-fold sections
- `NgOptimizedImage` for all product images
- SEO service: title, meta, canonical, OG, JSON-LD per route

---

## 8. Design System (from Dazzle reference)

Capture had Dark Reader on. Colors below = estimate. Verify with clean capture. Replace with Rocky Telecom brand once logo ready.

- **Font:** Urbanist (body, UI), Georgia optional for editorial headings
- **Type scale:** 12 / 14 / 15 / 16 / 18px; weights 400 / 500 / 600 / 700
- **Spacing:** 4, 8, 12, 16, 20, 24, 32, 40, 50
- **Radii:** 8, 10, 12, 15, pill 999
- **Motion:** 150 / 200 / 300ms, `cubic-bezier(0.4, 0, 0.2, 1)`; `shimmer` skeleton, `marquee` strip; respect `prefers-reduced-motion`
- **Breakpoints:** 640 / 768 / 1024 / 1280 / 1536
- **Container:** max ~1420px, 16px mobile gutter, 50px desktop
- **Tokens (placeholder):** brand `#965c20`, accent `#fe6901`, bg `#f9fafb`, surface `#fff`, border `#e8d8c0`, success `#00d294`
- **Components:** primary button (9px radius, 14/18 padding, 12.5px bold), pill chip, search input (44px left pad for icon), product card, badge, price block, countdown, stepper, toast

---

## 9. Non-Functional Requirements

**Performance**
- SSR + edge cache for public pages
- Images WebP, responsive `srcset`, lazy below fold
- DB: pagination everywhere, no N+1 (use projections, `AsNoTracking`)
- Home payload cached, invalidated on admin change

**Security**
- HTTPS, HSTS, CSP, secure cookies
- Password hash (Identity PBKDF2 / Argon2), lockout after 5 fails
- Input validation server side, parameterized queries (EF)
- CSRF safe (JWT header + SameSite cookie for refresh)
- Payment IPN signature verify, amount match check
- File upload: type + size whitelist, rename, no exec
- Role-based authorization + audit log on admin actions
- PII minimum; follow privacy policy; no card data stored (gateway hosted page)
- OWASP Top 10 review before launch

**Reliability**
- Daily DB backup, restore tested
- Health checks `/health`
- Idempotent order + payment handlers

**Accessibility / SEO**
- WCAG AA contrast, keyboard nav, alt text, aria labels
- Sitemap.xml auto, robots.txt, canonical, structured data, clean slugs `/product/{slug}`

**Browser:** last 2 versions Chrome, Edge, Safari, Firefox; iOS Safari, Android Chrome.

---

## 10. Integrations

| Need | Options |
|---|---|
| Payment | SSLCommerz (sandbox mock in phase 1), bKash, Nagad, COD. All behind `IPaymentGateway` so mock swaps to real with config only. |
| SMS / OTP | BulkSMSBD, SSL Wireless, Alpha SMS |
| Email | SMTP / SendGrid / Mailgun |
| Courier | Behind `ICourierService`. Candidates: Steadfast, Pathao, RedX. Mock first. Booking + tracking + charge. |
| Maps | Google Maps embed / Leaflet + OSM (free) |
| Analytics | GA4, Meta Pixel, GTM |
| Storage | DigitalOcean Spaces / S3 |

---

## 11. Delivery Plan

| Phase | Weeks | Deliverables |
|---|---|---|
| 0. Setup | 1 | Repo, CI, solution skeleton, DB, Angular workspace, tokens, seed data |
| 1. Catalog | 2–3 | Category/brand/product API, home, listing, detail, search, SEO |
| 2. Cart + Auth | 4 | Register/login/OTP, cart, guest merge, wishlist |
| 3. Checkout + Orders | 5–6 | Address, delivery, COD, order flow, tracking, invoice, emails/SMS |
| 4. Payments | 7 | SSLCommerz + bKash/Nagad, IPN, EMI calc |
| 5. Admin | 8–9 | Dashboard, catalog, orders, offers, content, roles |
| 6. Extras | 10 | Flash sale, compare, reviews, blog, pre-order, trade-in, support |
| 7. Harden + Launch | 11–12 | Security review, load test, SEO check, UAT, deploy, monitor |

Solo dev estimate: 12 weeks part-time may stretch to 16–20. Cut scope first: reviews, trade-in, Q&A, blog to v1.1.

---

## 12. Deployment

- Windows or Linux VPS, Nginx reverse proxy, Kestrel, Angular SSR via Node
- Or Docker Compose: `api`, `web-ssr`, `sql`, `redis`, `nginx`
- CI: GitHub Actions → build, test, push image, deploy
- Envs: dev, staging, prod. Secrets in env vars / user-secrets, never in repo.

---

## 13. Risks

| Risk | Mitigation |
|---|---|
| Scope too big for timeline | Phase gates, MVP = phases 0–4 + minimal admin |
| Payment gateway approval delay | Mock gateway in dev. Start merchant applications early. Launch with COD first. |
| Client enters own products, slow or messy data | Simple admin UX, CSV template, validation, draft state, training session |
| Overselling stock | Row version + reservation |
| SEO drop after SSR misconfig | Test with `curl` + Search Console before launch |
| Copyright on Dazzle assets | Own images, logo, copy, colors only |
| Price/stock data stale | Admin bulk edit + CSV update |

---

## 14. Open Questions (remaining)

1. Courier partner final pick (Steadfast, Pathao, RedX)?
2. Delivery charge rule: flat per zone (Gaibandha / Rangpur division / outside) or by weight? Default: 3-zone flat.
3. Domain name and hosting budget?
4. EMI: calculator only, or real bank processing? Default: calculator only.
5. Real hex values of Dazzle theme (capture had Dark Reader on). Verify before locking tokens.
6. Showroom opening hours, phone, email, social links?
7. Trade-in and reviews in v1 or later? Default: later.

Answered: name (Rocky Telecom), theme (Dazzle), logo (AI later), gateways, device types, product entry (client), courier (yes), branch (one, Sadullapur, Gaibandha), language (English).

---

## 14A. Product Specs and Variants

### Variant axes (change price / stock / image)
| Axis | Example |
|---|---|
| Color | Titanium Black, Blue, Silver (with swatch hex) |
| RAM | 8GB, 12GB |
| Storage (ROM) | 128GB, 256GB, 512GB |
| Region (optional) | Official BD, International |
| SIM type (optional) | Physical, eSIM, Dual |

### Spec groups: Phones (default template)
| Group | Fields |
|---|---|
| General | Release date, Status, Model number |
| Display | Type, Size (in), Resolution, Refresh rate (Hz), Brightness (nits), Protection |
| Performance | Chipset, CPU, GPU, OS, UI |
| Memory | Card slot (RAM and Storage come from variant) |
| Main Camera | Setup (MP), Video, Features |
| Selfie Camera | MP, Video |
| Battery | Capacity (mAh), Wired charging (W), Wireless charging, Reverse charging |
| Network | Technology, Bands, SIM, 5G, Wi-Fi, Bluetooth, NFC, USB |
| Design | Dimensions, Weight (g), Build, Colors, Water/dust rating |
| Sensors | Fingerprint, Face unlock, Others |
| In the box | Items |
| Warranty | Type (official / shop), Months |

Other categories get own template: Laptops (CPU, GPU, RAM, storage type, display, ports, battery), Tablets, Smart Watch, Sound, Smart TV, Monitor, Accessories (short template).

### Rules
- Client fills values only. Fields come from template.
- Fields marked filterable feed listing filters (RAM, Storage, Battery, Display size, Chipset brand, Price, Brand).
- Fields marked comparable feed compare table.
- Required minimum for publish: Display size, Chipset, Main camera, Battery, OS, at least one variant with price and stock, one image.
- No scraping or copying spec text from Dazzle, GSMArena, or other sites. Client writes or uses manufacturer facts in own words.
- Future option: licensed spec feed.

### Client effort savers
- Duplicate product
- CSV import/export
- Spec template prefill for same brand series
- Bulk price / stock update grid

---

## 15. Out of Scope v1 (Backlog)

Mobile app, Bangla UI, loyalty points, affiliate, live chat, AI recommend, multi-warehouse, POS sync, push notifications, seller portal.
