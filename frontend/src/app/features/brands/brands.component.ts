import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, mediaUrl } from '../../core/api.service';
import { SeoService } from '../../core/seo.service';
import { Brand } from '../../core/models';

/** Brand index: every active brand links to its filtered listing at /brands/{slug}. */
@Component({
  selector: 'rt-brands',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="kicker mono-label">Brands</div>
      <h1 style="margin-bottom:8px;">Shop by brand</h1>
      <p>Official-warranty handsets and gear from the brands we stock.</p>

      @if (loading()) {
        <div class="rt-grid rt-cols-4">
          @for (n of [1, 2, 3, 4, 5, 6, 7, 8]; track n) {
            <div class="rt-skeleton" style="height:120px;"></div>
          }
        </div>
      } @else if (brands().length === 0) {
        <div class="rt-panel center" style="padding:40px 20px;">
          <h3 class="mt-0">No brands yet</h3>
          <p>Brands appear here once products are published.</p>
          <a class="rt-btn rt-btn--ghost" routerLink="/shop">Browse the catalogue</a>
        </div>
      } @else {
        <div class="rt-grid rt-cols-4 brand-grid">
          @for (brand of brands(); track brand.id) {
            <a class="rt-card brand-tile" [routerLink]="['/brands', brand.slug]">
              @if (brand.logoUrl) {
                <img [src]="media(brand.logoUrl)" [alt]="brand.name" loading="lazy" />
              } @else {
                <span class="brand-initial" aria-hidden="true">{{ brand.name.charAt(0) }}</span>
              }
              <strong>{{ brand.name }}</strong>
            </a>
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      .brand-tile {
        display: grid;
        place-items: center;
        gap: 10px;
        min-height: 120px;
        padding: 16px;
        text-align: center;
        text-decoration: none;
        color: inherit;
      }

      .brand-tile img { max-width: 100%; max-height: 56px; object-fit: contain; }

      .brand-initial {
        font-family: var(--font-display);
        font-size: 36px;
        line-height: 1;
      }
    `
  ]
})
export class BrandsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly seo = inject(SeoService);

  readonly brands = signal<Brand[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.seo.set({ title: 'Shop by brand', description: 'Official-warranty phones, laptops and gear from Apple, Samsung, Xiaomi and more at Rocky Telecom.', path: '/brands' });
    this.api.get<Brand[]>('/brands').subscribe({
      next: (brands) => {
        this.brands.set((brands ?? []).filter((brand) => brand.isActive));
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }
}
