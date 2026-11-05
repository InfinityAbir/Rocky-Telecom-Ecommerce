import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiService, GUEST_TOKEN_KEY } from './api.service';
import { Cart } from './models';
import { ToastService } from './toast.service';

const EMPTY: Cart = { items: [], subTotal: 0, totalQty: 0, hasUnavailableItems: false };

/**
 * The cart always lives on the server (FR-02). A guest is identified by an opaque token in
 * localStorage; after sign-in the guest cart is merged once, then the token is dropped.
 */
@Injectable({ providedIn: 'root' })
export class CartService {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly cart = signal<Cart>(EMPTY);
  readonly count = computed(() => this.cart().totalQty);
  readonly subTotal = computed(() => this.cart().subTotal);
  readonly items = computed(() => this.cart().items);

  constructor() {
    this.ensureGuestToken();
  }

  load(): void {
    this.api.get<Cart>('/cart').subscribe({
      next: (cart) => this.cart.set(cart ?? EMPTY),
      error: () => this.cart.set(EMPTY)
    });
  }

  add(variantId: number, qty = 1): Observable<Cart> {
    return this.api.post<Cart>('/cart/items', { variantId, qty }).pipe(
      tap((cart) => {
        this.cart.set(cart ?? EMPTY);
        this.toast.show('Added to your cart.');
      })
    );
  }

  update(itemId: number, qty: number): Observable<Cart> {
    return this.api.put<Cart>(`/cart/items/${itemId}`, { qty }).pipe(tap((cart) => this.cart.set(cart ?? EMPTY)));
  }

  remove(itemId: number): Observable<Cart> {
    return this.api.delete<Cart>(`/cart/items/${itemId}`).pipe(tap((cart) => this.cart.set(cart ?? EMPTY)));
  }

  clear(): void {
    this.api.delete<Cart>('/cart').subscribe({
      next: (cart) => this.cart.set(cart ?? EMPTY),
      error: () => undefined
    });
  }

  /** Folds the guest cart into the signed-in cart (Phase 2.3) and forgets the guest token. */
  mergeAfterLogin(): void {
    const token = this.guestToken();

    if (!token) {
      this.load();
      return;
    }

    this.api.post<Cart>('/cart/merge').subscribe({
      next: (cart) => {
        this.cart.set(cart ?? EMPTY);
        this.dropGuestToken();
        this.toast.show('Your guest cart was merged into your account.');
      },
      error: () => this.load()
    });
  }

  guestToken(): string | null {
    try {
      return typeof localStorage === 'undefined' ? null : localStorage.getItem(GUEST_TOKEN_KEY);
    } catch {
      return null;
    }
  }

  private ensureGuestToken(): void {
    try {
      if (typeof localStorage === 'undefined' || localStorage.getItem(GUEST_TOKEN_KEY)) {
        return;
      }

      const token =
        typeof crypto !== 'undefined' && 'randomUUID' in crypto
          ? crypto.randomUUID()
          : `g-${Date.now()}-${Math.random().toString(16).slice(2)}`;

      localStorage.setItem(GUEST_TOKEN_KEY, token);
    } catch {
      // Storage disabled: the caller simply acts as an empty guest cart.
    }
  }

  private dropGuestToken(): void {
    try {
      localStorage.removeItem(GUEST_TOKEN_KEY);
    } catch {
      // ignore
    }
  }
}
