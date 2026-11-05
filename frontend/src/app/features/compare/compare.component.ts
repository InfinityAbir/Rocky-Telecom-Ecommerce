import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService, mediaUrl, toApiError } from '../../core/api.service';
import { COMPARE_SEPARATOR, CompareService } from '../../core/compare.service';
import { CompareResult } from '../../core/models';
import { BdtPipe, CleanNamePipe } from '../../core/ui';

/**
 * Side-by-side comparison at /product-compare/{a}-vs-{b}. The URL is the source of truth so a
 * compare link can be shared; the server enforces the 4-product and same-category rules.
 */
@Component({
  selector: 'rt-compare',
  standalone: true,
  imports: [RouterLink, BdtPipe, CleanNamePipe],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="kicker mono-label">Compare</div>
      <h1 style="margin-bottom:8px;">Side by side</h1>

      @if (loading()) {
        <div class="rt-skeleton" style="height:320px;"></div>
      } @else if (slugs().length === 0) {
        <div class="rt-panel center" style="padding:40px 20px;">
          <h3 class="mt-0">Nothing to compare yet</h3>
          <p>Tap the ⇄ button on up to {{ max }} products of the same category, then come back here.</p>
          <a class="rt-btn rt-btn--ghost" routerLink="/shop">Browse the catalogue</a>
        </div>
      } @else if (error()) {
        <div class="rt-panel" style="padding:24px 20px;">
          <h3 class="mt-0">We couldn't compare these</h3>
          <p>{{ error() }}</p>
          <button type="button" class="rt-btn rt-btn--ghost" (click)="clearAll()">Start over</button>
        </div>
      } @else {
       @if (result(); as data) {
        <p class="small">
          Differences are highlighted.
          <button type="button" class="link-btn" (click)="copyLink()">Copy share link</button> ·
          <button type="button" class="link-btn" (click)="clearAll()">Clear all</button>
        </p>

        <div class="compare-scroll">
          <table class="compare-table">
            <thead>
              <tr>
                <th scope="col" class="sticky-col"><span class="sr-only">Specification</span></th>
                @for (product of data.products; track product.id) {
                  <th scope="col">
                    <div class="head-card">
                      @if (product.thumbUrl) {
                        <img [src]="media(product.thumbUrl)" [alt]="product.name | cleanName" loading="lazy" />
                      }
                      <a [routerLink]="['/product', product.slug]">{{ product.name | cleanName }}</a>
                      <div class="rt-price">{{ product.effectivePrice | bdt }}</div>
                      @if (product.inStock) {
                        <span class="stock-in">In stock</span>
                      } @else {
                        <span class="stock-out">Sold out</span>
                      }
                      <button type="button" class="link-btn" (click)="remove(product.slug)">Remove</button>
                    </div>
                  </th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of data.rows; track row.label) {
                <tr [class.is-different]="row.isDifferent">
                  <th scope="row" class="sticky-col">{{ row.label }}</th>
                  @for (value of row.values; track $index) {
                    <td>{{ value || '—' }}</td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>

        @if (data.products.length < max) {
          <p style="margin-top:18px;">
            <a class="rt-btn rt-btn--ghost rt-btn--sm" routerLink="/shop">Add another product</a>
          </p>
        }
       }
      }
    </div>
  `,
  styles: [
    `
      .compare-scroll { overflow-x: auto; -webkit-overflow-scrolling: touch; }

      .compare-table { border-collapse: collapse; min-width: 560px; width: 100%; }

      .compare-table th,
      .compare-table td {
        padding: 12px 14px;
        text-align: left;
        vertical-align: top;
        border-bottom: 1px dashed rgba(0, 0, 0, 0.2);
        min-width: 160px;
      }

      .sticky-col {
        position: sticky;
        left: 0;
        z-index: 1;
        background: var(--paper, #fff);
        min-width: 110px;
        font-weight: 700;
      }

      .is-different td { background: rgba(212, 169, 122, 0.22); font-weight: 600; }

      .head-card { display: grid; gap: 6px; }
      .head-card img { width: 100%; max-width: 140px; aspect-ratio: 1; object-fit: contain; }

      .link-btn {
        background: none;
        border: 0;
        padding: 0;
        min-height: 44px;
        color: var(--rust, #965c20);
        text-decoration: underline;
        cursor: pointer;
        font: inherit;
        text-align: left;
      }

      .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip: rect(0 0 0 0);
        white-space: nowrap;
      }
    `
  ]
})
export class CompareComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly compare = inject(CompareService);
  private readonly destroy$ = new Subject<void>();

  readonly max = CompareService.MAX;
  readonly slugs = signal<string[]>([]);
  readonly result = signal<CompareResult | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const raw = params.get('slugs');

      if (!raw && this.compare.slugs().length > 0) {
        // /compare with no slugs uses the shopper's picks, then moves to the canonical,
        // shareable /product-compare/{a}-vs-{b} URL so the address bar and share link match.
        this.router.navigateByUrl(this.compare.urlFor(this.compare.slugs()), { replaceUrl: true });
        return;
      }

      this.load(raw ? raw.split(COMPARE_SEPARATOR).filter(Boolean) : []);
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }

  remove(slug: string): void {
    const remaining = this.slugs().filter((item) => item !== slug);
    this.compare.replace(remaining);
    this.router.navigateByUrl(remaining.length > 0 ? this.compare.urlFor(remaining) : '/compare');
  }

  clearAll(): void {
    this.compare.clear();
    this.router.navigateByUrl('/compare');
  }

  copyLink(): void {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(window.location.href).catch(() => undefined);
    }
  }

  private load(slugs: string[]): void {
    this.slugs.set(slugs);
    this.error.set(null);
    this.result.set(null);

    if (slugs.length === 0) {
      this.loading.set(false);
      return;
    }

    // A shared link becomes this browser's compare list so the ⇄ buttons stay in sync.
    this.compare.replace(slugs);
    this.loading.set(true);

    this.api.get<CompareResult>('/products/compare', { slugs }).subscribe({
      next: (data) => {
        this.loading.set(false);
        if (!data.isValid) {
          this.error.set(data.reason ?? 'These products cannot be compared.');
          return;
        }

        this.result.set(data);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(toApiError(error).message);
      }
    });
  }
}
