import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService, toApiError } from '../core/api.service';
import { AdminOrderRow, OrderDetail, Paged } from '../core/models';
import { ToastService } from '../core/toast.service';
import { BdtPipe, statusLabel, statusTone } from '../core/ui';

const FLOW: Record<string, { label: string; value: number } | null> = {
  Placed: { label: 'Confirm', value: 2 },
  Confirmed: { label: 'Pack', value: 3 },
  Packed: { label: 'Ship', value: 4 },
  Shipped: { label: 'Deliver', value: 5 },
  Delivered: null,
  Cancelled: null,
  Returned: null
};

@Component({
  selector: 'rt-admin-orders',
  standalone: true,
  imports: [FormsModule, RouterLink, BdtPipe, DatePipe],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Order desk</div>
        <h1 class="mb-0">Orders</h1>
      </div>

      <div class="rt-row" style="gap:8px;">
        <select class="rt-select" style="min-width:150px;" [(ngModel)]="status" (ngModelChange)="load(1)">
          <option value="">All statuses</option>
          @for (option of statuses; track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>
        <input class="rt-input" style="min-width:180px;" placeholder="Order no / phone" [(ngModel)]="q" (change)="load(1)" />
        <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="load(page()?.page ?? 1)">↻</button>
      </div>
    </div>

    @if (loading()) {
      <div class="rt-stack mt-3">
        @for (n of [1, 2, 3]; track n) {
          <div class="rt-skeleton" style="height:70px;"></div>
        }
      </div>
    } @else if (orders().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;">
        <h3 class="mt-0">No orders match</h3>
        <p class="mb-0">Clear the filter or wait for the next one.</p>
      </div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead>
            <tr>
              <th>Order</th><th>Customer</th><th>Placed</th><th>Status</th><th>Payment</th>
              <th class="right">Total</th><th class="right">Actions</th>
            </tr>
          </thead>
          <tbody>
            @for (order of orders(); track order.id) {
              <tr>
                <td>
                  <strong>{{ order.orderNo }}</strong>
                  <div class="tiny muted">{{ order.totalQty }} item(s) · {{ order.deliveryType }}</div>
                </td>
                <td>
                  {{ order.customer }}
                  <div class="tiny muted">{{ order.phone || '—' }}</div>
                </td>
                <td class="small">{{ order.createdAt | date: 'short' }}</td>
                <td>
                  <span
                    class="rt-tag"
                    [class.rt-tag--teal]="tone(order.status) === 'ok'"
                    [class.rt-tag--rust]="tone(order.status) === 'rust'"
                  >{{ text(order.status) }}</span>
                </td>
                <td class="small">{{ text(order.paymentStatus) }}<div class="tiny muted">{{ order.paymentMethod }}</div></td>
                <td class="right"><strong>{{ order.total | bdt }}</strong></td>
                <td class="right">
                  <div class="rt-row" style="justify-content:flex-end; gap:6px;">
                    @if (next(order.status); as step) {
                      <button class="rt-btn rt-btn--sm" type="button" [disabled]="busy() === order.id" (click)="advance(order, step.value)">
                        {{ step.label }}
                      </button>
                    }
                    @if (order.status === 'Placed' || order.status === 'Confirmed' || order.status === 'Packed') {
                      <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" [disabled]="busy() === order.id" (click)="advance(order, 6)">
                        Cancel
                      </button>
                    }
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      @if (page() && page()!.totalPages > 1) {
        <div class="rt-row" style="justify-content:center; gap:14px; margin-top:16px;">
          <button class="rt-btn rt-btn--ghost rt-btn--sm" [disabled]="page()!.page <= 1" (click)="load(page()!.page - 1)">← Prev</button>
          <span class="mono-label">Page {{ page()!.page }} / {{ page()!.totalPages }} · {{ page()!.total }} orders</span>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" [disabled]="page()!.page >= page()!.totalPages" (click)="load(page()!.page + 1)">Next →</button>
        </div>
      }
    }
  `
})
export class AdminOrdersComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);

  readonly statuses = ['Placed', 'Confirmed', 'Packed', 'Shipped', 'Delivered', 'Cancelled', 'Returned'];
  readonly orders = signal<AdminOrderRow[]>([]);
  readonly page = signal<Paged<AdminOrderRow> | null>(null);
  readonly loading = signal(true);
  readonly busy = signal<number | null>(null);

  status = '';
  q = '';

  ngOnInit(): void {
    this.q = this.route.snapshot.queryParamMap.get('q') ?? '';
    this.load(1);
  }

  load(page: number): void {
    this.loading.set(true);

    this.api
      .get<Paged<AdminOrderRow>>('/admin/orders', { status: this.status || undefined, q: this.q || undefined, page, pageSize: 15 })
      .subscribe({
        next: (result) => {
          this.orders.set(result.items ?? []);
          this.page.set(result);
          this.loading.set(false);
        },
        error: (error) => {
          this.loading.set(false);
          this.toast.show(toApiError(error).message, 'error');
        }
      });
  }

  advance(order: AdminOrderRow, status: number): void {
    if (this.busy()) return;

    this.busy.set(order.id);

    this.api.post<OrderDetail>(`/admin/orders/${order.id}/status`, { status }).subscribe({
      next: (detail) => {
        this.busy.set(null);
        this.toast.show(`Order ${detail.orderNo} is now ${detail.status}.`);
        this.load(this.page()?.page ?? 1);
      },
      error: (error) => {
        this.busy.set(null);
        this.toast.show(toApiError(error).message, 'error');
      }
    });
  }

  next(status: string): { label: string; value: number } | null {
    return FLOW[status] ?? null;
  }

  text(status: string): string {
    return statusLabel(status);
  }

  tone(status: string): string {
    return statusTone(status);
  }
}
