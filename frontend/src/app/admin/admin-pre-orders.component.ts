import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeError, parseUtc } from '../core/api.service';
import { AdminPreOrder, Paged } from '../core/models';
import { ToastService } from '../core/toast.service';

type Status = AdminPreOrder['status'];

/** The API takes the enum number; the table shows the name. */
const STATUS_CODE: Record<Status, number> = { Pending: 1, Contacted: 2, Converted: 3, Cancelled: 4 };
const STATUSES = Object.keys(STATUS_CODE) as Status[];

/** Pre-order requests from the storefront. The showroom calls each customer back. */
@Component({
  selector: 'rt-admin-pre-orders',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Customers</div>
        <h1 class="mb-0">Pre-order requests</h1>
        <p class="small muted mb-0">No payment is taken online. Call the customer, then update the status.</p>
      </div>

      <div class="rt-row" style="gap:8px;">
        <select class="rt-select" style="min-width:150px;" aria-label="Filter by status" [(ngModel)]="status" (ngModelChange)="load(1)">
          <option value="">All</option>
          @for (s of statuses; track s) {
            <option [value]="s">{{ s }}</option>
          }
        </select>
        <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="load(page()?.page ?? 1)" aria-label="Refresh">↻</button>
      </div>
    </div>

    @if (loading()) {
      <div class="rt-stack mt-3">
        @for (n of [1, 2, 3]; track n) {
          <div class="rt-skeleton" style="height:64px;"></div>
        }
      </div>
    } @else if (items().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;">
        <h3 class="mt-0">No pre-order requests</h3>
        <p>Requests appear here when customers use the Pre-order page.</p>
      </div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead>
            <tr><th>Customer</th><th>Product</th><th>Received</th><th>Status</th></tr>
          </thead>
          <tbody>
            @for (row of items(); track row.id) {
              <tr>
                <td>
                  <strong>{{ row.name }}</strong>
                  <div class="small"><a [href]="'tel:' + row.phone">{{ row.phone }}</a></div>
                  @if (row.note) {
                    <div class="tiny muted">“{{ row.note }}”</div>
                  }
                </td>
                <td class="small">{{ row.productName || '(product removed)' }}</td>
                <td class="small">{{ received(row.createdAt) }}</td>
                <td>
                  <select
                    class="rt-select"
                    [attr.aria-label]="'Status for ' + row.name"
                    [ngModel]="row.status"
                    (ngModelChange)="change(row, $event)"
                  >
                    @for (s of statuses; track s) {
                      <option [value]="s">{{ s }}</option>
                    }
                  </select>
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
  `
})
export class AdminPreOrdersComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly statuses = STATUSES;
  readonly items = signal<AdminPreOrder[]>([]);
  readonly page = signal<Paged<AdminPreOrder> | null>(null);
  readonly loading = signal(true);

  status: Status | '' = '';

  ngOnInit(): void {
    this.load(1);
  }

  load(page: number): void {
    this.loading.set(true);

    this.api
      .get<Paged<AdminPreOrder>>('/admin/pre-orders', {
        status: this.status ? STATUS_CODE[this.status] : undefined,
        page,
        pageSize: 20
      })
      .subscribe({
        next: (result) => {
          this.items.set(result.items ?? []);
          this.page.set(result);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.toast.show(describeError(error), 'error');
        }
      });
  }

  change(row: AdminPreOrder, status: Status): void {
    if (status === row.status) {
      return;
    }

    this.api.post(`/admin/pre-orders/${row.id}/status`, { status: STATUS_CODE[status] }).subscribe({
      next: () => {
        this.toast.show(`${row.name}: ${status}.`);
        this.load(this.page()?.page ?? 1);
      },
      error: (error: unknown) => {
        this.toast.show(describeError(error), 'error');
        this.load(this.page()?.page ?? 1);
      }
    });
  }

  received(iso: string): string {
    return parseUtc(iso).toLocaleString();
  }
}
