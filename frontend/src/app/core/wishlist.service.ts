import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService, toApiError } from './api.service';
import { AuthService } from './auth.service';
import { ProductCard } from './models';
import { ToastService } from './toast.service';

/**
 * Server-backed wishlist for signed-in customers (`/wishlist`). Guests are sent to sign-in
 * with a return URL instead of keeping a second, local-only list that could drift.
 */
@Injectable({ providedIn: 'root' })
export class WishlistService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly items = signal<ProductCard[]>([]);
  readonly loading = signal(false);
  readonly count = computed(() => this.items().length);
  private readonly ids = computed(() => new Set(this.items().map((item) => item.id)));

  constructor() {
    // Reload on sign-in, clear on sign-out.
    effect(
      () => {
        if (this.auth.isSignedIn()) {
          this.load();
        } else {
          this.items.set([]);
        }
      },
      { allowSignalWrites: true }
    );
  }

  has(productId: number): boolean {
    return this.ids().has(productId);
  }

  load(): void {
    this.loading.set(true);

    this.api.get<ProductCard[]>('/wishlist').subscribe({
      next: (items) => {
        this.items.set(items ?? []);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  toggle(product: Pick<ProductCard, 'id' | 'name'>): void {
    if (!this.auth.isSignedIn()) {
      this.toast.show('Sign in to save products to your wishlist.');
      this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
      return;
    }

    this.api.post<boolean>('/wishlist/toggle', { productId: product.id }).subscribe({
      next: () => {
        this.toast.show(this.has(product.id) ? 'Removed from your wishlist.' : 'Saved to your wishlist.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(toApiError(error).message, 'error')
    });
  }
}
