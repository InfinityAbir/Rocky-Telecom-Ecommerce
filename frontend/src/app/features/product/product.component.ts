import { Component, HostListener, OnDestroy, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { compareSizes } from '../../core/size-order';
import { ApiService, mediaUrl, toApiError } from '../../core/api.service';
import { CartService } from '../../core/cart.service';
import { ChromeService } from '../../core/chrome.service';
import { SeoService } from '../../core/seo.service';
import { ProductDetail, ProductDetailResult, Variant } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { BdtPipe, CleanNamePipe, RevealDirective } from '../../core/ui';
import { ProductCardComponent } from '../../shared/product-card.component';
import { ReviewsComponent } from '../../shared/reviews.component';

@Component({
  selector: 'rt-product',
  standalone: true,
  imports: [FormsModule, RouterLink, BdtPipe, CleanNamePipe, RevealDirective, ProductCardComponent, ReviewsComponent],
  template: `
    <div class="rt-container" style="padding-top:20px;">
      @if (loading()) {
        <div class="rt-grid rt-cols-2">
          <div class="rt-skeleton" style="height:420px;"></div>
          <div class="rt-stack">
            <div class="rt-skeleton" style="height:44px;"></div>
            <div class="rt-skeleton" style="height:120px;"></div>
            <div class="rt-skeleton" style="height:80px;"></div>
          </div>
        </div>
      } @else if (!product()) {
        <div class="rt-panel center" style="padding:46px;">
          <h2 class="mt-0">This product is not on the shelf</h2>
          <p>It may have been renamed or unpublished. Try the catalogue instead.</p>
          <a class="rt-btn rt-btn--ghost" routerLink="/shop">Back to the shop</a>
        </div>
      } @else {
        @let p = product()!;
        <nav class="mono-label" style="margin-bottom:14px;">
          <a routerLink="/shop">Shop</a>
          @if (p.category) {
            <span> / </span>
            <a [routerLink]="['/category', p.category!.slug]">{{ p.category!.name }}</a>
          }
          <span> / {{ p.name | cleanName }}</span>
        </nav>

        <div class="detail-grid">
          <!-- GALLERY -->
          <section>
            <div class="gallery-main">
              @if (activeImage()) {
                <img [src]="media(activeImage())" [alt]="p.name | cleanName" />
              } @else {
                <div class="no-image">
                  <span class="rt-tag">Rocky Telecom</span>
                  <strong>{{ p.name | cleanName }}</strong>
                </div>
              }
              @if (selected()?.discountPercent) {
                <span class="gallery-discount">-{{ selected()!.discountPercent }}%</span>
              }
            </div>

            @if (images().length > 1) {
              <div class="thumbs">
                @for (image of images(); track image.id) {
                  <button
                    type="button"
                    class="thumb-btn"
                    [class.is-active]="activeImage() === image.url"
                    (click)="activeImage.set(image.url)"
                    [attr.aria-label]="'View image ' + image.sortOrder"
                  >
                    <img [src]="media(image.url)" alt="" />
                  </button>
                }
              </div>
            }
          </section>

          <!-- BUY BOX -->
          <section>
            <div class="rt-row rt-row--wrap" style="gap:8px;">
              <span class="rt-tag rt-tag--paper">{{ p.brand?.name || 'Rocky Telecom' }}</span>
              <span class="rt-tag" [class.rt-tag--rust]="p.type === 'used'">{{ label(p.type) }}</span>
              @if (p.warrantyMonths) {
                <span class="rt-tag rt-tag--teal">{{ p.warrantyMonths }}-month warranty</span>
              }
              @if (p.isPreOrder) {
                <span class="rt-tag">Pre-order</span>
              }
            </div>

            <h1 class="mt-2">{{ p.name | cleanName }}</h1>
            @if (p.shortDesc) {
              <p class="mb-0">{{ p.shortDesc }}</p>
            }

            <div class="price-box">
              <div>
                <div class="rt-price" style="font-size:30px;">{{ (selected()?.effectivePrice ?? 0) | bdt }}</div>
                @if (selected()?.regularPrice && selected()!.regularPrice > selected()!.effectivePrice) {
                  <s class="muted small">{{ selected()!.regularPrice | bdt }}</s>
                  <span class="rt-tag rt-tag--rust" style="margin-left:8px;">Save {{ selected()!.discountPercent }}%</span>
                }
              </div>
              <span [class.stock-in]="selected()?.inStock" [class.stock-out]="!selected()?.inStock">
                @if (selected()?.inStock) { In stock · {{ selected()!.stock }} left } @else { Sold out }
              </span>
            </div>

            <!-- VARIANTS -->
            @if (variantOptions().length > 0) {
              @for (group of variantOptions(); track group.axis) {
                <div class="variant-group">
                  <span class="mono-label">{{ group.axis }}</span>
                  <div class="rt-row rt-row--wrap" style="gap:8px; margin-top:6px;">
                    @for (option of group.values; track option.value) {
                      <button
                        type="button"
                        class="rt-chip"
                        [class.is-active]="group.selected === option.value"
                        (click)="selectOption(group.axis, option.value)"
                        [disabled]="!option.available"
                        [title]="option.available ? option.value : option.value + ' — unavailable'"
                      >
                        @if (option.hex) {
                          <span class="swatch" [style.background]="option.hex"></span>
                        }
                        {{ option.value }}
                      </button>
                    }
                  </div>
                </div>
              }
            }

            <div class="rt-row rt-row--wrap" style="gap:10px; margin-top:16px;">
              <div class="qty">
                <button type="button" (click)="changeQty(-1)" aria-label="Decrease quantity">−</button>
                <span>{{ qty() }}</span>
                <button type="button" (click)="changeQty(1)" aria-label="Increase quantity">+</button>
              </div>

              <button
                class="rt-btn rt-btn--rust"
                type="button"
                [disabled]="!selected()?.inStock || adding()"
                (click)="addToCart()"
              >
                {{ adding() ? 'Adding…' : 'Add to cart' }}
              </button>

              <button
                class="rt-btn rt-btn--ghost"
                type="button"
                [disabled]="!selected()?.inStock"
                (click)="buyNow()"
              >
                Buy now
              </button>

              @if (p.isPreOrder) {
                <a class="rt-btn rt-btn--ghost" routerLink="/pre-order" [queryParams]="{ product: p.slug }">
                  Pre-order this
                </a>
              }
            </div>

            <div class="rt-dashed"></div>

            <!-- EMI -->
            @if (p.emiPlans.length > 0) {
              <div>
                <span class="mono-label">EMI calculator</span>
                <table class="rt-table rt-table--scroll" style="margin-top:6px;">
                  <thead>
                    <tr><th>Bank</th><th>Months</th><th>Rate</th><th class="right">Monthly</th></tr>
                  </thead>
                  <tbody>
                    @for (plan of p.emiPlans.slice(0, 4); track plan.id) {
                      <tr>
                        <td>{{ plan.bankName }}</td>
                        <td>{{ plan.months }}</td>
                        <td>{{ plan.interestPct }}%</td>
                        <td class="right"><strong>{{ plan.monthlyAmount | bdt }}</strong></td>
                      </tr>
                    }
                  </tbody>
                </table>
                <p class="tiny muted mt-1">Calculator only — the bank approves the plan at the counter.</p>
              </div>
            }

            <!-- DELIVERY -->
            <div class="delivery">
              <span class="mono-label">Delivery</span>
              <p class="small mb-0">
                @for (zone of p.deliveryZones; track zone.id) {
                  <span>{{ zone.name }} {{ zone.charge | bdt }} ({{ zone.estimatedDays }}d) · </span>
                }
                Showroom pickup free in 2 hours.
              </p>
            </div>
          </section>
        </div>

        <!-- SPECS + DESCRIPTION -->
        <section class="section" rtReveal>
          <div class="tabs" role="tablist" aria-label="Product information">
            <button class="tab" role="tab" [attr.aria-selected]="tab() === 'specs'" [class.is-active]="tab() === 'specs'" (click)="tab.set('specs')">Specification</button>
            <button class="tab" role="tab" [attr.aria-selected]="tab() === 'about'" [class.is-active]="tab() === 'about'" (click)="tab.set('about')">Description</button>
          </div>

          @if (tab() === 'specs') {
            <div class="spec-grid">
              @for (group of p.specGroups; track group.name) {
                <div class="spec-card">
                  <h3>{{ group.name }}</h3>
                  <table class="rt-table spec-table">
                    <tbody>
                      @for (field of group.fields; track field.label) {
                        @if (field.value) {
                          <tr>
                            <th>{{ field.label }}</th>
                            <td>{{ field.value }}{{ field.unit ? ' ' + field.unit : '' }}</td>
                          </tr>
                        }
                      }
                    </tbody>
                  </table>
                </div>
              }
              @if (p.specGroups.length === 0) {
                <p class="muted">The spec sheet is being prepared for this product.</p>
              }
            </div>
          } @else {
            @let a = about();
            <article class="about rt-panel rt-panel--flat">
              <h3 class="mt-0">{{ p.name | cleanName }} price in Bangladesh</h3>
              <p>{{ a.intro }}</p>
              @if (p.description) {
                <p style="white-space: pre-line;">{{ p.description }}</p>
              }

              @if (a.features.length) {
                <h3>Key features of {{ p.name | cleanName }}</h3>
                @for (feature of a.features; track feature.title) {
                  <h4>{{ feature.title }}</h4>
                  <p>{{ feature.text }}</p>
                }
              }

              <h3>Why buy {{ p.name | cleanName }} from Rocky Telecom</h3>
              <ul class="benefits">
                @for (benefit of a.benefits; track benefit) {
                  <li>{{ benefit }}</li>
                }
              </ul>

              @if (a.faq.length) {
                <h3>Frequently asked questions</h3>
                @for (item of a.faq; track item.q) {
                  <h4>{{ item.q }}</h4>
                  <p>{{ item.a }}</p>
                }
              }

              @if (p.relatedProducts.length) {
                <h3>Explore more similar {{ p.category?.name || 'products' }}</h3>
                <ul class="similar">
                  @for (related of p.relatedProducts.slice(0, 6); track related.id) {
                    <li><a [routerLink]="['/product', related.slug]">{{ related.name | cleanName }}</a></li>
                  }
                </ul>
              }
            </article>
          }
        </section>

        <!-- REVIEWS -->
        <rt-reviews [slug]="p.slug" [summary]="p.rating" />

        <div class="buybar-spacer" aria-hidden="true"></div>
        @if (showBar()) {
          <div class="buybar" role="region" aria-label="Quick purchase">
            <div class="buybar-in rt-container">
              <div class="bb-info">
                <div class="bb-pick"><span aria-hidden="true">🏬</span> Showroom pickup <small>free · 2 hours</small></div>
                <div class="bb-pick"><span aria-hidden="true">🚚</span> Home delivery <small>{{ deliveryHint(p) }}</small></div>
              </div>

              <div class="bb-price">
                <strong>{{ (selected()?.effectivePrice ?? 0) | bdt }}</strong>
                @if (monthly(p); as m) {
                  <small>{{ m.amount | bdt }}/mo for {{ m.months }} months</small>
                }
              </div>

              <div class="bb-actions">
                <div class="qty">
                  <button type="button" (click)="changeQty(-1)" aria-label="Decrease quantity">−</button>
                  <span>{{ qty() }}</span>
                  <button type="button" (click)="changeQty(1)" aria-label="Increase quantity">+</button>
                </div>
                <button class="rt-btn rt-btn--rust" type="button" [disabled]="!selected()?.inStock || adding()" (click)="addToCart()">Add to cart</button>
                <button class="rt-btn" type="button" [disabled]="!selected()?.inStock" (click)="buyNow()">Buy now</button>
              </div>
            </div>
          </div>
        }

        <!-- RELATED -->
        @if (p.relatedProducts.length > 0) {
          <section class="section" rtReveal>
            <div class="section-head">
              <div>
                <div class="kicker">Same shelf</div>
                <h2>You may also like</h2>
              </div>
            </div>
            <div class="rt-grid rt-cols-4">
              @for (related of p.relatedProducts.slice(0, 4); track related.id) {
                <rt-product-card [product]="related" />
              }
            </div>
          </section>
        }
      }
    </div>
  `,
  styles: [
    `
      .detail-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 30px;
        align-items: start;
      }

      .gallery-main {
        position: relative;
        aspect-ratio: 1 / 1;
        border: 2px solid var(--ink);
        border-radius: 18px;
        background: var(--halftone) 0 0 / 7px 7px, linear-gradient(135deg, #efe0c4, #fbf2e1);
        box-shadow: var(--shadow);
        display: grid;
        place-items: center;
        overflow: hidden;
      }

      .gallery-main img { width: 100%; height: 100%; object-fit: contain; padding: 26px; }

      .gallery-discount {
        position: absolute;
        top: 16px;
        right: 16px;
        background: var(--rust);
        color: var(--cream);
        border: 2px solid var(--ink);
        border-radius: var(--pill);
        padding: 5px 12px;
        font-family: var(--font-mono);
        font-weight: 700;
      }

      .no-image { display: grid; gap: 8px; text-align: center; padding: 30px; color: var(--ink-soft); }

      .thumbs { display: flex; gap: 8px; margin-top: 10px; flex-wrap: wrap; }

      .thumb-btn {
        width: 66px;
        height: 66px;
        border: 2px solid var(--ink);
        border-radius: 9px;
        background: var(--cream);
        padding: 4px;
        cursor: pointer;
      }

      .thumb-btn.is-active { box-shadow: 0 0 0 3px var(--mustard); }

      .price-box {
        display: flex;
        align-items: flex-end;
        justify-content: space-between;
        gap: 12px;
        flex-wrap: wrap;
        padding: 16px 18px;
        margin: 16px 0;
        background: var(--paper-deep);
        border: 2px solid var(--ink);
        border-radius: 12px;
        box-shadow: var(--shadow-sm);
      }

      .variant-group { margin-top: 14px; }

      .swatch { width: 14px; height: 14px; border-radius: 50%; border: 1.5px solid var(--ink); display: inline-block; }

      .qty {
        display: inline-flex;
        align-items: center;
        border: 2px solid var(--ink);
        border-radius: 9px;
        background: var(--cream);
        overflow: hidden;
      }

      .qty button {
        width: 42px;
        height: 44px;
        border: 0;
        background: var(--paper-deep);
        font-size: 18px;
        cursor: pointer;
      }

      .qty span { min-width: 46px; text-align: center; font-family: var(--font-mono); font-weight: 700; }

      .delivery { margin-top: 14px; }

      .tabs { display: flex; gap: 8px; }
      .tab {
        min-height: 44px; padding: 0 20px; border: 2px solid var(--ink); border-radius: 10px 10px 0 0;
        background: var(--cream); font: inherit; font-weight: 700; cursor: pointer;
      }
      .tab.is-active { background: var(--ink); color: var(--cream); }

      /* Spec sheet: one full-width card per group, label on the left, value on the right. */
      .spec-grid { display: grid; grid-template-columns: 1fr; gap: 14px; margin-top: 0; padding-top: 14px; border-top: 2px solid var(--ink); }
      .spec-table th { width: 26%; min-width: 120px; font-weight: 600; color: var(--ink-soft); text-align: left; vertical-align: top; }
      .spec-table td { overflow-wrap: anywhere; }

      .about { margin-top: 0; padding: 22px 24px; line-height: 1.65; }
      .about h3 { margin: 26px 0 8px; font-size: 20px; }
      .about h4 { margin: 18px 0 4px; font-size: 16px; }
      .about p { margin: 0 0 6px; }
      .benefits, .similar { padding-left: 20px; margin: 6px 0; }
      .benefits li { margin: 4px 0; }
      .similar a { text-decoration: underline; text-underline-offset: 3px; }

      /* Sticky purchase bar: appears once the main buy box has scrolled away. */
      .buybar-spacer { height: 84px; }
      .buybar {
        position: fixed; left: 0; right: 0; bottom: 0; z-index: 45;
        background: var(--ink); color: var(--cream); border-top: 2px solid var(--ink);
        padding-bottom: env(safe-area-inset-bottom);
        box-shadow: 0 -8px 24px rgba(32, 26, 19, 0.25);
      }
      .buybar-in { display: flex; align-items: center; gap: 24px; padding-top: 10px; padding-bottom: 10px; }
      .bb-info { display: flex; gap: 22px; flex: 1; font-size: 13px; }
      .bb-pick { display: grid; gap: 2px; }
      .bb-pick small { color: #cdbfa8; }
      .bb-price { display: grid; text-align: right; }
      .bb-price strong { font-size: 22px; font-family: var(--font-display); }
      .bb-price small { color: #cdbfa8; }
      .bb-actions { display: flex; align-items: center; gap: 10px; }
      .buybar .qty { background: var(--cream); color: var(--ink); }

      @media (max-width: 1023px) {
        /* Sit above the mobile bottom nav (56px tall). */
        .buybar { bottom: calc(60px + env(safe-area-inset-bottom)); padding-bottom: 0; }
        .buybar-spacer { height: 150px; }
        .buybar-in { flex-wrap: wrap; gap: 8px 14px; }
        .bb-info { display: none; }
        .bb-price { text-align: left; flex: 1; }
        .bb-actions { width: 100%; }
        .bb-actions .rt-btn { flex: 1; padding-left: 10px; padding-right: 10px; }
      }

      .spec-card {
        background: var(--cream);
        border: 2px solid var(--ink);
        border-radius: 12px;
        padding: 16px;
      }

      .spec-card h3 { font-size: 15px; text-transform: uppercase; letter-spacing: 0.06em; margin-bottom: 8px; }

      @media (max-width: 860px) {
        .detail-grid { grid-template-columns: 1fr; }
      }
    `
  ]
})
export class ProductComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly cart = inject(CartService);
  private readonly chrome = inject(ChromeService);
  private readonly toast = inject(ToastService);
  private readonly seo = inject(SeoService);

  private readonly liftHelp = effect(() => {
    // The bar is about 84px on desktop and about 110px on phones; a fixed lift keeps both clear.
    this.chrome.bottomInset.set(this.showBar() && this.product() ? 92 : 0);
  }, { allowSignalWrites: true });
  private readonly destroy$ = new Subject<void>();

  readonly product = signal<ProductDetail | null>(null);
  readonly loading = signal(true);
  readonly adding = signal(false);
  readonly qty = signal(1);
  readonly tab = signal<'specs' | 'about'>('specs');
  readonly activeImage = signal<string | null>(null);
  readonly selection = signal<Record<string, string>>({});

  readonly images = computed(() => {
    const p = this.product();
    if (!p) return [];
    return [...p.images].sort((a, b) => a.sortOrder - b.sortOrder);
  });

  readonly selected = computed<Variant | null>(() => this.pickVariant());

  readonly variantOptions = computed(() => this.buildOptions());

  /** The sticky purchase bar shows once the page has been scrolled past the main buy box. */
  readonly showBar = signal(false);

  @HostListener('window:scroll')
  onScroll(): void {
    if (typeof window !== 'undefined') {
      this.showBar.set(window.scrollY > 520);
    }
  }

  readonly about = computed(() => this.buildAbout(this.product()));

  deliveryHint(p: ProductDetail): string {
    const zone = [...p.deliveryZones].sort((a, b) => a.estimatedDays - b.estimatedDays)[0];
    return zone ? `${zone.estimatedDays} day${zone.estimatedDays === 1 ? '' : 's'} · ${zone.name}` : '1–3 days';
  }

  /** Cheapest monthly instalment among the EMI plans, for the "from ৳X/mo" hint. */
  monthly(p: ProductDetail): { amount: number; months: number } | null {
    const plans = p.emiPlans.filter((plan) => plan.monthlyAmount && plan.monthlyAmount > 0);
    if (plans.length === 0) {
      return null;
    }

    const best = plans.reduce((a, b) => ((a.monthlyAmount ?? 0) <= (b.monthlyAmount ?? 0) ? a : b));
    return { amount: best.monthlyAmount ?? 0, months: best.months };
  }

  /** Value of one spec line, looked up by group and label (case-insensitive). */
  private spec(p: ProductDetail, group: string, label: string): string | null {
    const g = p.specGroups.find((x) => x.name.toLowerCase() === group.toLowerCase());
    const field = g?.fields.find((x) => x.label.toLowerCase() === label.toLowerCase());
    return field?.value ? `${field.value}${field.unit ? ' ' + field.unit : ''}`.trim() : null;
  }

  /**
   * Description tab: written from the product's own data so every product gets accurate, original
   * copy, and the FAQ is also published as structured data for search engines.
   */
  buildAbout(p: ProductDetail | null) {
    const empty = { intro: '', features: [] as { title: string; text: string }[], benefits: [] as string[], faq: [] as { q: string; a: string }[] };
    if (!p) {
      return empty;
    }

    const name = p.name.replace(/\s*\(Official\)\s*/i, '').trim();
    const bdt = (value: number) => '৳' + Math.round(value).toLocaleString('en-IN');
    const prices = p.variants.map((v) => v.effectivePrice).filter((x) => x > 0);
    const from = prices.length ? Math.min(...prices) : 0;
    const to = prices.length ? Math.max(...prices) : 0;
    const brand = p.brand?.name ?? 'the brand';
    const warranty = p.warrantyMonths ? `${p.warrantyMonths}-month ${p.warrantyType === 'official' ? 'official' : 'shop'} warranty` : 'warranty as listed';

    const size = this.spec(p, 'Display', 'Size');
    const dtype = this.spec(p, 'Display', 'Type');
    const res = this.spec(p, 'Display', 'Resolution');
    const hz = this.spec(p, 'Display', 'Refresh rate');
    const chip = this.spec(p, 'Platform', 'Chipset');
    const os = this.spec(p, 'Platform', 'OS');
    const cam = this.spec(p, 'Main camera', 'Setup');
    const selfie = this.spec(p, 'Selfie camera', 'Setup');
    const mah = this.spec(p, 'Battery', 'Capacity');
    const watt = this.spec(p, 'Battery', 'Charging');
    const wireless = this.spec(p, 'Battery', 'Wireless charging');
    const water = this.spec(p, 'Body', 'Water / dust rating');
    const build = this.spec(p, 'Body', 'Build');
    const net = this.spec(p, 'Network', 'Technology');
    const wlan = this.spec(p, 'Comms', 'WLAN');
    const nfc = this.spec(p, 'Comms', 'NFC');

    const options = this.variantOptions();
    const ram = options.find((o) => o.axis === 'Memory')?.values.map((v) => v.value) ?? [];
    const storage = options.find((o) => o.axis === 'Storage')?.values.map((v) => v.value) ?? [];
    const colors = options.find((o) => o.axis === 'Colour')?.values.map((v) => v.value) ?? [];

    const intro =
      `${name} from ${brand} is available at Rocky Telecom in Sadullapur, Gaibandha` +
      (from ? `, priced from ${bdt(from)}${to > from ? ` up to ${bdt(to)} depending on the variant` : ''}` : '') +
      `. It comes with ${warranty}, and you can pay in cash, by mobile banking or on EMI. Prices include VAT and stock is checked live, so what you see is what the counter has.`;

    const features: { title: string; text: string }[] = [];
    if (size || dtype) {
      features.push({
        title: `${size ? size.replace(/\s*in$/, '') + '-inch ' : ''}${dtype ?? 'display'}`,
        text: `The screen is a ${size ? size.replace(/\s*in$/, '') + '-inch ' : ''}${dtype ?? 'panel'}${res ? ` at ${res}` : ''}${hz ? `, refreshing up to ${hz.replace(/\s*Hz$/i, '')} Hz for smoother scrolling and games` : ''}.`
      });
    }
    if (chip) {
      features.push({ title: chip, text: `Powered by ${chip}${os ? `, running ${os}` : ''}, so everyday apps, multitasking and games stay responsive.` });
    }
    if (cam) {
      features.push({ title: 'Camera system', text: `Rear cameras: ${cam}.${selfie ? ` Front camera: ${selfie}.` : ''}` });
    }
    if (mah) {
      features.push({
        title: `${mah} battery`,
        text: `A ${mah} battery${watt ? ` with ${watt} wired charging` : ''}${wireless && !/^no/i.test(wireless) ? ` and wireless charging (${wireless})` : ''}.`
      });
    }
    if (build || water) {
      features.push({ title: 'Build quality', text: `${build ? 'Build: ' + build + '. ' : ''}${water && !/none/i.test(water) ? `Water and dust rating: ${water}.` : ''}`.trim() });
    }
    if (net || wlan) {
      features.push({ title: 'Connectivity', text: `${net ? net + '. ' : ''}${wlan ? 'WLAN: ' + wlan + '. ' : ''}${nfc === 'Yes' ? 'NFC is included for contactless payments.' : ''}`.trim() });
    }

    const benefits = [
      `Genuine product with ${warranty}, handled at our Sadullapur showroom.`,
      'Easy EMI with the banks listed on this page, approved at the counter.',
      'Free showroom pickup, or home delivery across Bangladesh.',
      'Cash on delivery is available; prices and stock are verified by the server at checkout.',
      'Walk in to see the device before you buy, and ask the team anything.'
    ];

    const faq: { q: string; a: string }[] = [];
    if (from) {
      faq.push({ q: `What is the price of ${name} in Bangladesh?`, a: `At Rocky Telecom ${name} starts from ${bdt(from)}${to > from ? ` (up to ${bdt(to)} for the larger configurations)` : ''}. The price on this page is the one you pay.` });
    }
    if (ram.length || storage.length) {
      faq.push({
        q: `What RAM and storage options does ${name} have?`,
        a: `${ram.length ? 'Memory: ' + ram.join(', ') + '. ' : ''}${storage.length ? 'Storage: ' + storage.join(', ') + '.' : ''}`.trim()
      });
    }
    if (colors.length) {
      faq.push({ q: `Which colours is ${name} available in?`, a: `${colors.join(', ')}. Availability can differ by colour and configuration.` });
    }
    if (net) {
      faq.push({ q: `Does ${name} support 5G?`, a: /5G/i.test(net) ? `Yes. Network support: ${net}.` : `No. It supports: ${net}.` });
    }
    if (cam) {
      faq.push({ q: `How good is the ${name} camera?`, a: `Main cameras: ${cam}.${selfie ? ` Selfie camera: ${selfie}.` : ''}` });
    }
    if (mah) {
      faq.push({ q: `What is the battery capacity of ${name}?`, a: `${mah}${watt ? ` with ${watt} charging` : ''}.` });
    }
    if (wireless) {
      faq.push({ q: `Does ${name} support wireless charging?`, a: /^no/i.test(wireless) ? 'No, it charges by cable only.' : `Yes: ${wireless}.` });
    }
    if (os) {
      faq.push({ q: `Which operating system does ${name} run?`, a: `${os}.` });
    }
    if (water) {
      faq.push({ q: `Is ${name} water resistant?`, a: /none|^no/i.test(water) ? 'It has no official water or dust rating.' : `It is rated ${water}.` });
    }
    faq.push({ q: `Is there a warranty on ${name}?`, a: `Yes: ${warranty}. Claims are handled at the Rocky Telecom showroom in Sadullapur, Gaibandha.` });
    faq.push({ q: `Where can I buy ${name} in Bangladesh?`, a: 'You can order it online from Rocky Telecom for home delivery, or collect it from the showroom in Sadullapur, Gaibandha.' });

    return { intro, features, benefits, faq };
  }

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((params) => this.load(params.get('slug') ?? ''));
  }

  ngOnDestroy(): void {
    this.chrome.bottomInset.set(0);
    this.destroy$.next();
    this.destroy$.complete();
  }

  private load(slug: string): void {
    this.loading.set(true);

    this.api.get<ProductDetailResult>(`/products/${encodeURIComponent(slug)}`).subscribe({
      next: (result) => {
        if (!result.product) {
          if (result.redirectSlug) {
            this.router.navigate(['/product', result.redirectSlug], { replaceUrl: true });
            return;
          }

          this.product.set(null);
          this.loading.set(false);
          return;
        }

        this.product.set(result.product);
        this.selection.set({});
        this.qty.set(1);
        this.activeImage.set(this.firstImage(result.product));
        this.loading.set(false);
        this.applySeo(result.product);
      },
      error: () => {
        this.product.set(null);
        this.loading.set(false);
      }
    });
  }

  private firstImage(product: ProductDetail): string | null {
    const images = [...product.images].sort((a, b) => a.sortOrder - b.sortOrder);
    return images[0]?.url ?? null;
  }

  private applySeo(product: ProductDetail): void {
    const origin = this.seo.origin();
    const path = `/product/${product.slug}`;
    const images = [...product.images].sort((a, b) => a.sortOrder - b.sortOrder).map((image) => mediaUrl(image.url));
    const prices = product.variants.map((variant) => variant.effectivePrice).filter((price) => price > 0);
    const anyStock = product.variants.some((variant) => variant.inStock);
    const description = product.seoDesc || product.shortDesc || `Buy ${product.name} at Rocky Telecom, Sadullapur, Gaibandha.`;

    const availability = product.isPreOrder
      ? 'https://schema.org/PreOrder'
      : anyStock
        ? 'https://schema.org/InStock'
        : 'https://schema.org/OutOfStock';

    const productLd: Record<string, unknown> = {
      '@context': 'https://schema.org',
      '@type': 'Product',
      name: product.name,
      description,
      image: images,
      sku: product.variants[0]?.sku,
      ...(product.brand ? { brand: { '@type': 'Brand', name: product.brand.name } } : {}),
      itemCondition:
        product.type === 'used' ? 'https://schema.org/UsedCondition' : 'https://schema.org/NewCondition'
    };

    if (prices.length > 0) {
      productLd['offers'] = {
        '@type': 'AggregateOffer',
        priceCurrency: 'BDT',
        lowPrice: Math.min(...prices),
        highPrice: Math.max(...prices),
        offerCount: product.variants.length,
        availability,
        url: `${origin}${path}`
      };
    }

    if (product.rating.count > 0 && product.rating.average) {
      productLd['aggregateRating'] = {
        '@type': 'AggregateRating',
        ratingValue: product.rating.average,
        reviewCount: product.rating.count
      };
    }

    const crumbs = [
      { name: 'Home', url: `${origin}/` },
      ...(product.category ? [{ name: product.category.name, url: `${origin}/category/${product.category.slug}` }] : []),
      { name: product.name, url: `${origin}${path}` }
    ];

    this.seo.set({
      title: product.seoTitle || `${product.name} price in Bangladesh`,
      description,
      path,
      image: images[0] ?? null,
      type: 'product',
      jsonLd: [
        ...(this.buildAbout(product).faq.length
          ? [
              {
                '@context': 'https://schema.org',
                '@type': 'FAQPage',
                mainEntity: this.buildAbout(product).faq.map((item) => ({
                  '@type': 'Question',
                  name: item.q,
                  acceptedAnswer: { '@type': 'Answer', text: item.a }
                }))
              }
            ]
          : []),
        productLd,
        {
          '@context': 'https://schema.org',
          '@type': 'BreadcrumbList',
          itemListElement: crumbs.map((crumb, index) => ({
            '@type': 'ListItem',
            position: index + 1,
            name: crumb.name,
            item: crumb.url
          }))
        }
      ]
    });
  }

  private pickVariant(): Variant | null {
    const product = this.product();
    if (!product || product.variants.length === 0) {
      return null;
    }

    const selection = this.selection();
    const axes: Array<keyof Variant> = ['color', 'ram', 'storage', 'region', 'simType'];

    const exact = product.variants.find((variant) =>
      axes.every((axis) => {
        const wanted = selection[axis as string];
        return !wanted || !variant[axis] || String(variant[axis]) === wanted;
      })
    );

    return exact ?? product.variants.find((variant) => variant.inStock) ?? product.variants[0];
  }

  private buildOptions() {
    const product = this.product();
    if (!product) {
      return [] as { axis: string; selected: string | null; values: { value: string; hex: string | null; available: boolean }[] }[];
    }

    const axes: { key: keyof Variant; label: string }[] = [
      { key: 'color', label: 'Colour' },
      { key: 'ram', label: 'Memory' },
      { key: 'storage', label: 'Storage' },
      { key: 'region', label: 'Region' },
      { key: 'simType', label: 'SIM' }
    ];

    const selection = this.selection();

    return axes
      .map((axis) => {
        const values = new Map<string, { hex: string | null; available: boolean }>();

        for (const variant of product.variants) {
          const value = variant[axis.key];
          if (!value) continue;
          const key = String(value);
          const hex = axis.key === 'color' ? variant.colorHex : null;
          const existing = values.get(key);
          values.set(key, { hex: existing?.hex ?? hex, available: (existing?.available ?? false) || variant.inStock });
        }

        return {
          axis: axis.label,
          selected: selection[axis.key as string] ?? (this.selected()?.[axis.key] ? String(this.selected()![axis.key]) : null),
          values: [...values.entries()]
            .map(([value, meta]) => ({ value, hex: meta.hex, available: meta.available }))
            .sort((x, y) => (axis.key === 'ram' || axis.key === 'storage' ? compareSizes(x.value, y.value) : 0))
        };
      })
      .filter((group) => group.values.length > 0);
  }

  selectOption(axisLabel: string, value: string): void {
    const map: Record<string, keyof Variant> = {
      Colour: 'color',
      Memory: 'ram',
      Storage: 'storage',
      Region: 'region',
      SIM: 'simType'
    };

    const key = map[axisLabel];
    if (!key) return;

    this.selection.set({ ...this.selection(), [key]: value });
    this.qty.set(1);
  }

  changeQty(delta: number): void {
    const max = Math.max(1, Math.min(10, this.selected()?.stock ?? 1));
    this.qty.set(Math.min(max, Math.max(1, this.qty() + delta)));
  }

  addToCart(): void {
    const variant = this.selected();
    if (!variant) return;

    this.adding.set(true);
    this.cart.add(variant.id, this.qty()).subscribe({
      next: () => this.adding.set(false),
      error: (error) => {
        this.adding.set(false);
        this.toast.show(toApiError(error).message, 'error');
      }
    });
  }

  buyNow(): void {
    const variant = this.selected();
    if (!variant) return;

    this.cart.add(variant.id, this.qty()).subscribe({
      next: () => this.router.navigate(['/checkout']),
      error: (error) => this.toast.show(toApiError(error).message, 'error')
    });
  }

  label(type: string): string {
    return type === 'official' ? 'Official' : type === 'unofficial' ? 'Unofficial' : 'Used';
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }
}
