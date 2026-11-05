import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService, describeError } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { PreOrderReceipt, ProductCard, ProductListResult } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { ProductCardComponent } from '../../shared/product-card.component';

/** Same rule as the server (FR-12): a Bangladeshi mobile number, optional +88 / 88 prefix. */
const PHONE = /^(?:\+?88)?01[3-9]\d{8}$/;

/**
 * /pre-order — products flagged for pre-order plus a short request form. No payment is taken:
 * the showroom calls the customer back, so a request is a reservation of interest, not an order.
 */
@Component({
  selector: 'rt-pre-order',
  standalone: true,
  imports: [FormsModule, RouterLink, ProductCardComponent],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="kicker mono-label">Pre-order</div>
      <h1 style="margin-bottom:8px;">Reserve a new arrival</h1>
      <p>Tell us which handset you want. The showroom calls you to confirm the price and date — nothing is charged now.</p>

      @if (receipt(); as r) {
        <div class="rt-panel" role="status" style="margin:16px 0;">
          <h3 class="mt-0">Request received</h3>
          <p class="mb-0">
            We've noted your interest in <strong>{{ r.productName }}</strong> (reference #{{ r.id }}). The showroom will
            call you on {{ phoneShown }}.
          </p>
          <p style="margin-top:14px;">
            <button type="button" class="rt-btn rt-btn--ghost rt-btn--sm" (click)="receipt.set(null)">Reserve another</button>
            <a class="rt-btn rt-btn--ghost rt-btn--sm" routerLink="/shop" style="margin-left:8px;">Back to the shop</a>
          </p>
        </div>
      }

      @if (loading()) {
        <div class="rt-grid rt-cols-4">
          @for (n of [1, 2, 3, 4]; track n) {
            <div class="rt-skeleton" style="height:330px;"></div>
          }
        </div>
      } @else if (products().length === 0) {
        <div class="rt-panel center" style="padding:40px 20px;">
          <h3 class="mt-0">Nothing open for pre-order right now</h3>
          <p>When a new handset is announced it will appear here.</p>
          <a class="rt-btn rt-btn--ghost" routerLink="/shop">Browse the shop</a>
        </div>
      } @else {
        @if (!receipt()) {
          <form class="rt-panel preform" (ngSubmit)="submit()" #f="ngForm" novalidate>
            <div class="rt-field">
              <label class="rt-label" for="po-product">Product</label>
              <select id="po-product" name="product" class="rt-select" [(ngModel)]="productSlug" required>
                <option value="" disabled>Choose a product…</option>
                @for (product of products(); track product.id) {
                  <option [value]="product.slug">{{ product.name }}</option>
                }
              </select>
            </div>

            <div class="rt-grid rt-cols-2" style="gap:12px;">
              <div class="rt-field">
                <label class="rt-label" for="po-name">Your name</label>
                <input id="po-name" name="name" class="rt-input" [(ngModel)]="name" required maxlength="120" autocomplete="name" />
              </div>
              <div class="rt-field">
                <label class="rt-label" for="po-phone">Mobile number</label>
                <input
                  id="po-phone"
                  name="phone"
                  class="rt-input"
                  inputmode="tel"
                  placeholder="01XXXXXXXXX"
                  [(ngModel)]="phone"
                  required
                  autocomplete="tel"
                  [attr.aria-invalid]="phone && !validPhone() ? 'true' : null"
                />
                @if (phone && !validPhone()) {
                  <p class="rt-error mb-0">Enter a Bangladeshi mobile number, e.g. 01712345678.</p>
                }
              </div>
            </div>

            <div class="rt-field">
              <label class="rt-label" for="po-note">Note (optional)</label>
              <textarea id="po-note" name="note" class="rt-textarea" rows="2" maxlength="500" [(ngModel)]="note"
                placeholder="Colour, storage, best time to call…"></textarea>
            </div>

            <button class="rt-btn rt-btn--rust" type="submit" [disabled]="submitting() || !canSubmit()">
              {{ submitting() ? 'Sending…' : 'Send pre-order request' }}
            </button>
          </form>
        }

        <div class="rt-grid rt-cols-4" style="margin-top:24px;">
          @for (product of products(); track product.id) {
            <rt-product-card [product]="product" />
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      .preform { max-width: 640px; }
    `
  ]
})
export class PreOrderComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);

  readonly products = signal<ProductCard[]>([]);
  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly receipt = signal<PreOrderReceipt | null>(null);

  productSlug = '';
  name = '';
  phone = '';
  note = '';
  phoneShown = '';

  ngOnInit(): void {
    // Signed-in customers don't retype their details.
    const user = this.auth.user();
    this.name = user?.name ?? '';
    this.phone = user?.phone ?? '';

    this.api.get<ProductListResult>('/products', { preOrderOnly: true, pageSize: 24 }).subscribe({
      next: (result) => {
        const items = result.page.items ?? [];
        this.products.set(items);

        const wanted = this.route.snapshot.queryParamMap.get('product');
        this.productSlug = items.find((item) => item.slug === wanted)?.slug ?? (items.length === 1 ? items[0].slug : '');
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  validPhone(): boolean {
    return PHONE.test(this.phone.trim());
  }

  canSubmit(): boolean {
    return !!this.productSlug && !!this.name.trim() && this.validPhone();
  }

  submit(): void {
    if (this.submitting() || !this.canSubmit()) {
      return;
    }

    this.submitting.set(true);

    this.api
      .post<PreOrderReceipt>('/pre-orders', {
        productSlug: this.productSlug,
        name: this.name.trim(),
        phone: this.phone.trim(),
        note: this.note.trim() || null
      })
      .subscribe({
        next: (receipt) => {
          this.submitting.set(false);
          this.phoneShown = this.phone.trim();
          this.note = '';
          this.receipt.set(receipt);
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          this.toast.show(describeError(error), 'error');
        }
      });
  }
}
