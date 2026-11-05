import { Component, OnInit, inject, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { StoreInfo } from '../../core/models';
import { SeoService } from '../../core/seo.service';

/** /shop-location — the one showroom: address, hours, phone and a map. */
@Component({
  selector: 'rt-store-location',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="kicker mono-label">Visit us</div>
      <h1 style="margin-bottom:8px;">Our showroom</h1>

      @if (store(); as s) {
        <div class="rt-grid rt-cols-2" style="gap:20px; align-items:start;">
          <div class="rt-panel">
            <h2 class="mt-0" style="font-size:24px;">{{ s.name }}</h2>

            <p class="mb-1"><span class="mono-label">Address</span></p>
            <p style="margin-top:0;">{{ s.address }}</p>

            @if (s.openingHours) {
              <p class="mb-1"><span class="mono-label">Opening hours</span></p>
              <p style="margin-top:0;">{{ s.openingHours }}</p>
            }

            <p class="mb-1"><span class="mono-label">Contact</span></p>
            <p style="margin-top:0;">
              <a [href]="'tel:' + s.phone">{{ s.phone }}</a>
              @if (s.email) {
                <br /><a [href]="'mailto:' + s.email">{{ s.email }}</a>
              }
            </p>

            <div class="rt-row rt-row--wrap" style="gap:8px;">
              <a class="rt-btn rt-btn--rust rt-btn--sm" [href]="'tel:' + s.phone">Call the showroom</a>
              @if (directionsUrl(); as url) {
                <a class="rt-btn rt-btn--ghost rt-btn--sm" [href]="url" target="_blank" rel="noopener noreferrer">Get directions</a>
              }
            </div>

            <div class="rt-dashed" style="margin:18px 0;"></div>
            <p class="small mb-0">
              Order online and choose <strong>showroom pickup</strong> at checkout — or bring your phone in for warranty help,
              <a routerLink="/trade-in">trade-in</a> or a <a routerLink="/pre-order">pre-order</a>.
            </p>
          </div>

          @if (mapUrl(); as map) {
            <div class="rt-panel" style="padding:0; overflow:hidden;">
              <iframe
                class="map"
                title="Map showing the Rocky Telecom showroom"
                [src]="map"
                loading="lazy"
                referrerpolicy="no-referrer"
              ></iframe>
              <p class="tiny muted" style="margin:8px 12px;">Map data © OpenStreetMap contributors.</p>
            </div>
          }
        </div>
      } @else if (loading()) {
        <div class="rt-skeleton" style="height:300px;"></div>
      } @else {
        <div class="rt-panel center" style="padding:40px 20px;">
          <h3 class="mt-0">Showroom details are not available</h3>
          <p>Please call or message us.</p>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .map { display: block; width: 100%; height: 360px; border: 0; }
    `
  ]
})
export class StoreLocationComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly seo = inject(SeoService);

  readonly store = signal<StoreInfo | null>(null);
  readonly loading = signal(true);
  readonly mapUrl = signal<SafeResourceUrl | null>(null);
  readonly directionsUrl = signal<string | null>(null);

  ngOnInit(): void {
    this.seo.set({
      title: 'Showroom location',
      description: 'Visit the Rocky Telecom showroom in Sadullapur, Gaibandha — address, opening hours and map.',
      path: '/shop-location'
    });

    this.api.get<StoreInfo | null>('/store').subscribe({
      next: (store) => {
        this.store.set(store);
        this.loading.set(false);

        if (store?.mapLat != null && store.mapLng != null) {
          const lat = Number(store.mapLat);
          const lng = Number(store.mapLng);
          const d = 0.012;
          const bbox = `${lng - d},${lat - d},${lng + d},${lat + d}`;

          // An OpenStreetMap embed: the URL is built from numbers only, so no user text reaches it.
          const embed = `https://www.openstreetmap.org/export/embed.html?bbox=${bbox}&layer=mapnik&marker=${lat},${lng}`;
          this.mapUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(embed));
          this.directionsUrl.set(`https://www.openstreetmap.org/directions?to=${lat}%2C${lng}`);
        }
      },
      error: () => this.loading.set(false)
    });
  }
}
