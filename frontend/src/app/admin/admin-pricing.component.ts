import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, takeUntil } from 'rxjs';
import { ApiService, describeError } from '../core/api.service';
import { AdminCoupon, AdminEmiPlan, AdminFlashSale, VariantOption } from '../core/models';
import { ToastService } from '../core/toast.service';
import { BdtPipe } from '../core/ui';
import { toIso, toLocalInput, when } from './admin-utils';

// =============================================================================================
// Coupons
// =============================================================================================

interface CouponForm {
  id: number;
  code: string;
  type: number;
  value: number | null;
  minCart: number | null;
  maxDiscount: number | null;
  usageLimit: number | null;
  perUserLimit: number | null;
  startAt: string;
  endAt: string;
  isActive: boolean;
}

/** Discount codes customers enter at checkout (percentage or fixed amount). Admin only: it changes prices. */
@Component({
  selector: 'rt-admin-coupons',
  standalone: true,
  imports: [FormsModule, BdtPipe],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Offers</div>
        <h1 class="mb-0">Coupons</h1>
        <p class="small muted mb-0">Codes are checked on the server at checkout. A coupon that has been used can be deactivated but not deleted.</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New coupon</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit coupon' : 'New coupon' }}</h3>

        <div class="rt-grid rt-cols-3" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="c-code">Code</label>
            <input id="c-code" name="code" class="rt-input" [(ngModel)]="f.code" required maxlength="30" style="text-transform:uppercase;" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-type">Type</label>
            <select id="c-type" name="type" class="rt-select" [(ngModel)]="f.type">
              <option [ngValue]="1">Percent off</option>
              <option [ngValue]="2">Fixed amount off (৳)</option>
            </select>
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-val">{{ f.type === 1 ? 'Percent (1–100)' : 'Amount (৳)' }}</label>
            <input id="c-val" name="value" type="number" min="0" class="rt-input" [(ngModel)]="f.value" required />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-min">Minimum cart (৳, optional)</label>
            <input id="c-min" name="minCart" type="number" min="0" class="rt-input" [(ngModel)]="f.minCart" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-max">Max discount (৳, optional)</label>
            <input id="c-max" name="maxDiscount" type="number" min="0" class="rt-input" [(ngModel)]="f.maxDiscount" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-use">Total uses (optional)</label>
            <input id="c-use" name="usageLimit" type="number" min="1" class="rt-input" [(ngModel)]="f.usageLimit" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-per">Uses per customer (optional)</label>
            <input id="c-per" name="perUserLimit" type="number" min="1" class="rt-input" [(ngModel)]="f.perUserLimit" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-start">Starts</label>
            <input id="c-start" name="startAt" type="datetime-local" class="rt-input" [(ngModel)]="f.startAt" required />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-end">Ends</label>
            <input id="c-end" name="endAt" type="datetime-local" class="rt-input" [(ngModel)]="f.endAt" required />
          </div>
        </div>

        <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
          <input type="checkbox" name="active" [(ngModel)]="f.isActive" /> Active
        </label>

        <div class="rt-row" style="gap:8px; margin-top:8px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !f.code.trim() || !f.value || !f.startAt || !f.endAt">
            {{ saving() ? 'Saving…' : 'Save coupon' }}
          </button>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="form.set(null)">Cancel</button>
        </div>
      </form>
    }

    @if (loading()) {
      <div class="rt-skeleton mt-3" style="height:120px;"></div>
    } @else if (items().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;"><h3 class="mt-0">No coupons yet</h3></div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead><tr><th>Code</th><th>Discount</th><th>Rules</th><th>Valid</th><th>Used</th><th>State</th><th class="right">Actions</th></tr></thead>
          <tbody>
            @for (c of items(); track c.id) {
              <tr>
                <td><strong>{{ c.code }}</strong></td>
                <td>{{ c.type === 1 ? c.value + '%' : (c.value | bdt) }}</td>
                <td class="tiny">
                  @if (c.minCart) { Min {{ c.minCart | bdt }}<br /> }
                  @if (c.maxDiscount) { Max {{ c.maxDiscount | bdt }}<br /> }
                  @if (c.perUserLimit) { {{ c.perUserLimit }} per customer }
                </td>
                <td class="tiny">{{ when(c.startAt) }}<br />→ {{ when(c.endAt) }}</td>
                <td class="small">{{ c.usedCount }}{{ c.usageLimit ? ' / ' + c.usageLimit : '' }}</td>
                <td><span class="rt-tag" [class.rt-tag--teal]="c.isActive" [class.rt-tag--paper]="!c.isActive">{{ c.isActive ? 'Active' : 'Off' }}</span></td>
                <td class="right">
                  <div class="rt-row" style="justify-content:flex-end; gap:6px;">
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(c)">Edit</button>
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(c)">Delete</button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `
})
export class AdminCouponsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly items = signal<AdminCoupon[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<CouponForm | null>(null);
  readonly when = when;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<AdminCoupon[]>('/admin/coupons').subscribe({
      next: (items) => {
        this.items.set(items ?? []);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  startNew(): void {
    const start = new Date();
    const end = new Date(Date.now() + 30 * 86400000);
    this.form.set({
      id: 0, code: '', type: 1, value: null, minCart: null, maxDiscount: null, usageLimit: null, perUserLimit: null,
      startAt: toLocalInput(start.toISOString()), endAt: toLocalInput(end.toISOString()), isActive: true
    });
  }

  edit(c: AdminCoupon): void {
    this.form.set({
      id: c.id, code: c.code, type: c.type, value: c.value, minCart: c.minCart, maxDiscount: c.maxDiscount,
      usageLimit: c.usageLimit, perUserLimit: c.perUserLimit,
      startAt: toLocalInput(c.startAt), endAt: toLocalInput(c.endAt), isActive: c.isActive
    });
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving()) {
      return;
    }

    const num = (v: number | null) => (v === null || v === undefined || (v as unknown) === '' ? null : Number(v));
    const body = {
      code: f.code.trim(),
      type: f.type,
      value: Number(f.value),
      minCart: num(f.minCart),
      maxDiscount: num(f.maxDiscount),
      usageLimit: num(f.usageLimit),
      perUserLimit: num(f.perUserLimit),
      startAt: toIso(f.startAt),
      endAt: toIso(f.endAt),
      isActive: f.isActive
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/coupons/${f.id}`, body) : this.api.post('/admin/coupons', body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.form.set(null);
        this.toast.show('Coupon saved.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(c: AdminCoupon): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete coupon ${c.code}?`)) {
      return;
    }

    this.api.delete(`/admin/coupons/${c.id}`).subscribe({
      next: () => {
        this.toast.show('Coupon deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}

// =============================================================================================
// EMI plans
// =============================================================================================

interface EmiForm {
  id: number;
  bankName: string;
  months: number | null;
  interestPct: number | null;
  minPrice: number | null;
  isActive: boolean;
}

/** Bank instalment plans behind the EMI calculator on product pages (shown only above the minimum price). */
@Component({
  selector: 'rt-admin-emi',
  standalone: true,
  imports: [FormsModule, BdtPipe],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Offers</div>
        <h1 class="mb-0">EMI plans</h1>
        <p class="small muted mb-0">The calculator only estimates; the final plan is set by the bank.</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New plan</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit plan' : 'New plan' }}</h3>
        <div class="rt-grid rt-cols-2" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="e-bank">Bank</label>
            <input id="e-bank" name="bank" class="rt-input" [(ngModel)]="f.bankName" required maxlength="80" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="e-months">Months (1–60)</label>
            <input id="e-months" name="months" type="number" min="1" max="60" class="rt-input" [(ngModel)]="f.months" required />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="e-int">Interest % (0 for none)</label>
            <input id="e-int" name="interest" type="number" min="0" max="100" step="0.1" class="rt-input" [(ngModel)]="f.interestPct" required />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="e-min">Minimum price (৳)</label>
            <input id="e-min" name="minPrice" type="number" min="0" class="rt-input" [(ngModel)]="f.minPrice" required />
          </div>
        </div>
        <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
          <input type="checkbox" name="active" [(ngModel)]="f.isActive" /> Active
        </label>
        <div class="rt-row" style="gap:8px; margin-top:8px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !f.bankName.trim() || !f.months">{{ saving() ? 'Saving…' : 'Save plan' }}</button>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="form.set(null)">Cancel</button>
        </div>
      </form>
    }

    @if (loading()) {
      <div class="rt-skeleton mt-3" style="height:120px;"></div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead><tr><th>Bank</th><th class="right">Months</th><th class="right">Interest</th><th class="right">Min price</th><th>State</th><th class="right">Actions</th></tr></thead>
          <tbody>
            @for (p of items(); track p.id) {
              <tr>
                <td><strong>{{ p.bankName }}</strong></td>
                <td class="right">{{ p.months }}</td>
                <td class="right">{{ p.interestPct }}%</td>
                <td class="right">{{ p.minPrice | bdt }}</td>
                <td><span class="rt-tag" [class.rt-tag--teal]="p.isActive" [class.rt-tag--paper]="!p.isActive">{{ p.isActive ? 'Active' : 'Off' }}</span></td>
                <td class="right">
                  <div class="rt-row" style="justify-content:flex-end; gap:6px;">
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(p)">Edit</button>
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(p)">Delete</button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `
})
export class AdminEmiComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly items = signal<AdminEmiPlan[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<EmiForm | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<AdminEmiPlan[]>('/admin/emi-plans').subscribe({
      next: (items) => {
        this.items.set(items ?? []);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  startNew(): void {
    this.form.set({ id: 0, bankName: '', months: 6, interestPct: 0, minPrice: 10000, isActive: true });
  }

  edit(p: AdminEmiPlan): void {
    this.form.set({ ...p });
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving()) {
      return;
    }

    const body = {
      bankName: f.bankName.trim(),
      months: Number(f.months),
      interestPct: Number(f.interestPct ?? 0),
      minPrice: Number(f.minPrice ?? 0),
      isActive: f.isActive
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/emi-plans/${f.id}`, body) : this.api.post('/admin/emi-plans', body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.form.set(null);
        this.toast.show('EMI plan saved.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(p: AdminEmiPlan): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete the ${p.bankName} ${p.months}-month plan?`)) {
      return;
    }

    this.api.delete(`/admin/emi-plans/${p.id}`).subscribe({
      next: () => {
        this.toast.show('EMI plan deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}

// =============================================================================================
// Flash sales
// =============================================================================================

interface FlashItemForm {
  variantId: number;
  label: string;
  regularPrice: number;
  salePrice: number | null;
}

interface FlashForm {
  id: number;
  title: string;
  startAt: string;
  endAt: string;
  isActive: boolean;
  items: FlashItemForm[];
}

/** Time-boxed discounted variants with the home-page countdown. The sale ends by itself at its end time. */
@Component({
  selector: 'rt-admin-flash-sales',
  standalone: true,
  imports: [FormsModule, BdtPipe],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Offers</div>
        <h1 class="mb-0">Flash sales</h1>
        <p class="small muted mb-0">The earliest-ending live sale is shown on the home page with a countdown.</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New flash sale</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit flash sale' : 'New flash sale' }}</h3>

        <div class="rt-grid rt-cols-3" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="f-title">Title</label>
            <input id="f-title" name="title" class="rt-input" [(ngModel)]="f.title" required maxlength="120" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="f-start">Starts</label>
            <input id="f-start" name="startAt" type="datetime-local" class="rt-input" [(ngModel)]="f.startAt" required />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="f-end">Ends</label>
            <input id="f-end" name="endAt" type="datetime-local" class="rt-input" [(ngModel)]="f.endAt" required />
          </div>
        </div>

        <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
          <input type="checkbox" name="active" [(ngModel)]="f.isActive" /> Active
        </label>

        <h4 class="mb-1">Items on sale ({{ f.items.length }})</h4>
        @if (f.items.length === 0) {
          <p class="small muted">Search below and add products.</p>
        }

        @for (item of f.items; track item.variantId) {
          <div class="item">
            <span class="name">{{ item.label }}<span class="tiny muted"> · regular {{ item.regularPrice | bdt }}</span></span>
            <label class="tiny" [for]="'sp-' + item.variantId">Sale price</label>
            <input
              [id]="'sp-' + item.variantId"
              [name]="'sp-' + item.variantId"
              type="number"
              min="1"
              class="rt-input price"
              [(ngModel)]="item.salePrice"
            />
            @if (item.salePrice !== null && item.salePrice >= item.regularPrice) {
              <span class="rt-error">Must be below {{ item.regularPrice | bdt }}</span>
            }
            <button type="button" class="rt-btn rt-btn--ghost rt-btn--sm" (click)="removeItem(item.variantId)">Remove</button>
          </div>
        }

        <div class="rt-field mt-2">
          <label class="rt-label" for="f-search">Add a product</label>
          <input id="f-search" name="search" class="rt-input" placeholder="Search by product name or SKU…" [ngModel]="search" (ngModelChange)="onSearch($event)" autocomplete="off" />
        </div>

        @if (results().length > 0) {
          <ul class="results">
            @for (v of results(); track v.variantId) {
              <li>
                <span>{{ v.label }} <span class="tiny muted">· {{ v.effectivePrice | bdt }} · stock {{ v.stock }}</span></span>
                <button type="button" class="rt-btn rt-btn--sm" [disabled]="has(v.variantId)" (click)="addItem(v)">{{ has(v.variantId) ? 'Added' : 'Add' }}</button>
              </li>
            }
          </ul>
        }

        <div class="rt-row" style="gap:8px; margin-top:16px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !canSave(f)">{{ saving() ? 'Saving…' : 'Save flash sale' }}</button>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="close()">Cancel</button>
        </div>
      </form>
    }

    @if (loading()) {
      <div class="rt-skeleton mt-3" style="height:120px;"></div>
    } @else if (sales().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;"><h3 class="mt-0">No flash sales yet</h3></div>
    } @else {
      <div class="rt-stack mt-2">
        @for (s of sales(); track s.id) {
          <div class="rt-panel">
            <div class="rt-row rt-row--between rt-row--wrap">
              <div>
                <strong>{{ s.title }}</strong>
                <span class="rt-tag" style="margin-left:8px;" [class.rt-tag--teal]="isLive(s)" [class.rt-tag--paper]="!isLive(s)">{{ state(s) }}</span>
                <div class="tiny muted">{{ when(s.startAt) }} → {{ when(s.endAt) }} · {{ s.items.length }} item(s)</div>
              </div>
              <div class="rt-row" style="gap:6px;">
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(s)">Edit</button>
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(s)">Delete</button>
              </div>
            </div>
          </div>
        }
      </div>
    }
  `,
  styles: [
    `
      .item { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; padding: 8px 12px; border: 1px dashed rgba(0, 0, 0, 0.25); border-radius: 10px; margin-bottom: 6px; }
      .item .name { flex: 1; min-width: 200px; }
      .item .price { width: 120px; }
      .results { list-style: none; margin: 0 0 8px; padding: 0; display: grid; gap: 6px; }
      .results li { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 8px 12px; border: 1px dashed rgba(0, 0, 0, 0.25); border-radius: 10px; }
    `
  ]
})
export class AdminFlashSalesComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly destroy$ = new Subject<void>();
  private readonly search$ = new Subject<string>();

  readonly sales = signal<AdminFlashSale[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<FlashForm | null>(null);
  readonly results = signal<VariantOption[]>([]);
  readonly when = when;

  search = '';

  ngOnInit(): void {
    this.load();
    this.search$.pipe(debounceTime(250), takeUntil(this.destroy$)).subscribe((term) => this.runSearch(term));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  isLive(s: AdminFlashSale): boolean {
    const now = Date.now();
    return s.isActive && new Date(this.asUtc(s.startAt)).getTime() <= now && new Date(this.asUtc(s.endAt)).getTime() >= now;
  }

  state(s: AdminFlashSale): string {
    if (!s.isActive) return 'Off';
    const now = Date.now();
    if (new Date(this.asUtc(s.endAt)).getTime() < now) return 'Ended';
    return this.isLive(s) ? 'Live' : 'Scheduled';
  }

  private asUtc(iso: string): string {
    return /(Z|[+-]\d{2}:?\d{2})$/i.test(iso) ? iso : `${iso}Z`;
  }

  load(): void {
    this.loading.set(true);
    this.api.get<AdminFlashSale[]>('/admin/flash-sales').subscribe({
      next: (sales) => {
        this.sales.set(sales ?? []);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  startNew(): void {
    const start = new Date();
    const end = new Date(Date.now() + 3 * 86400000);
    this.open({ id: 0, title: '', startAt: toLocalInput(start.toISOString()), endAt: toLocalInput(end.toISOString()), isActive: true, items: [] });
  }

  edit(s: AdminFlashSale): void {
    this.open({
      id: s.id,
      title: s.title,
      startAt: toLocalInput(s.startAt),
      endAt: toLocalInput(s.endAt),
      isActive: s.isActive,
      items: s.items.map((i) => ({ variantId: i.variantId, label: i.label, regularPrice: i.regularPrice, salePrice: i.salePrice }))
    });
  }

  close(): void {
    this.form.set(null);
    this.results.set([]);
    this.search = '';
  }

  onSearch(term: string): void {
    this.search = term;
    this.search$.next(term);
  }

  has(variantId: number): boolean {
    return !!this.form()?.items.some((i) => i.variantId === variantId);
  }

  addItem(v: VariantOption): void {
    const f = this.form();
    if (f && !this.has(v.variantId) && f.items.length < 50) {
      f.items = [...f.items, { variantId: v.variantId, label: v.label, regularPrice: v.regularPrice, salePrice: Math.round(v.effectivePrice * 0.9) }];
    }
  }

  removeItem(variantId: number): void {
    const f = this.form();
    if (f) {
      f.items = f.items.filter((i) => i.variantId !== variantId);
    }
  }

  canSave(f: FlashForm): boolean {
    return !!f.title.trim() && !!f.startAt && !!f.endAt && f.items.length > 0
      && f.items.every((i) => i.salePrice !== null && i.salePrice > 0 && i.salePrice < i.regularPrice);
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving() || !this.canSave(f)) {
      return;
    }

    const body = {
      title: f.title.trim(),
      startAt: toIso(f.startAt),
      endAt: toIso(f.endAt),
      isActive: f.isActive,
      items: f.items.map((i) => ({ variantId: i.variantId, salePrice: Number(i.salePrice) }))
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/flash-sales/${f.id}`, body) : this.api.post('/admin/flash-sales', body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.close();
        this.toast.show('Flash sale saved.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(s: AdminFlashSale): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete “${s.title}”?`)) {
      return;
    }

    this.api.delete(`/admin/flash-sales/${s.id}`).subscribe({
      next: () => {
        this.toast.show('Flash sale deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }

  private open(form: FlashForm): void {
    this.form.set(form);
    this.search = '';
    this.runSearch('');
  }

  private runSearch(term: string): void {
    this.api.get<VariantOption[]>('/admin/flash-sales/variant-search', { q: term.trim() || undefined }).subscribe({
      next: (results) => this.results.set(results ?? []),
      error: () => this.results.set([])
    });
  }
}
