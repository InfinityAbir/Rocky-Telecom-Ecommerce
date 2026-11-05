import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { mediaUrl, toApiError } from '../../core/api.service';
import { CartService } from '../../core/cart.service';
import { ToastService } from '../../core/toast.service';
import { BdtPipe, CleanNamePipe } from '../../core/ui';

@Component({
  selector: 'rt-cart',
  standalone: true,
  imports: [RouterLink, BdtPipe, CleanNamePipe],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="section-head">
        <div>
          <div class="kicker mono-label">Step 1 of 3</div>
          <h1 class="mb-0">Your cart</h1>
        </div>
        <a class="mono-label" routerLink="/shop">← Keep shopping</a>
      </div>

      @if (cart.items().length === 0) {
        <div class="rt-panel center" style="padding:44px 20px;">
          <h2 class="mt-0">Nothing in the basket yet</h2>
          <p>The shelves are stocked — pick something worth carrying home.</p>
          <a class="rt-btn rt-btn--rust" routerLink="/shop">Browse the shop</a>
        </div>
      } @else {
        <div class="cart-layout">
          <div class="rt-stack" style="gap:14px;">
            @for (item of cart.items(); track item.id) {
              <article class="cart-line" [class.is-unavailable]="!item.isAvailable">
                <a class="line-thumb" [routerLink]="['/product', item.productSlug]">
                  @if (item.thumbUrl) {
                    <img [src]="media(item.thumbUrl)" [alt]="item.name | cleanName" />
                  } @else {
                    <span class="rt-tag">RT</span>
                  }
                </a>

                <div class="line-body">
                  <a class="line-name" [routerLink]="['/product', item.productSlug]">{{ item.name | cleanName }}</a>
                  @if (item.variantLabel) {
                    <span class="mono-label">{{ item.variantLabel }}</span>
                  }
                  @if (!item.isAvailable) {
                    <span class="rt-tag rt-tag--rust">Unavailable — remove or reduce</span>
                  } @else if (item.stock <= 3) {
                    <span class="tiny" style="color:var(--warn);">Only {{ item.stock }} left</span>
                  }

                  <div class="rt-row rt-row--between rt-row--wrap" style="margin-top:8px;">
                    <div class="qty">
                      <button type="button" (click)="changeQty(item.id, item.qty - 1)" [disabled]="item.qty <= 1">−</button>
                      <span>{{ item.qty }}</span>
                      <button type="button" (click)="changeQty(item.id, item.qty + 1)" [disabled]="item.qty >= 10">+</button>
                    </div>

                    <div class="rt-row" style="gap:14px;">
                      <div class="rt-price">{{ item.lineTotal | bdt }}</div>
                      <button class="mono-label" type="button" (click)="remove(item.id)" style="background:none;border:0;cursor:pointer;">
                        Remove
                      </button>
                    </div>
                  </div>
                </div>
              </article>
            }
          </div>

          <aside class="rt-panel summary">
            <h3 class="mt-0">Order summary</h3>
            <div class="rt-row rt-row--between small">
              <span>Subtotal</span><strong>{{ cart.subTotal() | bdt }}</strong>
            </div>
            <div class="rt-row rt-row--between small">
              <span>Items</span><strong>{{ cart.count() }}</strong>
            </div>
            <div class="rt-dashed"></div>
            <p class="tiny muted mb-0">
              Delivery is calculated at checkout from your district (3-zone flat rate). Coupons apply there too.
            </p>
            @if (cart.cart().hasUnavailableItems) {
              <p class="rt-error mt-2 mb-0">Some items are out of stock. Adjust them before checking out.</p>
            }
            <a
              class="rt-btn rt-btn--rust rt-btn--block mt-2"
              routerLink="/checkout"
              [class.disabled]="cart.cart().hasUnavailableItems"
            >
              Continue to checkout →
            </a>
            <a class="rt-btn rt-btn--ghost rt-btn--block mt-1" routerLink="/shop">Add more items</a>
          </aside>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .cart-layout {
        display: grid;
        grid-template-columns: 1fr 320px;
        gap: 22px;
        align-items: start;
      }

      .cart-line {
        display: grid;
        grid-template-columns: 110px 1fr;
        gap: 16px;
        background: var(--cream);
        border: 2px solid var(--ink);
        border-radius: 14px;
        box-shadow: var(--shadow-sm);
        padding: 14px;
      }

      .cart-line.is-unavailable { border-color: var(--rust); background: #fbeee7; }

      .line-thumb {
        display: grid;
        place-items: center;
        border: 1.5px solid var(--ink);
        border-radius: 10px;
        background: var(--paper-deep);
        overflow: hidden;
        aspect-ratio: 1;
      }

      .line-thumb img { width: 100%; height: 100%; object-fit: contain; padding: 8px; }

      .line-body { display: flex; flex-direction: column; gap: 4px; }

      .line-name { font-weight: 700; text-decoration: none; }

      .qty {
        display: inline-flex;
        align-items: center;
        border: 2px solid var(--ink);
        border-radius: 8px;
        overflow: hidden;
        background: var(--cream);
      }

      .qty button { width: 36px; height: 36px; border: 0; background: var(--paper-deep); cursor: pointer; font-size: 16px; }
      .qty span { min-width: 38px; text-align: center; font-family: var(--font-mono); font-weight: 700; }

      .summary { position: sticky; top: 92px; }

      a.disabled { pointer-events: none; opacity: 0.5; }

      @media (max-width: 900px) {
        .cart-layout { grid-template-columns: 1fr; }
        .summary { position: static; }
      }
    `
  ]
})
export class CartComponent {
  readonly cart = inject(CartService);
  private readonly toast = inject(ToastService);

  constructor() {
    this.cart.load();
  }

  changeQty(itemId: number, qty: number): void {
    if (qty < 1) return;

    this.cart.update(itemId, qty).subscribe({
      error: (error) => this.toast.show(toApiError(error).message, 'error')
    });
  }

  remove(itemId: number): void {
    this.cart.remove(itemId).subscribe({
      error: (error) => this.toast.show(toApiError(error).message, 'error')
    });
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }
}
