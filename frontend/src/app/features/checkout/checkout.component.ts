import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, toApiError } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { CartService } from '../../core/cart.service';
import { OrderDetail, Quote } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { BdtPipe, CleanNamePipe } from '../../core/ui';

@Component({
  selector: 'rt-checkout',
  standalone: true,
  imports: [FormsModule, RouterLink, BdtPipe, CleanNamePipe],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="section-head">
        <div>
          <div class="kicker mono-label">Step 2 of 3</div>
          <h1 class="mb-0">Checkout</h1>
        </div>
        <a class="mono-label" routerLink="/cart">← Back to cart</a>
      </div>

      @if (cart.items().length === 0) {
        <div class="rt-panel center" style="padding:40px;">
          <h2 class="mt-0">Your cart is empty</h2>
          <a class="rt-btn rt-btn--rust" routerLink="/shop">Find something first</a>
        </div>
      } @else {
        <div class="checkout-layout">
          <form class="rt-stack" style="gap:16px;" (ngSubmit)="placeOrder()" #form="ngForm">
            <!-- CONTACT -->
            <section class="rt-panel rt-panel--flat">
              <h3 class="mt-0">1 · Who is it for?</h3>

              <div class="rt-grid rt-cols-2">
                <div class="rt-field">
                  <label class="rt-label" for="name">Full name</label>
                  <input id="name" name="name" class="rt-input" [(ngModel)]="model.name" required maxlength="120" />
                </div>
                <div class="rt-field">
                  <label class="rt-label" for="phone">Mobile number</label>
                  <input
                    id="phone"
                    name="phone"
                    class="rt-input"
                    [(ngModel)]="model.phone"
                    required
                    inputmode="tel"
                    placeholder="01XXXXXXXXX"
                    pattern="^(?:\\+?88)?01[3-9]\\d{8}$"
                  />
                </div>
              </div>

              @if (!auth.isSignedIn()) {
                <p class="tiny muted mb-0">
                  Checking out as a guest — your order is tied to this phone number.
                  <a routerLink="/login" [queryParams]="{ returnUrl: '/checkout' }">Sign in</a> to save it to an account.
                </p>
              }
            </section>

            <!-- DELIVERY -->
            <section class="rt-panel rt-panel--flat">
              <h3 class="mt-0">2 · How should it arrive?</h3>

              <div class="rt-row" style="gap:10px; margin-bottom:14px;">
                <button type="button" class="rt-chip" [class.is-active]="model.deliveryType === 1" (click)="setDelivery(1)">
                  Home delivery
                </button>
                <button type="button" class="rt-chip" [class.is-active]="model.deliveryType === 2" (click)="setDelivery(2)">
                  Showroom pickup (free)
                </button>
              </div>

              @if (model.deliveryType === 1) {
                <div class="rt-grid rt-cols-2">
                  <div class="rt-field">
                    <label class="rt-label" for="division">Division</label>
                    <input id="division" name="division" class="rt-input" [(ngModel)]="model.division" required />
                  </div>
                  <div class="rt-field">
                    <label class="rt-label" for="district">District</label>
                    <input id="district" name="district" class="rt-input" [(ngModel)]="model.district" required />
                  </div>
                  <div class="rt-field">
                    <label class="rt-label" for="thana">Thana</label>
                    <input id="thana" name="thana" class="rt-input" [(ngModel)]="model.thana" required />
                  </div>
                  <div class="rt-field">
                    <label class="rt-label" for="line">Address line</label>
                    <input id="line" name="line" class="rt-input" [(ngModel)]="model.line" required />
                  </div>
                </div>
              } @else {
                <p class="small mb-0">
                  Collect from <strong>Rocky Telecom, Sadullapur, Gaibandha</strong> — ready in about 2 hours
                  during opening times.
                </p>
              }

              <div class="rt-dashed"></div>

              <label class="rt-label" for="note">Order note (optional)</label>
              <textarea id="note" name="note" class="rt-textarea" rows="2" [(ngModel)]="model.note" maxlength="500"></textarea>
            </section>

            <!-- PAYMENT -->
            <section class="rt-panel rt-panel--flat">
              <h3 class="mt-0">3 · Payment</h3>

              <div class="pay-grid">
                <button type="button" class="pay-option" [class.is-active]="model.paymentMethod === 1" (click)="model.paymentMethod = 1">
                  <strong>Cash on delivery</strong>
                  <span class="tiny muted">Pay the rider. Available everywhere.</span>
                </button>
                <button type="button" class="pay-option" [class.is-active]="model.paymentMethod === 4" (click)="model.paymentMethod = 4">
                  <strong>Card / SSLCommerz</strong>
                  <span class="tiny muted">Sandbox gateway in this build.</span>
                </button>
                <button type="button" class="pay-option" [class.is-active]="model.paymentMethod === 2" (click)="model.paymentMethod = 2">
                  <strong>bKash</strong>
                  <span class="tiny muted">Sandbox gateway in this build.</span>
                </button>
                <button type="button" class="pay-option" [class.is-active]="model.paymentMethod === 3" (click)="model.paymentMethod = 3">
                  <strong>Nagad</strong>
                  <span class="tiny muted">Sandbox gateway in this build.</span>
                </button>
              </div>

              @if (model.paymentMethod !== 1) {
                <p class="tiny muted mt-2 mb-0">
                  You will be redirected to the gateway after placing the order. The order is only marked paid
                  when the gateway confirms it server-to-server.
                </p>
              }
            </section>

            @if (error()) {
              <p class="rt-error">{{ error() }}</p>
            }

            <button class="rt-btn rt-btn--rust rt-btn--block" type="submit" [disabled]="placing()">
              {{ placing() ? 'Placing order…' : 'Place order · ' + (quote()?.total | bdt) }}
            </button>
          </form>

          <!-- SUMMARY -->
          <aside class="rt-panel summary">
            <h3 class="mt-0">Basket</h3>

            @for (item of cart.items(); track item.id) {
              <div class="rt-row rt-row--between small" style="align-items:flex-start;">
                <span>{{ item.qty }} × {{ item.name | cleanName }}</span>
                <strong class="nowrap">{{ item.lineTotal | bdt }}</strong>
              </div>
            }

            <div class="rt-dashed"></div>

            <div class="rt-field">
              <label class="rt-label" for="coupon">Coupon code</label>
              <div class="rt-row" style="gap:6px;">
                <input id="coupon" name="coupon" class="rt-input" [(ngModel)]="couponCode" placeholder="SAVE10" />
                <button class="rt-btn rt-btn--sm rt-btn--ghost" type="button" (click)="refreshQuote()">Apply</button>
              </div>
              @if (quote()?.coupon; as coupon) {
                <p class="tiny" [style.color]="coupon.applied ? 'var(--ok)' : 'var(--warn)'">
                  {{ coupon.applied ? coupon.code + ' applied −' + (coupon.discount | bdt) : coupon.message }}
                </p>
              }
            </div>

            @if (quote(); as q) {
              <div class="rt-row rt-row--between small"><span>Subtotal</span><strong>{{ q.subTotal | bdt }}</strong></div>
              @if (q.coupon?.applied) {
                <div class="rt-row rt-row--between small" style="color:var(--ok);">
                  <span>Coupon</span><strong>−{{ q.coupon!.discount | bdt }}</strong>
                </div>
              }
              <div class="rt-row rt-row--between small">
                <span>Delivery{{ q.delivery.zone ? ' · ' + q.delivery.zone : '' }}</span>
                <strong>{{ q.delivery.fee | bdt }}</strong>
              </div>
              @if (q.delivery.estimatedDays) {
                <p class="tiny muted mb-0">Estimated {{ q.delivery.estimatedDays }} day(s)</p>
              }
              <div class="rt-dashed"></div>
              <div class="rt-row rt-row--between">
                <span class="mono-label">Total</span>
                <strong class="rt-price" style="font-size:22px;">{{ q.total | bdt }}</strong>
              </div>
            } @else {
              <p class="tiny muted">Calculating your total…</p>
            }

            <p class="tiny muted mt-2 mb-0">
              Prices and delivery are recomputed on the server. Nothing is charged from this page.
            </p>
          </aside>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .checkout-layout {
        display: grid;
        grid-template-columns: 1fr 330px;
        gap: 22px;
        align-items: start;
      }

      .summary { position: sticky; top: 92px; }

      .pay-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }

      .pay-option {
        display: flex;
        flex-direction: column;
        gap: 3px;
        text-align: left;
        padding: 12px 14px;
        border: 2px solid var(--ink);
        border-radius: 10px;
        background: var(--cream);
        cursor: pointer;
      }

      .pay-option.is-active { background: var(--mustard); box-shadow: var(--shadow-sm); }

      @media (max-width: 900px) {
        .checkout-layout { grid-template-columns: 1fr; }
        .summary { position: static; order: -1; }
      }

      @media (max-width: 560px) {
        .pay-grid { grid-template-columns: 1fr; }
      }
    `
  ]
})
export class CheckoutComponent {
  readonly cart = inject(CartService);
  readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly model = {
    name: this.auth.user()?.name ?? '',
    phone: this.auth.user()?.phone ?? '',
    deliveryType: 1,
    division: 'Rangpur',
    district: 'Gaibandha',
    thana: '',
    line: '',
    note: '',
    paymentMethod: 1
  };

  couponCode = '';
  readonly quote = signal<Quote | null>(null);
  readonly placing = signal(false);
  readonly error = signal('');

  constructor() {
    this.cart.load();
    this.refreshQuote();
    const user = this.auth.user();
    if (user) {
      this.model.name = user.name;
      this.model.phone = user.phone;
    }
  }

  setDelivery(type: number): void {
    this.model.deliveryType = type;
    this.refreshQuote();
  }

  refreshQuote(): void {
    this.api
      .post<Quote>('/checkout/quote', {
        items: [],
        deliveryType: Number(this.model.deliveryType),
        district: this.model.deliveryType === 1 ? this.model.district : null,
        couponCode: this.couponCode || null
      })
      .subscribe({
        next: (quote) => {
          this.quote.set(quote);
          this.error.set('');
        },
        error: (error) => {
          this.quote.set(null);
          this.error.set(toApiError(error).message);
        }
      });
  }

  placeOrder(): void {
    if (this.placing()) return;

    this.placing.set(true);
    this.error.set('');

    const idempotencyKey = this.idempotencyKey();

    this.api
      .post<{ order: OrderDetail; replay: boolean }>(
        '/orders',
        {
          deliveryType: Number(this.model.deliveryType),
          paymentMethod: Number(this.model.paymentMethod),
          couponCode: this.couponCode || null,
          note: this.model.note || null,
          guestPhone: this.auth.isSignedIn() ? null : this.model.phone,
          address:
            this.model.deliveryType === 1
              ? {
                  name: this.model.name,
                  phone: this.model.phone,
                  division: this.model.division,
                  district: this.model.district,
                  thana: this.model.thana,
                  line: this.model.line
                }
              : {
                  name: this.model.name,
                  phone: this.model.phone,
                  division: 'Rangpur',
                  district: 'Gaibandha',
                  thana: 'Sadullapur',
                  line: 'Showroom pickup'
                }
        },
        { 'Idempotency-Key': idempotencyKey }
      )
      .subscribe({
        next: (result) => {
          this.cart.load();
          this.placing.set(false);

          // Online payments start at the gateway; COD goes straight to the receipt.
          if (this.model.paymentMethod !== 1) {
            this.api
              .post<{ gatewayPageUrl: string }>(`/payments/${this.gatewayKey()}/init`, {
                orderId: result.order.id,
                guestPhone: this.auth.isSignedIn() ? null : this.model.phone
              })
              .subscribe({
                next: (init) => {
                  if (typeof window !== 'undefined') {
                    window.location.href = init.gatewayPageUrl;
                  }
                },
                error: () => this.toReceipt(result.order, this.model.phone)
              });
            return;
          }

          this.toReceipt(result.order, this.model.phone);
        },
        error: (error) => {
          this.placing.set(false);
          const apiError = toApiError(error);
          this.error.set(apiError.message);
          this.toast.show(apiError.message, 'error');
        }
      });
  }

  private toReceipt(order: OrderDetail, phone: string): void {
    // The order exists now; the next checkout must mint a fresh idempotency key.
    try {
      sessionStorage.removeItem('rt_idem');
    } catch {
      // ignore
    }

    this.router.navigate(['/order', order.orderNo], { queryParams: { phone } });
  }

  private gatewayKey(): string {
    return this.model.paymentMethod === 2 ? 'bkash' : this.model.paymentMethod === 3 ? 'nagad' : 'sslcommerz';
  }

  /** One key per checkout attempt: refreshing or double-clicking cannot create a second order (FR-11). */
  private idempotencyKey(): string {
    try {
      const existing = sessionStorage.getItem('rt_idem');
      if (existing) return existing;

      const key = typeof crypto !== 'undefined' && 'randomUUID' in crypto
        ? crypto.randomUUID()
        : `idem-${Date.now()}-${Math.random().toString(16).slice(2)}`;
      sessionStorage.setItem('rt_idem', key);
      return key;
    } catch {
      return `idem-${Date.now()}-${Math.random().toString(16).slice(2)}`;
    }
  }
}
