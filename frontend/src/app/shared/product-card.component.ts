import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { mediaUrl } from '../core/api.service';
import { CompareService } from '../core/compare.service';
import { ProductCard } from '../core/models';
import { WishlistService } from '../core/wishlist.service';
import { BdtPipe, CleanNamePipe } from '../core/ui';

@Component({
  selector: 'rt-product-card',
  standalone: true,
  imports: [RouterLink, BdtPipe, CleanNamePipe],
  template: `
    <article class="rt-product-card">
      <div class="thumb">
        @if (product().badge) {
          <span class="badge rt-tag rt-tag--rust">{{ product().badge }}</span>
        } @else if (product().type === 'used') {
          <span class="badge rt-tag rt-tag--paper">Used · {{ product().condition }}</span>
        } @else if (product().isPreOrder) {
          <span class="badge rt-tag rt-tag--teal">Pre-order</span>
        }

        @if (product().discountPercent > 0) {
          <span class="discount">-{{ product().discountPercent }}%</span>
        }

        <div class="quick-actions">
          <button
            type="button"
            class="quick-btn"
            [class.is-on]="wishlist.has(product().id)"
            [attr.aria-pressed]="wishlist.has(product().id)"
            [attr.aria-label]="wishlist.has(product().id) ? 'Remove from wishlist' : 'Add to wishlist'"
            (click)="wishlist.toggle(product())"
          >
            {{ wishlist.has(product().id) ? '♥' : '♡' }}
          </button>
          <button
            type="button"
            class="quick-btn"
            [class.is-on]="compare.has(product().slug)"
            [attr.aria-pressed]="compare.has(product().slug)"
            [attr.aria-label]="compare.has(product().slug) ? 'Remove from compare' : 'Add to compare'"
            (click)="compare.toggle(product().slug)"
          >
            ⇄
          </button>
        </div>

        @if (product().thumbUrl) {
          <img [src]="media(product().thumbUrl)" [alt]="product().name | cleanName" loading="lazy" />
        } @else {
          <div class="placeholder" aria-hidden="true">
            <span class="rt-tag rt-tag--paper">{{ product().brandName || 'Rocky' }}</span>
            <strong>{{ (product().name | cleanName).slice(0, 14) }}</strong>
          </div>
        }
      </div>

      <div class="body">
        <span class="mono-label">{{ product().brandName || 'Rocky Telecom' }}</span>
        @if (product().reviewCount > 0 && product().averageRating) {
          <span class="tiny" [attr.aria-label]="product().averageRating + ' out of 5 stars, ' + product().reviewCount + ' reviews'">
            <span style="color:var(--mustard,#e6a817);">★</span> {{ product().averageRating }}
            <span class="muted">({{ product().reviewCount }})</span>
          </span>
        }

        <a class="name" [routerLink]="['/product', product().slug]">{{ product().name | cleanName }}</a>

        <div class="rt-row rt-row--between" style="margin-top:auto;">
          <div>
            <div class="rt-price">{{ product().effectivePrice | bdt }}</div>
            @if (product().salePrice && product().salePrice! < product().regularPrice) {
              <div class="tiny muted"><s>{{ product().regularPrice | bdt }}</s></div>
            }
          </div>

          @if (product().inStock) {
            <span class="stock-in">In stock</span>
          } @else {
            <span class="stock-out">Sold out</span>
          }
        </div>

        <a class="rt-btn rt-btn--ghost rt-btn--sm rt-btn--block" [routerLink]="['/product', product().slug]">
          View details →
        </a>
      </div>
    </article>
  `,
  styles: [
    `
      .quick-actions {
        position: absolute;
        top: 8px;
        right: 8px;
        z-index: 2;
        display: flex;
        flex-direction: column;
        gap: 6px;
      }

      .quick-btn {
        width: 44px;
        height: 44px;
        border-radius: 999px;
        border: 1px solid var(--line, rgba(0, 0, 0, 0.15));
        background: rgba(255, 255, 255, 0.92);
        color: inherit;
        font-size: 18px;
        line-height: 1;
        cursor: pointer;
      }

      .quick-btn.is-on {
        background: var(--rust, #965c20);
        color: #fff;
        border-color: transparent;
      }

      .placeholder {
        height: 100%;
        display: grid;
        place-items: center;
        gap: 6px;
        text-align: center;
        padding: 12px;
        font-family: var(--font-display);
        color: var(--ink-soft);
        font-size: 15px;
        line-height: 1.1;
      }
    `
  ]
})
export class ProductCardComponent {
  readonly product = input.required<ProductCard>();
  readonly wishlist = inject(WishlistService);
  readonly compare = inject(CompareService);

  media(path: string | null): string {
    return mediaUrl(path);
  }
}
