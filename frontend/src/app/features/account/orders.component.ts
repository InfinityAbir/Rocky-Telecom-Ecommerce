import { AccountNavComponent } from '../../shared/account-nav.component';
import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { OrderDetail, OrderSummary, Paged } from '../../core/models';
import { BdtPipe, CleanNamePipe, statusLabel, statusTone } from '../../core/ui';

@Component({
  selector: 'rt-account-orders',
  standalone: true,
  imports: [RouterLink, BdtPipe, CleanNamePipe, DatePipe, AccountNavComponent],
  template: `
    <div class="rt-container" style="padding-top:24px; max-width:1040px;">
      <div class="rt-row rt-row--between rt-row--wrap">
        <div>
          <div class="kicker mono-label">Your account</div>
          <h1 class="mb-0">Order history</h1>
        </div>
        <a class="mono-label" routerLink="/track">Track a guest order →</a>
      </div>
      <rt-account-nav />

      @if (!auth.isSignedIn()) {
        <div class="rt-panel center mt-3" style="padding:36px;">
          <h3 class="mt-0">Sign in to see your orders</h3>
          <a class="rt-btn rt-btn--rust" routerLink="/login" [queryParams]="{ returnUrl: '/account/orders' }">Sign in</a>
        </div>
      } @else if (loading()) {
        <div class="rt-stack mt-3">
          @for (n of [1, 2, 3]; track n) {
            <div class="rt-skeleton" style="height:82px;"></div>
          }
        </div>
      } @else if (orders().length === 0) {
        <div class="rt-panel center mt-3" style="padding:40px;">
          <h3 class="mt-0">No orders yet</h3>
          <p>The first one is a good excuse to visit the shop.</p>
          <a class="rt-btn rt-btn--rust" routerLink="/shop">Browse products</a>
        </div>
      } @else {
        <div class="rt-stack mt-3" style="gap:12px;">
          @for (order of orders(); track order.id) {
            <article class="order-card">
              <button class="order-head" type="button" (click)="toggle(order.id)">
                <div>
                  <span class="mono-label">{{ order.orderNo }}</span>
                  <div class="small muted">{{ order.createdAt | date: 'medium' }} · {{ order.totalQty }} item(s)</div>
                </div>
                <div class="rt-row" style="gap:14px;">
                  <span class="rt-tag" [class.rt-tag--teal]="tone(order.status) === 'ok'" [class.rt-tag--rust]="tone(order.status) === 'rust'">
                    {{ text(order.status) }}
                  </span>
                  <strong class="rt-price">{{ order.total | bdt }}</strong>
                  <span aria-hidden="true">{{ expanded() === order.id ? '▴' : '▾' }}</span>
                </div>
              </button>

              @if (expanded() === order.id && detail(); as order) {
                <div class="order-body">
                  <div class="rt-grid rt-cols-2">
                    <div>
                      <span class="mono-label">Items</span>
                      <table class="rt-table">
                        <tbody>
                          @for (item of order.items; track item.id) {
                            <tr>
                              <td>{{ item.qty }} × {{ item.name | cleanName }}</td>
                              <td class="right">{{ item.lineTotal | bdt }}</td>
                            </tr>
                          }
                        </tbody>
                      </table>
                      <div class="rt-dashed"></div>
                      <div class="rt-row rt-row--between small"><span>Subtotal</span><span>{{ order.subTotal | bdt }}</span></div>
                      @if (order.discount > 0) {
                        <div class="rt-row rt-row--between small" style="color:var(--ok);">
                          <span>Discount</span><span>−{{ order.discount | bdt }}</span>
                        </div>
                      }
                      <div class="rt-row rt-row--between small"><span>Delivery</span><span>{{ order.deliveryFee | bdt }}</span></div>
                      <div class="rt-row rt-row--between"><strong>Total</strong><strong>{{ order.total | bdt }}</strong></div>
                    </div>

                    <div>
                      <span class="mono-label">Progress</span>
                      <ol class="timeline">
                        @for (step of order.timeline; track $index) {
                          <li>
                            <span class="dot" aria-hidden="true"></span>
                            <div>
                              <strong>{{ text(step.status) }}</strong>
                              <div class="tiny muted">{{ step.at | date: 'short' }}</div>
                            </div>
                          </li>
                        }
                      </ol>
                    </div>
                  </div>
                </div>
              }
            </article>
          }
        </div>

        @if (page() && page()!.totalPages > 1) {
          <div class="rt-row" style="justify-content:center; gap:14px; margin-top:20px;">
            <button class="rt-btn rt-btn--ghost rt-btn--sm" [disabled]="page()!.page <= 1" (click)="goTo(page()!.page - 1)">
              ← Prev
            </button>
            <span class="mono-label">Page {{ page()!.page }} / {{ page()!.totalPages }}</span>
            <button
              class="rt-btn rt-btn--ghost rt-btn--sm"
              [disabled]="page()!.page >= page()!.totalPages"
              (click)="goTo(page()!.page + 1)"
            >
              Next →
            </button>
          </div>
        }
      }
    </div>
  `,
  styles: [
    `
      .order-card {
        background: var(--cream);
        border: 2px solid var(--ink);
        border-radius: 14px;
        box-shadow: var(--shadow-sm);
        overflow: hidden;
      }

      .order-head {
        width: 100%;
        display: flex;
        justify-content: space-between;
        align-items: center;
        gap: 14px;
        flex-wrap: wrap;
        padding: 14px 16px;
        background: transparent;
        border: 0;
        cursor: pointer;
        text-align: left;
      }

      .order-body { padding: 0 16px 18px; border-top: 2px dashed rgba(32, 26, 19, 0.25); }

      .timeline { list-style: none; margin: 8px 0 0; padding: 0 0 0 6px; border-left: 2px dashed rgba(32, 26, 19, 0.3); }
      .timeline li { position: relative; padding: 0 0 12px 20px; display: flex; gap: 8px; }
      .timeline .dot {
        position: absolute; left: -7px; top: 4px; width: 12px; height: 12px;
        border: 2px solid var(--ink); border-radius: 50%; background: var(--mustard);
      }
    `
  ]
})
export class AccountOrdersComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);

  readonly orders = signal<OrderSummary[]>([]);
  readonly page = signal<Paged<OrderSummary> | null>(null);
  readonly loading = signal(true);
  readonly expanded = signal<number | null>(null);
  readonly detail = signal<OrderDetail | null>(null);

  ngOnInit(): void {
    if (!this.auth.isSignedIn()) {
      this.loading.set(false);
      return;
    }

    this.load(1);
  }

  load(page: number): void {
    this.loading.set(true);

    this.api.get<Paged<OrderSummary>>('/orders', { page, pageSize: 10 }).subscribe({
      next: (result) => {
        this.orders.set(result.items ?? []);
        this.page.set(result);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  goTo(page: number): void {
    this.load(page);
  }

  toggle(orderId: number): void {
    if (this.expanded() === orderId) {
      this.expanded.set(null);
      this.detail.set(null);
      return;
    }

    this.expanded.set(orderId);
    this.detail.set(null);

    this.api.get<OrderDetail>(`/orders/${orderId}`).subscribe({
      next: (detail) => this.detail.set(detail),
      error: () => this.expanded.set(null)
    });
  }

  text(status: string): string {
    return statusLabel(status);
  }

  tone(status: string): string {
    return statusTone(status);
  }
}
