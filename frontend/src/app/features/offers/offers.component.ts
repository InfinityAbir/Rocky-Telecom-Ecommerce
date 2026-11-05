import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, mediaUrl, parseUtc } from '../../core/api.service';
import { SeoService } from '../../core/seo.service';
import { OfferSummary } from '../../core/models';

/** /offer — every live campaign. Ended or scheduled offers never reach this list. */
@Component({
  selector: 'rt-offers',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="kicker mono-label">Offers</div>
      <h1 style="margin-bottom:8px;">Offers at the counter</h1>
      <p>Limited-time prices and bundles. Offers disappear when they end.</p>

      @if (loading()) {
        <div class="rt-grid rt-cols-3">
          @for (n of [1, 2, 3]; track n) {
            <div class="rt-skeleton" style="height:220px;"></div>
          }
        </div>
      } @else if (offers().length === 0) {
        <div class="rt-panel center" style="padding:40px 20px;">
          <h3 class="mt-0">No offers running right now</h3>
          <p>Check back soon, or browse today's prices in the shop.</p>
          <a class="rt-btn rt-btn--ghost" routerLink="/shop" [queryParams]="{ sort: 'discount' }">See discounted products</a>
        </div>
      } @else {
        <div class="rt-grid rt-cols-3">
          @for (offer of offers(); track offer.id) {
            <a class="rt-card offer-card" [routerLink]="['/offer', offer.slug]">
              <div class="offer-banner">
                @if (offer.bannerUrl) {
                  <img [src]="media(offer.bannerUrl)" [alt]="offer.title" loading="lazy" />
                } @else {
                  <span class="offer-initial" aria-hidden="true">%</span>
                }
              </div>
              <div class="offer-body">
                <strong>{{ offer.title }}</strong>
                @if (offer.subtitle) {
                  <span class="small">{{ offer.subtitle }}</span>
                }
                <span class="tiny muted">
                  {{ offer.productCount }} product(s) ·
                  {{ offer.endAt ? 'Ends ' + endLabel(offer.endAt) : 'While stock lasts' }}
                </span>
              </div>
            </a>
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      .offer-card { display: grid; text-decoration: none; color: inherit; overflow: hidden; padding: 0; }
      .offer-banner {
        aspect-ratio: 1600 / 520;
        display: grid;
        place-items: center;
        background: linear-gradient(135deg, #efe0c4, #f9efdc);
        border-bottom: 2px solid var(--ink, #101828);
      }
      .offer-banner img { width: 100%; height: 100%; object-fit: cover; }
      .offer-initial { font-family: var(--font-display); font-size: 56px; line-height: 1; }
      .offer-body { display: grid; gap: 4px; padding: 14px 16px 16px; }
    `
  ]
})
export class OffersComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly seo = inject(SeoService);

  readonly offers = signal<OfferSummary[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.seo.set({ title: 'Offers', description: 'Limited-time offers on phones, laptops, audio and accessories at Rocky Telecom.', path: '/offer' });
    this.api.get<OfferSummary[]>('/offers').subscribe({
      next: (offers) => {
        this.offers.set(offers ?? []);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }

  endLabel(iso: string): string {
    return parseUtc(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
  }
}
