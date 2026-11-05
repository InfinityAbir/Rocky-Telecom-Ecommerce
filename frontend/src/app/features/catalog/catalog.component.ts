import { Component, OnDestroy, OnInit, inject, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, debounceTime, takeUntil } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { SeoService } from '../../core/seo.service';
import { Brand, ProductListResult } from '../../core/models';
import { ProductCardComponent } from '../../shared/product-card.component';

/**
 * One listing component serves /shop, /search, /category/:slug and /brands/:slug. All filter
 * state lives in the URL query string, so every filtered view is shareable and SSR-renderable.
 * Facet options (brand, colour, RAM, storage, price range) come from the listing response and
 * are recomputed by the API for the current category/brand.
 */
/** Fallback colours by keyword for variants stored without a hex code. */
const NAME_HUES: [string, string][] = [
  ['black', '#1c1c1e'], ['obsidian', '#1c1c1e'], ['onyx', '#1c1c1e'], ['graphite', '#3a3a3c'], ['midnight', '#1d2433'],
  ['white', '#f4f1ea'], ['porcelain', '#f1ece4'], ['cream', '#f2e8d0'], ['silver', '#c9ccd1'], ['gray', '#8e8e93'], ['grey', '#8e8e93'],
  ['titanium', '#8f8a82'], ['gold', '#d7b56d'], ['yellow', '#f2c94c'], ['orange', '#f2994a'], ['red', '#d64545'],
  ['pink', '#f2a1b8'], ['rose', '#e6a6a6'], ['purple', '#8e6bbf'], ['lavender', '#b9a7e0'], ['violet', '#7b5ea7'],
  ['blue', '#3b6fd4'], ['navy', '#1f3a6e'], ['teal', '#2a8f8f'], ['mint', '#9fd8c3'], ['green', '#3f9a5a'],
  ['bronze', '#a77b4f'], ['beige', '#d8c7a6'], ['brown', '#7a5a3a']
];

