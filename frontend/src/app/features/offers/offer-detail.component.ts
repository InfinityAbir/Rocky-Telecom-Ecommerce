import { isPlatformBrowser } from '@angular/common';
import { Component, OnDestroy, OnInit, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService, mediaUrl, parseUtc } from '../../core/api.service';
import { SeoService } from '../../core/seo.service';
import { OfferDetail } from '../../core/models';
import { ProductCardComponent } from '../../shared/product-card.component';

/** /offer/{slug} — one live campaign with a countdown (when it has an end) and its products. */
@Component({
  selector: 'rt-offer-detail',
  standalone: true,
  imports: [RouterLink, ProductCardComponent],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <nav class="small" aria-label="Breadcrumb">
        <a routerLink="/">Home</a> / <a routerLink="/offer">Offers</a>
      </nav>

      @if (offer(); as o) {
        @if (o.bannerUrl) {
          <img class="banner" [src]="media(o.bannerUrl)" [alt]="o.title" />
        }

        <div class="rt-row rt-row--between rt-row--wrap" style="align-items:flex-end; margin-top:16px;">
          <div>
            @if (o.subtitle) {
              <div class="kicker mono-label">{{ o.subtitle }}</div>
            }
            <h1 style="margin-bottom:8px;">{{ o.title }}</h1>
          </div>

          @if (remaining(); as r) {
            <div class="rt-countdown" role="timer" aria-live="off" [attr.aria-label]="'Ends in ' + r.label">
              @if (r.days > 0) {
                <span class="unit">{{ r.days }}<small>DAYS</small></span>
              }
              <span class="unit">{{ pad(r.hours) }}<small>HRS</small></span>
              <span class="unit">{{ pad(r.minutes) }}<small>MIN</small></span>
              <span class="unit">{{ pad(r.seconds) }}<small>SEC</small></span>
            </div>
          }
        </div>

        @for (paragraph of paragraphs(); track $index) {
          <p>{{ paragraph }}</p>
        }

        @if (o.items.length === 0) {
          <div class="rt-panel center" style="padding:32px 20px;">
            <h3 class="mt-0">Products are being added</h3>
            <a class="rt-btn rt-btn--ghost" routerLink="/shop">Browse the shop</a>
          </div>
        } @else {
          <div class="rt-grid rt-cols-4" style="margin-top:16px;">
            @for (product of o.items; track product.id) {
              <rt-product-card [product]="product" />
            }
          </div>
        }
      } @else if (loading()) {
        <div class="rt-skeleton" style="height:240px; margin-top:16px;"></div>
      } @else {
        <div class="rt-panel center" style="padding:40px 20px; margin-top:16px;">
          <h3 class="mt-0">This offer isn't available</h3>
          <p>It may have ended or not started yet.</p>
          <a class="rt-btn rt-btn--ghost" routerLink="/offer">See current offers</a>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .banner { width: 100%; aspect-ratio: 1600 / 520; object-fit: cover; border-radius: 12px; margin-top: 16px; }
    `
  ]
})
export class OfferDetailComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly seo = inject(SeoService);
  private readonly destroy$ = new Subject<void>();
  private timer?: ReturnType<typeof setInterval>;

  readonly offer = signal<OfferDetail | null>(null);
  readonly loading = signal(true);
  private readonly now = signal(Date.now());

  readonly paragraphs = computed(() =>
    (this.offer()?.description ?? '')
      .split(/\n+/)
      .map((line) => line.trim())
      .filter(Boolean)
  );

  readonly remaining = computed(() => {
    const end = this.offer()?.endAt;
    if (!end) {
      return null;
    }

    const ms = parseUtc(end).getTime() - this.now();
    if (ms <= 0) {
      return null;
    }

    const total = Math.floor(ms / 1000);
    const days = Math.floor(total / 86400);
    const hours = Math.floor((total % 86400) / 3600);
    const minutes = Math.floor((total % 3600) / 60);
    const seconds = total % 60;

    return { days, hours, minutes, seconds, label: `${days} days ${hours} hours ${minutes} minutes` };
  });

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((params) => this.load(params.get('slug') ?? ''));

    if (isPlatformBrowser(this.platformId)) {
      this.timer = setInterval(() => this.now.set(Date.now()), 1000);
    }
  }

  ngOnDestroy(): void {
    if (this.timer) {
      clearInterval(this.timer);
    }

    this.destroy$.next();
    this.destroy$.complete();
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }

  pad(value: number): string {
    return String(value).padStart(2, '0');
  }

  private load(slug: string): void {
    this.loading.set(true);
    this.offer.set(null);

    this.api.get<OfferDetail>(`/offers/${encodeURIComponent(slug)}`).subscribe({
      next: (offer) => {
        this.offer.set(offer);
        this.loading.set(false);
        this.seo.set({
          title: offer.title,
          description: offer.subtitle ?? offer.description,
          path: `/offer/${offer.slug}`,
          image: offer.bannerUrl ? mediaUrl(offer.bannerUrl) : null
        });
      },
      error: () => this.loading.set(false)
    });
  }
}
