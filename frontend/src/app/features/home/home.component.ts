import { Component, OnDestroy, OnInit, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService, mediaUrl } from '../../core/api.service';
import { SeoService } from '../../core/seo.service';
import { HomePayload } from '../../core/models';
import { BdtPipe, CleanNamePipe, RevealDirective } from '../../core/ui';
import { ProductCardComponent } from '../../shared/product-card.component';

@Component({
  selector: 'rt-home',
  standalone: true,
  imports: [RouterLink, BdtPipe, CleanNamePipe, RevealDirective, ProductCardComponent],
  template: `
    <div class="rt-container" style="padding-top:22px;">
      <!-- HERO -->
      <section class="rt-hero">
        <div class="rt-sunburst"></div>
        <div class="hero-grid">
          <div class="hero-copy">
            <span class="stamp">EST. 2014 · GAIBANDHA</span>
            <h1>Phones worth<br />the walk to<br /><span class="highlight">Sadullapur.</span></h1>
            <p class="lede">
              Genuine handsets, laptops and sound — official warranty, EMI on the counter,
              and a human who answers the phone.
            </p>

            <div class="rt-row rt-row--wrap" style="gap:10px;">
              <a class="rt-btn rt-btn--rust" routerLink="/shop">Browse the shop</a>
              <a class="rt-btn rt-btn--ghost" routerLink="/track">Track an order</a>
            </div>

            <div class="hero-stats">
              <div><strong>100%</strong><span>Genuine stock</span></div>
              <div><strong>12</strong><span>Months warranty</span></div>
              <div><strong>2 hr</strong><span>Showroom pickup</span></div>
            </div>
          </div>

          <div class="hero-art" aria-hidden="true">
            @for (item of heroArt(); track item.id; let i = $index) {
              <div class="hero-card" [style.--tilt]="tilts[i % tilts.length]" [style.--delay]="i * 0.4 + 's'">
                <span class="mono-label">{{ item.brandName || 'Rocky pick' }}</span>
                @if (item.thumbUrl) {
                  <img [src]="media(item.thumbUrl)" alt="" />
                }
                <strong>{{ item.name | cleanName }}</strong>
                <span class="rt-price">{{ item.effectivePrice | bdt }}</span>
              </div>
            }

            <div class="hero-sticker rt-sticker">Warranty included</div>
          </div>
        </div>
      </section>

      <!-- BANNER SLIDER: admin-managed "home.hero" banners -->
      @if (heroBanners().length > 0) {
        <section
          class="banner-slider"
          aria-roledescription="carousel"
          aria-label="Featured offers"
          (mouseenter)="paused = true"
          (mouseleave)="paused = false"
          (focusin)="paused = true"
          (focusout)="paused = false"
        >
          <div class="slides" [style.transform]="'translateX(-' + slide() * 100 + '%)'">
            @for (banner of heroBanners(); track banner.id; let i = $index) {
              @if (isExternal(banner.link)) {
                <a class="slide" [href]="banner.link" [attr.aria-hidden]="i !== slide()" [attr.tabindex]="i !== slide() ? -1 : null">
                  <img [src]="media(banner.imageUrl)" [alt]="banner.title || 'Offer'" [attr.loading]="i === 0 ? 'eager' : 'lazy'" />
                </a>
              } @else {
                <a class="slide" [routerLink]="banner.link || '/shop'" [attr.aria-hidden]="i !== slide()" [attr.tabindex]="i !== slide() ? -1 : null">
                  <img [src]="media(banner.imageUrl)" [alt]="banner.title || 'Offer'" [attr.loading]="i === 0 ? 'eager' : 'lazy'" />
                </a>
              }
            }
          </div>

          @if (heroBanners().length > 1) {
            <button type="button" class="slide-btn prev" aria-label="Previous offer" (click)="go(-1)">‹</button>
            <button type="button" class="slide-btn next" aria-label="Next offer" (click)="go(1)">›</button>
            <div class="dots">
              @for (banner of heroBanners(); track banner.id; let i = $index) {
                <button
                  type="button"
                  class="dot"
                  [class.is-on]="i === slide()"
                  [attr.aria-label]="'Show offer ' + (i + 1)"
                  [attr.aria-current]="i === slide() ? 'true' : null"
                  (click)="slide.set(i)"
                ></button>
              }
            </div>
          }
        </section>
      }

      <!-- CATEGORIES -->
      <section class="section" rtReveal>
        <div class="section-head">
          <div>
            <div class="kicker">Counter one</div>
            <h2>Shop by category</h2>
          </div>
          <a class="mono-label" routerLink="/shop">Everything →</a>
        </div>

        <div class="cat-rail">
          @for (category of categories(); track category.id) {
            <a class="cat-chip" [routerLink]="['/category', category.slug]">
              <span class="cat-ico" aria-hidden="true">{{ icon(category.name) }}</span>
              <span>{{ category.name }}</span>
            </a>
          }
          @if (categories().length === 0) {
            <a class="cat-chip" routerLink="/shop">
              <span class="cat-ico" aria-hidden="true">▦</span>
              <span>All products</span>
            </a>
          }
        </div>
      </section>

      <!-- FLASH SALE -->
      @if (home()?.flashSale; as sale) {
        <section class="section" rtReveal>
          <div class="flash">
            <div class="flash-head">
              <div>
                <span class="rt-tag rt-tag--rust">Flash counter</span>
                <h2 class="mt-1">{{ sale.title }}</h2>
              </div>
              <div class="flash-clock">
                <span class="mono-label">Ends in</span>
                <div class="rt-countdown" aria-live="polite">
                  <span class="unit">{{ pad(countdown().h) }}<small>HRS</small></span>
                  <span class="unit">{{ pad(countdown().m) }}<small>MIN</small></span>
                  <span class="unit">{{ pad(countdown().s) }}<small>SEC</small></span>
                </div>
              </div>
            </div>

            <div class="flash-grid">
              @for (item of sale.items.slice(0, 4); track item.variantId) {
                <a class="flash-item" [routerLink]="['/product', item.slug]">
                  <div class="flash-thumb">
                    @if (item.thumbUrl) {
                      <img [src]="media(item.thumbUrl)" [alt]="item.name | cleanName" loading="lazy" />
                    }
                  </div>
                  <strong>{{ item.name | cleanName }}</strong>
                  <div class="rt-row" style="gap:8px;">
                    <span class="rt-price">{{ item.salePrice | bdt }}</span>
                    <s class="tiny muted">{{ item.regularPrice | bdt }}</s>
                  </div>
                  <span class="rt-tag">-{{ item.discountPercent }}%</span>
                </a>
              }
            </div>
          </div>
        </section>
      }

      <!-- SHOWCASES: hand-picked shelves managed in admin -->
      @for (shelf of showcases(); track shelf.id) {
        <section class="section" rtReveal [attr.aria-labelledby]="'showcase-' + shelf.slug">
          <div class="section-head">
            <div>
              @if (shelf.subtitle) {
                <div class="kicker">{{ shelf.subtitle }}</div>
              }
              <h2 [id]="'showcase-' + shelf.slug">{{ shelf.title }}</h2>
            </div>
            <a class="mono-label" routerLink="/shop">See all →</a>
          </div>

          <div class="rt-grid rt-cols-4">
            @for (product of shelf.items; track product.id) {
              <rt-product-card [product]="product" />
            }
          </div>
        </section>
      }

      <!-- NEW ARRIVALS -->
      <section class="section" rtReveal>
        <div class="section-head">
          <div>
            <div class="kicker">Fresh off the truck</div>
            <h2>New arrivals</h2>
          </div>
          <a class="mono-label" routerLink="/shop" [queryParams]="{ sort: 'newest' }">See all →</a>
        </div>

        @if (loading()) {
          <div class="rt-grid rt-cols-4">
            @for (n of [1, 2, 3, 4]; track n) {
              <div class="rt-skeleton" style="height:330px;"></div>
            }
          </div>
        } @else if (products('newArrivals').length === 0) {
          <div class="rt-panel center">
            <h3 class="mt-0">The shelves are being restocked</h3>
            <p>The first products will appear here as soon as the showroom publishes them.</p>
            <a class="rt-btn rt-btn--ghost" routerLink="/shop">Open the catalogue</a>
          </div>
        } @else {
          <div class="rt-grid rt-cols-4">
            @for (product of products('newArrivals'); track product.id) {
              <rt-product-card [product]="product" />
            }
          </div>
        }
      </section>

      <!-- PROMO BANNERS: admin-managed "home.promo" banners -->
      @if (promoBanners().length > 0) {
        <section class="promo-duo" rtReveal>
          @for (banner of promoBanners(); track banner.id) {
            @if (isExternal(banner.link)) {
              <a [href]="banner.link"><img [src]="media(banner.imageUrl)" [alt]="banner.title || 'Promotion'" loading="lazy" /></a>
            } @else {
              <a [routerLink]="banner.link || '/shop'"><img [src]="media(banner.imageUrl)" [alt]="banner.title || 'Promotion'" loading="lazy" /></a>
            }
          }
        </section>
      }

      <!-- PROMO BAND -->
      <section class="promo-band" rtReveal>
        <div class="promo-copy">
          <span class="mono-label">Old phone, new credit</span>
          <h3 class="mt-1">Trade in at the counter.<br />Walk out with the upgrade.</h3>
          <p class="mb-0">Bring your handset to Sadullapur and we value it on the spot.</p>
        </div>
        <div class="promo-actions">
          <a class="rt-btn rt-btn--rust" routerLink="/shop">See upgrades</a>
          <a class="rt-btn rt-btn--ghost" href="tel:01700000000">Call the shop</a>
        </div>
      </section>

      <!-- BEST DEALS -->
      <section class="section" rtReveal>
        <div class="section-head">
          <div>
            <div class="kicker">Price tags we can defend</div>
            <h2>Best deals this week</h2>
          </div>
          <a class="mono-label" routerLink="/shop" [queryParams]="{ sort: 'discount' }">All offers →</a>
        </div>

        <div class="rt-grid rt-cols-4">
          @for (product of products('bestDeals'); track product.id) {
            <rt-product-card [product]="product" />
          }
        </div>
      </section>

      <!-- LATEST BLOG POSTS -->
      @if (blogs().length > 0) {
        <section class="section" rtReveal>
          <div class="section-head">
            <div>
              <div class="kicker">From the counter</div>
              <h2>Guides &amp; news</h2>
            </div>
            <a class="mono-label" routerLink="/blogs">All posts →</a>
          </div>

          <div class="rt-grid rt-cols-3">
            @for (post of blogs(); track post.id) {
              <a class="rt-card" [routerLink]="['/blogs', post.slug]" style="display:grid; overflow:hidden; padding:0; color:inherit; text-decoration:none;">
                @if (post.cover) {
                  <img [src]="media(post.cover)" [alt]="post.title" loading="lazy" style="width:100%; aspect-ratio:1600/520; object-fit:cover; border-bottom:2px solid var(--ink);" />
                }
                <span style="display:grid; gap:6px; padding:14px 16px 18px;">
                  <strong>{{ post.title }}</strong>
                  @if (post.excerpt) {
                    <span class="small">{{ post.excerpt }}</span>
                  }
                </span>
              </a>
            }
          </div>
        </section>
      }

      <!-- TRUST -->
      <section class="trust" rtReveal>
        @for (badge of trust; track badge.title) {
          <div class="trust-item">
            <span class="trust-ico" aria-hidden="true">{{ badge.icon }}</span>
            <div>
              <strong>{{ badge.title }}</strong>
              <p class="tiny mb-0">{{ badge.note }}</p>
            </div>
          </div>
        }
      </section>

      <!-- BRANDS + STORE -->
      <section class="section" rtReveal>
        <div class="rt-grid rt-cols-2" style="align-items:stretch; gap:20px;">
          <div class="rt-panel">
            <span class="mono-label">Stocked brands</span>
            <h3 class="mt-1">Names we stand behind</h3>
            <div class="brand-rail">
              @for (brand of brands(); track brand.id) {
                <a class="brand-chip" [routerLink]="['/brands', brand.slug]">
                  {{ brand.name }}
                </a>
              }
              @if (brands().length === 0) {
                <span class="tiny muted">Brand list is being updated.</span>
              }
            </div>
          </div>

          <div class="rt-panel" style="background:var(--teal); color:var(--cream);">
            <span class="mono-label" style="color:#bfe3da;">The showroom</span>
            <h3 class="mt-1" style="color:var(--cream);">{{ store()?.name || 'Rocky Telecom' }}</h3>
            <p style="color:#d7ece6;">{{ store()?.address || 'Sadullapur, Gaibandha' }}</p>
            <p class="small" style="color:#bfe3da;">
              {{ store()?.openingHours || 'Every day 10:00–21:00' }} · {{ store()?.phone || '01700000000' }}
            </p>
            <div class="rt-row rt-row--wrap" style="gap:10px;">
              <a class="rt-btn rt-btn--ghost" href="tel:01700000000">Call</a>
              <a class="rt-btn rt-btn--rust" routerLink="/track">Track order</a>
            </div>
          </div>
        </div>
      </section>
    </div>
  `,
  styles: [
    `
      .hero-grid {
        display: grid;
        grid-template-columns: 1.1fr 0.9fr;
        gap: 26px;
        padding: clamp(24px, 4vw, 52px);
        position: relative;
        z-index: 1;
      }

      .stamp {
        display: inline-block;
        font-family: var(--font-mono);
        font-size: 10.5px;
        letter-spacing: 0.2em;
        border: 1.5px solid rgba(255, 249, 236, 0.5);
        border-radius: 4px;
        padding: 4px 8px;
        margin-bottom: 14px;
      }

      .lede {
        color: #d9ece6;
        max-width: 44ch;
        font-size: 16.5px;
      }

      .hero-stats {
        display: flex;
        gap: 22px;
        margin-top: 26px;
        border-top: 1.5px dashed rgba(255, 249, 236, 0.35);
        padding-top: 16px;
      }

      .hero-stats strong {
        font-family: var(--font-display);
        font-size: 22px;
        display: block;
      }

      .hero-stats span {
        font-family: var(--font-mono);
        font-size: 10px;
        letter-spacing: 0.12em;
        text-transform: uppercase;
        color: #bfe3da;
      }

      .hero-art {
        position: relative;
        min-height: 300px;
      }

      .hero-card {
        position: absolute;
        width: 200px;
        background: var(--cream);
        color: var(--ink);
        border: 2px solid var(--ink);
        border-radius: 12px;
        padding: 12px;
        box-shadow: 7px 7px 0 rgba(0, 0, 0, 0.45);
        transform: rotate(var(--tilt));
        display: flex;
        flex-direction: column;
        gap: 6px;
        animation: float 6s ease-in-out infinite;
        animation-delay: var(--delay);
      }

      .hero-card img {
        height: 110px;
        object-fit: contain;
      }

      .hero-card strong { font-size: 13px; line-height: 1.2; }

      .hero-card:nth-child(1) { top: 6%; left: 2%; }
      .hero-card:nth-child(2) { top: 32%; left: 44%; }
      .hero-card:nth-child(3) { top: 2%; left: 62%; }

      .hero-sticker {
        position: absolute;
        bottom: 4%;
        left: 34%;
        font-size: 13px;
      }

      .cat-rail {
        display: flex;
        gap: 12px;
        overflow-x: auto;
        padding-bottom: 6px;
        scrollbar-width: thin;
      }

      .cat-chip {
        flex: 0 0 auto;
        display: inline-flex;
        align-items: center;
        gap: 10px;
        padding: 12px 18px;
        border: 2px solid var(--ink);
        border-radius: 12px;
        background: var(--cream);
        box-shadow: var(--shadow-sm);
        font-weight: 700;
        text-decoration: none;
        transition: transform var(--dur-1) var(--ease), box-shadow var(--dur-1) var(--ease);
      }

      .cat-chip:hover { transform: translate(-2px, -2px); box-shadow: var(--shadow); }

      .cat-ico {
        font-size: 19px;
        width: 30px;
        height: 30px;
        display: grid;
        place-items: center;
        background: var(--mustard);
        border: 1.5px solid var(--ink);
        border-radius: 8px;
      }

      .flash {
        border: 2px solid var(--ink);
        border-radius: 18px;
        background:
          var(--halftone) 0 0 / 7px 7px,
          #f7e3c6;
        box-shadow: var(--shadow);
        padding: 22px;
      }

      .flash-head {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        gap: 16px;
        flex-wrap: wrap;
        margin-bottom: 18px;
      }

      .flash-clock { text-align: right; }

      .flash-grid {
        display: grid;
        grid-template-columns: repeat(4, minmax(0, 1fr));
        gap: 14px;
      }

      .flash-item {
        display: flex;
        flex-direction: column;
        gap: 6px;
        padding: 12px;
        background: var(--cream);
        border: 2px solid var(--ink);
        border-radius: 12px;
        text-decoration: none;
        transition: transform var(--dur-1) var(--ease);
      }

      .flash-item:hover { transform: translateY(-3px); }

      .flash-thumb {
        height: 110px;
        background: var(--paper-deep);
        border: 1.5px solid var(--ink);
        border-radius: 8px;
        display: grid;
        place-items: center;
        overflow: hidden;
      }

      .flash-thumb img { max-height: 100px; object-fit: contain; padding: 6px; }

      .promo-band {
        margin-top: 34px;
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 20px;
        flex-wrap: wrap;
        padding: 28px;
        border: 2px dashed var(--ink);
        border-radius: 18px;
        background: var(--mustard);
        box-shadow: var(--shadow-sm);
      }

      .promo-copy h3 { margin-bottom: 6px; }

      .promo-actions { display: flex; gap: 10px; flex-wrap: wrap; }

      .trust {
        display: grid;
        grid-template-columns: repeat(4, minmax(0, 1fr));
        gap: 14px;
        margin-top: 34px;
      }

      .trust-item {
        display: flex;
        gap: 12px;
        align-items: flex-start;
        padding: 16px;
        background: var(--cream);
        border: 2px solid var(--ink);
        border-radius: 12px;
      }

      .trust-ico { font-size: 20px; }

      .brand-rail { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }

      .brand-chip {
        padding: 8px 14px;
        border: 2px solid var(--ink);
        border-radius: var(--pill);
        background: var(--cream);
        text-decoration: none;
        font-weight: 700;
        font-size: 13px;
      }

      @media (max-width: 1023px) {
        .hero-grid { grid-template-columns: 1fr; }
        .hero-art { display: none; }
        .flash-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
        .trust { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      }

      @media (max-width: 560px) {
        .flash-grid { grid-template-columns: 1fr; }
      }
    `
  ]
})
export class HomeComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly seo = inject(SeoService);

  readonly home = signal<HomePayload | null>(null);
  readonly loading = signal(true);
  readonly now = signal(Date.now());
  readonly tilts = ['-6deg', '3deg', '-2deg'];

  private timer?: ReturnType<typeof setInterval>;

  readonly trust = [
    { icon: '✔', title: '100% genuine', note: 'Sealed stock, official warranty' },
    { icon: '⇥', title: 'Fast delivery', note: 'Gaibandha same-day, 3-zone flat rate' },
    { icon: '◷', title: 'Counter EMI', note: 'Bank plans from 3 to 24 months' },
    { icon: '↺', title: 'Replacement promise', note: '7-day exchange on dead-on-arrival' }
  ];

  readonly categories = computed(() => this.home()?.categories ?? []);
  readonly brands = computed(() => this.home()?.brands ?? []);
  readonly store = computed(() => this.home()?.store ?? null);
  readonly showcases = computed(() => this.home()?.showcases ?? []);
  readonly heroBanners = computed(() => this.home()?.hero ?? []);
  readonly blogs = computed(() => this.home()?.blogs ?? []);
  readonly promoBanners = computed(() => (this.home()?.promo ?? []).slice(0, 2));

  readonly slide = signal(0);
  paused = false;
  private ticks = 0;

  readonly heroArt = computed(() => {
    const payload = this.home();
    if (!payload) {
      return [];
    }

    const pool = [...payload.featured, ...payload.newArrivals, ...payload.bestDeals];
    const seen = new Set<number>();

    return pool.filter((item) => (seen.has(item.id) ? false : (seen.add(item.id), true))).slice(0, 3);
  });

  readonly countdown = computed(() => {
    const sale = this.home()?.flashSale;
    const end = sale ? new Date(sale.endAt).getTime() : 0;
    const remaining = Math.max(0, end - this.now());
    const totalSeconds = Math.floor(remaining / 1000);

    return {
      h: Math.floor(totalSeconds / 3600),
      m: Math.floor((totalSeconds % 3600) / 60),
      s: totalSeconds % 60
    };
  });

  ngOnInit(): void {
    this.api.get<HomePayload>('/home').subscribe({
      next: (payload) => {
        this.home.set(payload);
        this.loading.set(false);
        this.applySeo(payload);
      },
      error: () => this.loading.set(false)
    });

    // Browser only: a 1s interval would keep the app unstable forever during SSR
    // and the dev server would report "page did not render in 30 seconds".
    if (isPlatformBrowser(this.platformId)) {
      const reduceMotion = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;

      this.timer = setInterval(() => {
        this.now.set(Date.now());

        // Banner slider advances every 6 s unless the pointer or focus is on it, or motion is reduced.
        if (!this.paused && !reduceMotion && this.heroBanners().length > 1 && ++this.ticks % 6 === 0) {
          this.go(1);
        }
      }, 1000);
    }
  }

  /** Local-business and site-search structured data for the home page. */
  private applySeo(payload: HomePayload): void {
    const origin = this.seo.origin();
    const store = payload.store;

    this.seo.set({
      title: 'Rocky Telecom — Phones & Electronics, Gaibandha',
      path: '/',
      jsonLd: [
        {
          '@context': 'https://schema.org',
          '@type': 'WebSite',
          name: 'Rocky Telecom',
          url: origin,
          potentialAction: {
            '@type': 'SearchAction',
            target: `${origin}/search?q={search_term_string}`,
            'query-input': 'required name=search_term_string'
          }
        },
        {
          '@context': 'https://schema.org',
          '@type': 'ElectronicsStore',
          name: store?.name ?? 'Rocky Telecom',
          url: origin,
          telephone: store?.phone,
          email: store?.email ?? undefined,
          address: {
            '@type': 'PostalAddress',
            streetAddress: store?.address ?? 'Sadullapur, Gaibandha',
            addressCountry: 'BD'
          }
        }
      ]
    });
  }

  go(delta: 1 | -1): void {
    const count = this.heroBanners().length;
    if (count > 0) {
      this.slide.set((this.slide() + delta + count) % count);
      this.ticks = 0;
    }
  }

  isExternal(link: string | null): boolean {
    return !!link && /^https?:\/\//i.test(link);
  }

  ngOnDestroy(): void {
    if (this.timer) {
      clearInterval(this.timer);
    }
  }

  products(shelf: 'newArrivals' | 'bestDeals' | 'featured') {
    return this.home()?.[shelf] ?? [];
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }

  pad(value: number): string {
    return String(value).padStart(2, '0');
  }

  icon(name: string): string {
    const key = name.toLowerCase();

    if (key.includes('phone') || key.includes('mobile')) return '📱';
    if (key.includes('laptop')) return '💻';
    if (key.includes('tablet')) return '▭';
    if (key.includes('watch')) return '⌚';
    if (key.includes('sound') || key.includes('audio')) return '◉';
    if (key.includes('tv')) return '▣';
    if (key.includes('accessor')) return '⌁';

    return '▦';
  }
}