@Component({
  selector: 'rt-catalog',
  standalone: true,
  imports: [FormsModule, RouterLink, ProductCardComponent],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="rt-row rt-row--between rt-row--wrap" style="align-items:flex-end;">
        <div>
          <div class="kicker mono-label">{{ heading() }}</div>
          <h1 style="margin-bottom:8px;">{{ title() }}</h1>
          <p class="mb-0">
            @if (result()) {
              {{ result()!.page.total }} product(s) · prices include VAT · server-verified stock
            } @else {
              Loading the shelves…
            }
          </p>
        </div>

        <div class="rt-row" style="gap:10px;">
          <button
            type="button"
            class="rt-btn rt-btn--ghost rt-btn--sm filters-toggle"
            [attr.aria-expanded]="filtersOpen()"
            aria-controls="filter-rail"
            (click)="filtersOpen.set(!filtersOpen())"
          >
            Filters{{ activeFilterCount() > 0 ? ' (' + activeFilterCount() + ')' : '' }}
          </button>
          <label class="mono-label" for="sort">Sort</label>
          <select id="sort" class="rt-select" style="min-width:180px;" [ngModel]="sort()" (ngModelChange)="setSort($event)">
            <option value="newest">Newest first</option>
            <option value="price-asc">Price: low to high</option>
            <option value="price-desc">Price: high to low</option>
            <option value="popularity">Most popular</option>
            <option value="discount">Biggest discount</option>
          </select>
        </div>
      </div>

      <div class="catalog-layout">
        <!-- FILTER RAIL -->
        <aside id="filter-rail" class="filters rt-panel rt-panel--flat" [class.is-open]="filtersOpen()">
          <div class="rt-row rt-row--between">
            <strong>Filters</strong>
            <button class="mono-label" type="button" (click)="clearFilters()" style="background:none;border:0;cursor:pointer;">
              Reset
            </button>
          </div>

          <div class="rt-dashed"></div>

          @if (brands().length > 0 && !lockedBrand) {
            <span class="mono-label">Brand</span>
            <div class="filter-chips">
              @for (brand of brands(); track brand.id) {
                <button
                  type="button"
                  class="rt-chip"
                  [class.is-active]="selectedBrand() === brand.slug"
                  (click)="toggleBrand(brand.slug)"
                >
                  {{ brand.name }}
                </button>
              }
            </div>
            <div class="rt-dashed"></div>
          }

          <span class="mono-label">Price (BDT)</span>
          <div class="rt-row" style="gap:8px; margin-top:8px;">
            <input
              class="rt-input"
              type="number"
              min="0"
              inputmode="numeric"
              [attr.placeholder]="priceHint('min')"
              aria-label="Minimum price"
              [(ngModel)]="minPrice"
              (change)="applyFilters()"
            />
            <input
              class="rt-input"
              type="number"
              min="0"
              inputmode="numeric"
              [attr.placeholder]="priceHint('max')"
              aria-label="Maximum price"
              [(ngModel)]="maxPrice"
              (change)="applyFilters()"
            />
          </div>

          <div class="rt-dashed"></div>

          @for (group of facetGroups(); track group.key) {
            @if (group.options.length > 0 && group.key === 'color') {
              <div class="rt-row rt-row--between">
                <span class="mono-label">{{ group.label }}</span>
                @if (group.selected.length) {
                  <span class="tiny muted">{{ group.selected.length }} selected</span>
                }
              </div>
              <div class="swatches" role="group" aria-label="Colour">
                @for (family of visibleFamilies(); track family.hex) {
                  <button
                    type="button"
                    class="swatch"
                    [class.is-active]="familyActive(family)"
                    [style.--sw]="family.hex"
                    [attr.aria-pressed]="familyActive(family)"
                    [attr.aria-label]="family.names.join(', ')"
                    [attr.title]="family.names.join(', ')"
                    (click)="toggleFamily(family)"
                  ></button>
                }
              </div>
              @if (group.selected.length) {
                <div class="tiny muted" style="margin-top:6px;">{{ group.selected.length }} shade(s) selected</div>
              }
              @if (colorFamilies().length > colorLimit) {
                <button type="button" class="more" (click)="showAllColors.set(!showAllColors())">
                  {{ showAllColors() ? 'Show fewer colours' : 'Show all ' + colorFamilies().length + ' colours' }}
                </button>
              }
              <div class="rt-dashed"></div>
            } @else if (group.options.length > 0) {
              <span class="mono-label">{{ group.label }}</span>
              <div class="filter-chips">
                @for (option of group.options; track option) {
                  <button
                    type="button"
                    class="rt-chip"
                    [class.is-active]="group.selected.includes(option)"
                    [attr.aria-pressed]="group.selected.includes(option)"
                    (click)="toggleFacet(group.key, option)"
                  >
                    {{ option }}
                  </button>
                }
              </div>
              <div class="rt-dashed"></div>
            }
          }

          <label class="rt-row" style="gap:10px; cursor:pointer; align-items:center;">
            <input type="checkbox" class="rt-checkbox" [ngModel]="inStockOnly()" (ngModelChange)="setInStock($event)" />
            <span style="font-weight:600; font-size:14px; user-select:none;">In stock only</span>
          </label>

          <label class="rt-row" style="gap:10px; cursor:pointer; align-items:center; margin-top:10px;">
            <input type="checkbox" class="rt-checkbox" [ngModel]="onOffer()" (ngModelChange)="setOnOffer($event)" />
            <span style="font-weight:600; font-size:14px; user-select:none;">On offer</span>
          </label>

          <div class="rt-dashed"></div>

          <span class="mono-label">Quick shelves</span>
          <div class="filter-chips">
            <a class="rt-chip" routerLink="/shop" [queryParams]="{ sort: 'discount' }">Deals</a>
            <a class="rt-chip" routerLink="/shop" [queryParams]="{ sort: 'newest' }">New</a>
            <a class="rt-chip" routerLink="/brands">All brands</a>
          </div>
        </aside>

        <!-- RESULTS -->
        <div>
          @if (loading()) {
            <div class="rt-grid rt-cols-3">
              @for (n of [1, 2, 3, 4, 5, 6]; track n) {
                <div class="rt-skeleton" style="height:330px;"></div>
              }
            </div>
          } @else if (items().length === 0) {
            <div class="rt-panel center" style="padding:40px 20px;">
              <h3 class="mt-0">Nothing on this shelf yet</h3>
              <p>Try a different filter, or browse the whole catalogue.</p>
              <button class="rt-btn rt-btn--ghost" type="button" (click)="clearFilters()">Clear filters</button>
            </div>
          } @else {
            <div class="rt-grid rt-cols-3">
              @for (product of items(); track product.id) {
                <rt-product-card [product]="product" />
              }
            </div>

            @if (result(); as page) {
              @if (page.page.totalPages > 1) {
                <nav class="pager" aria-label="Pagination">
                  <button class="rt-btn rt-btn--ghost rt-btn--sm" [disabled]="page.page.page <= 1" (click)="goTo(page.page.page - 1)">
                    ← Prev
                  </button>
                  <span class="mono-label">Page {{ page.page.page }} / {{ page.page.totalPages }}</span>
                  <button
                    class="rt-btn rt-btn--ghost rt-btn--sm"
                    [disabled]="page.page.page >= page.page.totalPages"
                    (click)="goTo(page.page.page + 1)"
                  >
                    Next →
                  </button>
                </nav>
              }
            }
          }

          @if (aboutText(); as about) {
            <section class="seo-copy rt-panel rt-panel--flat" aria-label="About this shelf">
              <h2 class="mt-0">{{ title() }} in Bangladesh</h2>
              <div class="copy" [class.is-open]="aboutOpen()" [id]="'seo-copy'">
                @for (paragraph of about; track paragraph) {
                  <p>{{ paragraph }}</p>
                }
              </div>
              <button type="button" class="read-more" [attr.aria-expanded]="aboutOpen()" aria-controls="seo-copy" (click)="aboutOpen.set(!aboutOpen())">
                {{ aboutOpen() ? 'Read less' : 'Read more' }}
              </button>
            </section>
          }
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .catalog-layout {
        display: grid;
        grid-template-columns: 250px 1fr;
        gap: 22px;
        margin-top: 22px;
        align-items: start;
      }

      /* A long rail scrolls on its own so the page never has to scroll past the filters. */
      .filters { position: sticky; top: 92px; max-height: calc(100vh - 112px); overflow-y: auto; overscroll-behavior: contain; }

      .swatches { display: grid; grid-template-columns: repeat(auto-fill, minmax(30px, 1fr)); gap: 8px; margin-top: 10px; }
      .swatch {
        width: 30px; height: 30px; padding: 0; border-radius: 50%;
        border: 2px solid var(--ink); background: var(--sw, #bbb);
        box-shadow: inset 0 0 0 2px var(--cream); cursor: pointer; position: relative;
        transition: transform var(--dur-1) var(--ease);
      }
      .swatch:hover { transform: scale(1.12); }
      .swatch:focus-visible { outline: 3px solid var(--rust); outline-offset: 2px; }
      .swatch.is-active { box-shadow: 0 0 0 3px var(--cream), 0 0 0 5px var(--rust); }
      .swatch.is-active::after {
        content: '✓'; position: absolute; inset: 0; display: grid; place-items: center;
        font-size: 14px; font-weight: 800; color: #fff; text-shadow: 0 0 3px #000;
      }
      .more { margin-top: 8px; background: none; border: 0; padding: 4px 0; min-height: 32px; font: inherit; font-weight: 700; font-size: 13px; color: var(--rust); cursor: pointer; text-decoration: underline; text-underline-offset: 3px; }

      .filters-toggle { display: none; }

      .filter-chips { display: flex; flex-wrap: wrap; gap: 7px; margin-top: 8px; }

      .seo-copy { margin-top: 28px; padding: 20px 22px; }
      .seo-copy .copy { line-height: 1.65; max-height: 4.9em; overflow: hidden; position: relative; }
      .seo-copy .copy:not(.is-open)::after {
        content: ''; position: absolute; left: 0; right: 0; bottom: 0; height: 2.2em;
        background: linear-gradient(to bottom, transparent, var(--paper));
      }
      .seo-copy .copy.is-open { max-height: none; }
      .seo-copy .copy p { margin: 0 0 10px; }
      .read-more {
        margin-top: 8px; padding: 0; min-height: 44px; background: none; border: 0; font: inherit;
        font-weight: 700; color: var(--rust); cursor: pointer; text-decoration: underline; text-underline-offset: 3px;
      }

      .pager {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 16px;
        margin-top: 26px;
      }

      @media (max-width: 900px) {
        .catalog-layout { grid-template-columns: 1fr; }
        .filters { position: static; display: none; }
        .filters.is-open { display: block; }
        .filters-toggle { display: inline-flex; min-height: 44px; }
      }
    `
  ]
})
export class CatalogComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly seo = inject(SeoService);
  private readonly destroy$ = new Subject<void>();
  private readonly load$ = new Subject<void>();

  readonly result = signal<ProductListResult | null>(null);
  readonly loading = signal(true);
  readonly sort = signal('newest');
  readonly inStockOnly = signal(false);
  readonly onOffer = signal(false);
  readonly selectedBrand = signal('');
  readonly selectedColor = signal<string[]>([]);
  readonly selectedRam = signal<string[]>([]);
  readonly selectedStorage = signal<string[]>([]);
  readonly filtersOpen = signal(false);
  readonly heading = signal('Catalogue');
  readonly title = signal('Everything on the shelves');

  minPrice: number | null = null;
  maxPrice: number | null = null;

  /** True on /brands/:slug where the brand is part of the path, so the brand rail is hidden. */
  lockedBrand = false;

  private q = '';
  private categorySlug = '';
  private pathBrandSlug = '';
  private page = 1;

  readonly items = () => this.result()?.page.items ?? [];
  readonly brands = () => this.result()?.filters.brands ?? ([] as Brand[]);

  readonly aboutOpen = signal(false);

  /** Original shop copy for category and brand pages, written from what is actually on the shelf. */
  aboutText(): string[] | null {
    const result = this.result();
    if ((!this.categorySlug && !this.pathBrandSlug) || !result || result.page.total === 0) {
      return null;
    }

    const name = this.title();
    const total = result.page.total;
    const brands = result.filters.brands.map((b) => b.name);
    const bdt = (value: number) => '৳' + Math.round(value).toLocaleString('en-IN');
    const shown = brands.slice(0, 8).join(', ');
    const more = brands.length > 8 ? ` and ${brands.length - 8} more` : '';

    const paragraphs = [
      `Looking for ${name} in Bangladesh? Rocky Telecom in Sadullapur, Gaibandha has ${total} ${total === 1 ? 'product' : 'products'} on this shelf${brands.length ? `, from ${shown}${more}` : ''}. Every item is genuine and comes with the warranty shown on its page.`
    ];

    if (result.filters.maxPrice > 0) {
      paragraphs.push(
        `Prices run from ${bdt(result.filters.minPrice)} to ${bdt(result.filters.maxPrice)} and already include VAT. Use the filters to narrow by brand, colour, memory, storage and price, or open the compare button on any card to see two products side by side.`
      );
    }

    paragraphs.push(
      'Stock is checked live, so a product you can add to the cart is a product we have. Pay in cash, by mobile banking or on EMI with the listed banks; pick it up at the showroom within two hours, or have it delivered to your door anywhere in Bangladesh.'
    );

    return paragraphs;
  }

  readonly colorLimit = 12;
  readonly showAllColors = signal(false);

  /**
   * Marketing names ("Awesome Lilac", "Arctic Dawn") collapse into one swatch per actual colour, so a
   * rail of 67 names becomes a short palette. Hover shows the names; clicking picks all of them.
   */
  readonly colorFamilies = computed(() => {
    const options = this.result()?.filters.color ?? [];
    const groups = new Map<string, string[]>();

    for (const name of options) {
      const hex = this.swatchColor(name);
      groups.set(hex, [...(groups.get(hex) ?? []), name]);
    }

    return [...groups.entries()]
      .map(([hex, names]) => ({ hex, names }))
      .sort((a, b) => b.names.length - a.names.length);
  });

  /** First dozen swatches, plus any family with an active choice so a selection never hides. */
  readonly visibleFamilies = computed(() => {
    const all = this.colorFamilies();
    if (this.showAllColors() || all.length <= this.colorLimit) {
      return all;
    }

    const selected = this.selectedColor();
    const head = all.slice(0, this.colorLimit);
    return [...head, ...all.slice(this.colorLimit).filter((f) => f.names.some((n) => selected.includes(n)))];
  });

  familyActive(family: { names: string[] }): boolean {
    const selected = this.selectedColor();
    return family.names.every((n) => selected.includes(n));
  }

  toggleFamily(family: { names: string[] }): void {
    const current = this.selectedColor();
    this.selectedColor.set(
      this.familyActive(family)
        ? current.filter((n) => !family.names.includes(n))
        : [...new Set([...current, ...family.names])]
    );
    this.page = 1;
    this.syncUrl();
  }

  /** Real hex from the catalogue when known, otherwise a best guess from the colour's name. */
  swatchColor(name: string): string {
    const hex = this.result()?.filters.colorSwatches?.[name];
    if (hex) {
      return hex;
    }

    const lower = name.toLowerCase();
    const guess = NAME_HUES.find(([word]) => lower.includes(word));
    return guess ? guess[1] : '#b9b2a6';
  }

  readonly facetGroups = () => {
    const filters = this.result()?.filters;
    return [
      { key: 'color' as const, label: 'Colour', options: filters?.color ?? [], selected: this.selectedColor() },
      { key: 'ram' as const, label: 'RAM', options: filters?.ram ?? [], selected: this.selectedRam() },
      { key: 'storage' as const, label: 'Storage', options: filters?.storage ?? [], selected: this.selectedStorage() }
    ];
  };

  readonly activeFilterCount = () =>
    (this.selectedBrand() && !this.lockedBrand ? 1 : 0) +
    this.selectedColor().length +
    this.selectedRam().length +
    this.selectedStorage().length +
    (this.inStockOnly() ? 1 : 0) +
    (this.onOffer() ? 1 : 0) +
    (this.minPrice ? 1 : 0) +
    (this.maxPrice ? 1 : 0);

  constructor() {
    this.load$.pipe(debounceTime(120), takeUntil(this.destroy$)).subscribe(() => this.fetch());
  }

  ngOnInit(): void {
    // Path params and query params change independently; one read keeps the view consistent.
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe(() => this.readRoute());
    this.route.queryParamMap.pipe(takeUntil(this.destroy$)).subscribe(() => this.readRoute());
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  priceHint(which: 'min' | 'max'): string {
    const filters = this.result()?.filters;
    if (!filters) {
      return which === 'min' ? 'Min' : 'Max';
    }

    return String(Math.round(which === 'min' ? filters.minPrice : filters.maxPrice));
  }

  private readRoute(): void {
    const path = this.route.snapshot.routeConfig?.path ?? '';
    const params = this.route.snapshot.paramMap;
    const query = this.route.snapshot.queryParamMap;

    this.categorySlug = '';
    this.pathBrandSlug = '';
    this.q = '';
    this.lockedBrand = false;

    if (path.startsWith('categor')) {
      this.categorySlug = params.get('slug') ?? '';
      this.heading.set('Category');
      this.title.set(this.prettify(this.categorySlug));
    } else if (path.startsWith('brands/')) {
      this.pathBrandSlug = params.get('slug') ?? '';
      this.lockedBrand = true;
      this.heading.set('Brand');
      this.title.set(this.prettify(this.pathBrandSlug));
    } else if (path === 'search') {
      this.q = query.get('q') ?? '';
      this.heading.set('Search');
      this.title.set(this.q ? `Results for “${this.q}”` : 'Search the shop');
    } else {
      this.heading.set('Catalogue');
      this.title.set('Everything on the shelves');
    }

    this.applySeo(path);

    this.sort.set(query.get('sort') ?? 'newest');
    this.inStockOnly.set(query.get('inStock') === 'true');
    this.onOffer.set(query.get('onOffer') === 'true');
    this.selectedBrand.set(this.lockedBrand ? this.pathBrandSlug : (query.get('brand') ?? ''));
    this.selectedColor.set(this.list(query.get('color')));
    this.selectedRam.set(this.list(query.get('ram')));
    this.selectedStorage.set(this.list(query.get('storage')));
    this.minPrice = query.get('minPrice') ? Number(query.get('minPrice')) : null;
    this.maxPrice = query.get('maxPrice') ? Number(query.get('maxPrice')) : null;
    this.page = query.get('page') ? Math.max(1, Number(query.get('page'))) : 1;
    this.load$.next();
  }

  /** Category and brand pages are indexable; search results and filtered variants are not separate pages. */
  private applySeo(path: string): void {
    const name = this.title();
    const isSearch = path === 'search';
    const isPlain = path === 'shop' || path === '';

    this.seo.set({
      title: isSearch ? 'Search' : isPlain ? 'Shop phones, laptops and electronics' : `${name} price in Bangladesh`,
      description: isSearch
        ? undefined
        : `Shop ${isPlain ? 'phones, laptops, tablets and accessories' : name} at Rocky Telecom, Sadullapur, Gaibandha. Official warranty, EMI and home delivery.`,
      path: this.route.snapshot.url.length ? `/${this.route.snapshot.url.map((segment) => segment.path).join('/')}` : '/shop',
      noindex: isSearch
    });
  }

  fetch(): void {
    this.loading.set(true);

    this.api
      .get<ProductListResult>('/products', {
        q: this.q || undefined,
        categorySlug: this.categorySlug || undefined,
        brandSlug: this.selectedBrand() || undefined,
        color: this.selectedColor(),
        ram: this.selectedRam(),
        storage: this.selectedStorage(),
        minPrice: this.minPrice ?? undefined,
        maxPrice: this.maxPrice ?? undefined,
        inStockOnly: this.inStockOnly() || undefined,
        onOffer: this.onOffer() || undefined,
        sort: this.sort(),
        page: this.page,
        pageSize: 12
      })
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.loading.set(false);
          if (result.page.total === 0 && this.q) {
            this.title.set(`No matches for “${this.q}”`);
          }
        },
        error: () => this.loading.set(false)
      });
  }

  setSort(sort: string): void {
    this.sort.set(sort);
    this.page = 1;
    this.syncUrl();
  }

  setInStock(value: boolean): void {
    this.inStockOnly.set(value);
    this.page = 1;
    this.syncUrl();
  }

  setOnOffer(value: boolean): void {
    this.onOffer.set(value);
    this.page = 1;
    this.syncUrl();
  }

  toggleBrand(slug: string): void {
    this.selectedBrand.set(this.selectedBrand() === slug ? '' : slug);
    this.page = 1;
    this.syncUrl();
  }

  toggleFacet(key: 'color' | 'ram' | 'storage', option: string): void {
    const target = key === 'color' ? this.selectedColor : key === 'ram' ? this.selectedRam : this.selectedStorage;
    const current = target();
    target.set(current.includes(option) ? current.filter((item) => item !== option) : [...current, option]);
    this.page = 1;
    this.syncUrl();
  }

  applyFilters(): void {
    this.page = 1;
    this.syncUrl();
  }

  clearFilters(): void {
    this.selectedBrand.set(this.lockedBrand ? this.pathBrandSlug : '');
    this.selectedColor.set([]);
    this.selectedRam.set([]);
    this.selectedStorage.set([]);
    this.inStockOnly.set(false);
    this.onOffer.set(false);
    this.minPrice = null;
    this.maxPrice = null;
    this.page = 1;
    this.syncUrl();
  }

  goTo(page: number): void {
    this.page = page;
    this.syncUrl();
    if (typeof window !== 'undefined') {
      window.scrollTo({ top: 0, behavior: 'smooth' });
    }
  }

  private syncUrl(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        sort: this.sort() === 'newest' ? undefined : this.sort(),
        brand: this.lockedBrand ? undefined : this.selectedBrand() || undefined,
        color: this.selectedColor().join(',') || undefined,
        ram: this.selectedRam().join(',') || undefined,
        storage: this.selectedStorage().join(',') || undefined,
        inStock: this.inStockOnly() ? true : undefined,
        onOffer: this.onOffer() ? true : undefined,
        minPrice: this.minPrice || undefined,
        maxPrice: this.maxPrice || undefined,
        page: this.page > 1 ? this.page : undefined
      },
      queryParamsHandling: 'merge'
    });
  }

  private list(value: string | null): string[] {
    return value ? value.split(',').filter(Boolean) : [];
  }

  private prettify(slug: string): string {
    return slug
      .split('-')
      .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
      .join(' ');
  }
}
