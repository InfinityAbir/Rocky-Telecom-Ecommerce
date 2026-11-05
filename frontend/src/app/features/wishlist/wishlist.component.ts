import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { WishlistService } from '../../core/wishlist.service';
import { ProductCardComponent } from '../../shared/product-card.component';

/** Saved products for the signed-in customer. The route is behind the customer guard. */
@Component({
  selector: 'rt-wishlist',
  standalone: true,
  imports: [RouterLink, ProductCardComponent],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="kicker mono-label">Wishlist</div>
      <h1 style="margin-bottom:8px;">Saved for later</h1>

      @if (wishlist.loading() && wishlist.count() === 0) {
        <div class="rt-grid rt-cols-4">
          @for (n of [1, 2, 3, 4]; track n) {
            <div class="rt-skeleton" style="height:330px;"></div>
          }
        </div>
      } @else if (wishlist.count() === 0) {
        <div class="rt-panel center" style="padding:40px 20px;">
          <h3 class="mt-0">Your wishlist is empty</h3>
          <p>Tap the ♡ on any product to keep it here.</p>
          <a class="rt-btn rt-btn--ghost" routerLink="/shop">Browse the catalogue</a>
        </div>
      } @else {
        <p>{{ wishlist.count() }} saved product(s)</p>
        <div class="rt-grid rt-cols-4">
          @for (product of wishlist.items(); track product.id) {
            <rt-product-card [product]="product" />
          }
        </div>
      }
    </div>
  `
})
export class WishlistComponent {
  readonly wishlist = inject(WishlistService);
}
