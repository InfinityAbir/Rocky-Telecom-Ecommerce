import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeError } from '../core/api.service';
import { AdminReview, InventoryRow, Paged, StockLogEntry, StoreInfo } from '../core/models';
import { ToastService } from '../core/toast.service';
import { BdtPipe } from '../core/ui';
import { when } from './admin-utils';

// =============================================================================================
// Inventory
// =============================================================================================

interface RowEdit {
  stock: number;
  reason: string;
}

/** Stock per variant. Every change needs a reason and is written to the log below. */
@Component({
  selector: 'rt-admin-inventory',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Catalogue</div>
        <h1 class="mb-0">Inventory</h1>
        <p class="small muted mb-0">Set the counted stock for a variant and say why. Low stock means fewer than 5 units.</p>
      </div>

      <div class="rt-row" style="gap:10px; flex-wrap:wrap;">
        <input class="rt-input" style="min-width:220px;" placeholder="Search product or SKU" aria-label="Search inventory" [(ngModel)]="q" (change)="load(1)" />
        <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
          <input type="checkbox" [(ngModel)]="lowOnly" (ngModelChange)="load(1)" /> Low stock only
        </label>
      </div>
    </div>

    @if (loading()) {
      <div class="rt-skeleton mt-3" style="height:200px;"></div>
    } @else if (rows().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;"><h3 class="mt-0">Nothing matches</h3></div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead><tr><th>Product</th><th class="right">In stock</th><th>New count</th><th>Reason</th><th class="right"></th></tr></thead>
          <tbody>
            @for (r of rows(); track r.variantId) {
              <tr>
                <td>
                  <strong>{{ r.productName }}</strong>
                  <div class="tiny muted">{{ r.label || 'Single variant' }} · {{ r.sku }}</div>
                </td>
                <td class="right">
                  <strong [style.color]="r.stock === 0 ? 'var(--rust)' : r.stock < 5 ? '#b7791f' : 'inherit'">{{ r.stock }}</strong>
                  @if (r.stock === 0) { <div class="tiny" style="color:var(--rust);">Sold out</div> }
                  @else if (r.stock < 5) { <div class="tiny" style="color:#b7791f;">Low</div> }
                </td>
                <td>
                  <input type="number" min="0" class="rt-input" style="width:100px;" [attr.aria-label]="'New stock for ' + r.productName"
                    [ngModel]="edit(r).stock" (ngModelChange)="edit(r).stock = $event" />
                </td>
                <td>
                  <input class="rt-input" style="min-width:180px;" maxlength="200" placeholder="e.g. Recount, new delivery" [attr.aria-label]="'Reason for ' + r.productName"
                    [ngModel]="edit(r).reason" (ngModelChange)="edit(r).reason = $event" />
                </td>
                <td class="right">
                  <button class="rt-btn rt-btn--sm" type="button" [disabled]="!canSave(r)" (click)="save(r)">Save</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      @if (page() && page()!.totalPages > 1) {
        <div class="rt-row" style="justify-content:center; gap:14px; margin-top:16px;">
          <button class="rt-btn rt-btn--ghost rt-btn--sm" [disabled]="page()!.page <= 1" (click)="load(page()!.page - 1)">← Prev</button>
          <span class="mono-label">Page {{ page()!.page }} / {{ page()!.totalPages }}</span>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" [disabled]="page()!.page >= page()!.totalPages" (click)="load(page()!.page + 1)">Next →</button>
        </div>
      }
    }

    <h2 style="margin-top:32px; font-size:22px;">Recent stock changes</h2>
    @if (log().length === 0) {
      <p class="small muted">No manual changes recorded yet.</p>
    } @else {
      <div class="rt-panel" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead><tr><th>When</th><th>Product</th><th class="right">Change</th><th class="right">Now</th><th>Reason</th></tr></thead>
          <tbody>
            @for (e of log(); track e.id) {
              <tr>
                <td class="tiny">{{ when(e.at) }}</td>
                <td class="small">{{ e.productName }}<div class="tiny muted">{{ e.label }}</div></td>
                <td class="right"><strong [style.color]="e.delta < 0 ? 'var(--rust)' : 'inherit'">{{ e.delta > 0 ? '+' : '' }}{{ e.delta }}</strong></td>
                <td class="right">{{ e.stockAfter }}</td>
                <td class="small">{{ e.reason }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `
})
export class AdminInventoryComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly rows = signal<InventoryRow[]>([]);
  readonly page = signal<Paged<InventoryRow> | null>(null);
  readonly log = signal<StockLogEntry[]>([]);
  readonly loading = signal(true);
  readonly when = when;

  q = '';
  lowOnly = false;
  private readonly edits = new Map<number, RowEdit>();

  ngOnInit(): void {
    this.load(1);
    this.loadLog();
  }

  edit(r: InventoryRow): RowEdit {
    let e = this.edits.get(r.variantId);
    if (!e) {
      e = { stock: r.stock, reason: '' };
      this.edits.set(r.variantId, e);
    }

    return e;
  }

  canSave(r: InventoryRow): boolean {
    const e = this.edit(r);
    return e.stock !== null && Number(e.stock) >= 0 && Number(e.stock) !== r.stock && !!e.reason.trim();
  }

  load(page: number): void {
    this.loading.set(true);
    this.api
      .get<Paged<InventoryRow>>('/admin/inventory', { q: this.q || undefined, lowOnly: this.lowOnly || undefined, page, pageSize: 15 })
      .subscribe({
        next: (result) => {
          this.edits.clear();
          this.rows.set(result.items ?? []);
          this.page.set(result);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.toast.show(describeError(error), 'error');
        }
      });
  }

  private loadLog(): void {
    this.api.get<StockLogEntry[]>('/admin/inventory/log', { take: 15 }).subscribe({
      next: (entries) => this.log.set(entries ?? []),
      error: () => this.log.set([])
    });
  }

  save(r: InventoryRow): void {
    const e = this.edit(r);

    this.api.post(`/admin/inventory/${r.variantId}/adjust`, { newStock: Number(e.stock), reason: e.reason.trim() }).subscribe({
      next: () => {
        this.toast.show(`${r.productName}: stock set to ${e.stock}.`);
        this.load(this.page()?.page ?? 1);
        this.loadLog();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}

// =============================================================================================
// Review moderation
// =============================================================================================

/** Reviews from verified buyers wait here until approved; removing one deletes it. */
@Component({
  selector: 'rt-admin-reviews',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Customers</div>
        <h1 class="mb-0">Reviews</h1>
        <p class="small muted mb-0">Only customers whose order was delivered can write a review.</p>
      </div>
      <select class="rt-select" style="min-width:160px;" aria-label="Review status" [(ngModel)]="status" (ngModelChange)="load()">
        <option value="pending">Waiting for approval</option>
        <option value="approved">Published</option>
        <option value="all">All</option>
      </select>
    </div>

    @if (loading()) {
      <div class="rt-skeleton mt-3" style="height:120px;"></div>
    } @else if (items().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;"><h3 class="mt-0">Nothing here</h3><p class="mb-0">New reviews appear here for approval.</p></div>
    } @else {
      <div class="rt-stack mt-2">
        @for (r of items(); track r.id) {
          <article class="rt-panel">
            <div class="rt-row rt-row--between rt-row--wrap" style="gap:12px;">
              <div>
                <strong>{{ r.productName }}</strong>
                <div><span style="color:var(--mustard,#e6a817);">{{ '★'.repeat(r.rating) }}{{ '☆'.repeat(5 - r.rating) }}</span> <span class="tiny muted">{{ when(r.createdAt) }}</span></div>
              </div>
              <div class="rt-row" style="gap:6px;">
                @if (!r.isApproved) {
                  <button class="rt-btn rt-btn--sm" type="button" (click)="moderate(r, true)">Approve</button>
                }
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="moderate(r, false)">Remove</button>
              </div>
            </div>
            @if (r.body) {
              <p style="margin:10px 0 0; white-space:pre-wrap;">{{ r.body }}</p>
            }
          </article>
        }
      </div>
    }
  `
})
export class AdminReviewsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly items = signal<AdminReview[]>([]);
  readonly loading = signal(true);
  readonly when = when;

  status = 'pending';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<AdminReview[]>('/admin/reviews', { status: this.status }).subscribe({
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

  moderate(r: AdminReview, approve: boolean): void {
    if (!approve && typeof window !== 'undefined' && !window.confirm('Remove this review?')) {
      return;
    }

    this.api.post(`/admin/reviews/${r.id}/moderate`, { approve }).subscribe({
      next: () => {
        this.toast.show(approve ? 'Review published.' : 'Review removed.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}

// =============================================================================================
// Settings: showroom details and delivery zones
// =============================================================================================

interface ZoneRow {
  id: number;
  name: string;
  districts: string;
  charge: number;
  estimatedDays: number;
  sortOrder: number;
  isActive: boolean;
}

/** Showroom details (shown on the store page and footer) and flat delivery charges by zone. */
@Component({
  selector: 'rt-admin-settings',
  standalone: true,
  imports: [FormsModule, BdtPipe],
  template: `
    <div class="kicker mono-label">Settings</div>
    <h1 class="mb-0">Showroom &amp; delivery</h1>

    <form class="rt-panel mt-2" (ngSubmit)="saveStore()">
      <h3 class="mt-0">Showroom details</h3>

      <div class="rt-grid rt-cols-2" style="gap:12px;">
        <div class="rt-field">
          <label class="rt-label" for="s-name">Name</label>
          <input id="s-name" name="name" class="rt-input" [(ngModel)]="store.name" required maxlength="120" />
        </div>
        <div class="rt-field">
          <label class="rt-label" for="s-phone">Phone</label>
          <input id="s-phone" name="phone" class="rt-input" [(ngModel)]="store.phone" required maxlength="20" />
        </div>
        <div class="rt-field">
          <label class="rt-label" for="s-email">Email</label>
          <input id="s-email" name="email" type="email" class="rt-input" [(ngModel)]="store.email" maxlength="254" />
        </div>
        <div class="rt-field">
          <label class="rt-label" for="s-hours">Opening hours</label>
          <input id="s-hours" name="hours" class="rt-input" [(ngModel)]="store.openingHours" maxlength="200" />
        </div>
      </div>

      <div class="rt-field">
        <label class="rt-label" for="s-addr">Address</label>
        <input id="s-addr" name="address" class="rt-input" [(ngModel)]="store.address" required maxlength="300" />
      </div>

      <div class="rt-grid rt-cols-2" style="gap:12px;">
        <div class="rt-field">
          <label class="rt-label" for="s-lat">Map latitude</label>
          <input id="s-lat" name="lat" type="number" step="0.0001" class="rt-input" [(ngModel)]="store.mapLat" />
        </div>
        <div class="rt-field">
          <label class="rt-label" for="s-lng">Map longitude</label>
          <input id="s-lng" name="lng" type="number" step="0.0001" class="rt-input" [(ngModel)]="store.mapLng" />
        </div>
      </div>
      <p class="tiny muted">The map on the store page uses these coordinates. Open the showroom in a map app and copy its latitude and longitude here to place the pin exactly.</p>

      <button class="rt-btn rt-btn--sm" type="submit" [disabled]="savingStore() || !store.name || !store.address || !store.phone">
        {{ savingStore() ? 'Saving…' : 'Save showroom details' }}
      </button>
    </form>

    <h2 style="margin-top:32px; font-size:22px;">Delivery zones</h2>
    <p class="small muted">A flat charge per zone. A zone with <strong>*</strong> as its districts is the catch-all for everywhere else.</p>

    <div class="rt-stack">
      @for (z of zones(); track z.id) {
        <form class="rt-panel" (ngSubmit)="saveZone(z)">
          <div class="rt-grid rt-cols-3" style="gap:12px;">
            <div class="rt-field">
              <label class="rt-label" [for]="'z-n-' + z.id">Zone name</label>
              <input [id]="'z-n-' + z.id" [name]="'zn' + z.id" class="rt-input" [(ngModel)]="z.name" required maxlength="80" />
            </div>
            <div class="rt-field">
              <label class="rt-label" [for]="'z-c-' + z.id">Charge (৳)</label>
              <input [id]="'z-c-' + z.id" [name]="'zc' + z.id" type="number" min="0" class="rt-input" [(ngModel)]="z.charge" required />
            </div>
            <div class="rt-field">
              <label class="rt-label" [for]="'z-d-' + z.id">Delivery days</label>
              <input [id]="'z-d-' + z.id" [name]="'zd' + z.id" type="number" min="0" max="30" class="rt-input" [(ngModel)]="z.estimatedDays" required />
            </div>
          </div>

          <div class="rt-field">
            <label class="rt-label" [for]="'z-l-' + z.id">Districts (comma separated, or *)</label>
            <input [id]="'z-l-' + z.id" [name]="'zl' + z.id" class="rt-input" [(ngModel)]="z.districts" required maxlength="600" />
          </div>

          <div class="rt-row rt-row--between rt-row--wrap">
            <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
              <input type="checkbox" [name]="'za' + z.id" [(ngModel)]="z.isActive" /> Active · currently {{ z.charge | bdt }}
            </label>
            <div class="rt-row" style="gap:6px;">
              <button class="rt-btn rt-btn--sm" type="submit">{{ z.id ? 'Save zone' : 'Add zone' }}</button>
              @if (z.id) {
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="removeZone(z)">Delete</button>
              } @else {
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="cancelNew()">Cancel</button>
              }
            </div>
          </div>
        </form>
      }
    </div>

    <p style="margin-top:14px;"><button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="newZone()">+ Add a zone</button></p>
  `
})
export class AdminSettingsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly zones = signal<ZoneRow[]>([]);
  readonly savingStore = signal(false);

  store: StoreInfo = { name: '', address: '', phone: '', email: null, openingHours: null, mapLat: null, mapLng: null };

  ngOnInit(): void {
    this.api.get<StoreInfo>('/admin/settings/store').subscribe({
      next: (store) => (this.store = { ...store }),
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
    this.loadZones();
  }

  private loadZones(): void {
    this.api.get<ZoneRow[]>('/admin/settings/delivery-zones').subscribe({
      next: (zones) => this.zones.set(zones ?? []),
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }

  saveStore(): void {
    if (this.savingStore()) {
      return;
    }

    const num = (v: number | null) => (v === null || v === undefined || (v as unknown) === '' ? null : Number(v));
    this.savingStore.set(true);

    this.api
      .put('/admin/settings/store', {
        name: this.store.name.trim(),
        address: this.store.address.trim(),
        phone: this.store.phone.trim(),
        email: this.store.email?.trim() || null,
        openingHours: this.store.openingHours?.trim() || null,
        mapLat: num(this.store.mapLat),
        mapLng: num(this.store.mapLng)
      })
      .subscribe({
        next: () => {
          this.savingStore.set(false);
          this.toast.show('Showroom details saved.');
        },
        error: (error: unknown) => {
          this.savingStore.set(false);
          this.toast.show(describeError(error), 'error');
        }
      });
  }

  newZone(): void {
    if (this.zones().some((z) => z.id === 0)) {
      return;
    }

    const next = Math.max(0, ...this.zones().map((z) => z.sortOrder)) + 1;
    this.zones.set([...this.zones(), { id: 0, name: '', districts: '', charge: 100, estimatedDays: 2, sortOrder: next, isActive: true }]);
  }

  cancelNew(): void {
    this.zones.set(this.zones().filter((z) => z.id !== 0));
  }

  saveZone(z: ZoneRow): void {
    this.api
      .put('/admin/settings/delivery-zones', {
        id: z.id,
        name: z.name.trim(),
        districts: z.districts.trim(),
        charge: Number(z.charge),
        estimatedDays: Number(z.estimatedDays),
        sortOrder: z.sortOrder,
        isActive: z.isActive
      })
      .subscribe({
        next: () => {
          this.toast.show('Delivery zone saved.');
          this.loadZones();
        },
        error: (error: unknown) => this.toast.show(describeError(error), 'error')
      });
  }

  removeZone(z: ZoneRow): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete the “${z.name}” zone?`)) {
      return;
    }

    this.api.delete(`/admin/settings/delivery-zones/${z.id}`).subscribe({
      next: () => {
        this.toast.show('Delivery zone deleted.');
        this.loadZones();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}
