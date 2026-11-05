import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { Dashboard } from '../core/models';
import { BdtPipe, RevealDirective, statusLabel, statusTone } from '../core/ui';

@Component({
  selector: 'rt-admin-dashboard',
  standalone: true,
  imports: [RouterLink, BdtPipe, RevealDirective, DatePipe],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap" style="margin-bottom:16px;">
      <div>
        <div class="kicker mono-label">All in one place</div>
        <h1 class="mb-0">Showroom dashboard</h1>
      </div>
      <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="load()">↻ Refresh</button>
    </div>

    @if (!data()) {
      <div class="rt-grid rt-cols-3">
        @for (n of [1, 2, 3, 4, 5, 6]; track n) {
          <div class="rt-skeleton" style="height:96px;"></div>
        }
      </div>
    } @else {
      @let d = data()!;
      <!-- KPI -->
      <section class="kpi-grid">
        <div class="kpi kpi--hero">
          <span class="mono-label">Sales today</span>
          <strong>{{ d.salesToday | bdt }}</strong>
          <span class="tiny">{{ d.ordersToday }} order(s) placed</span>
        </div>
        <div class="kpi">
          <span class="mono-label">Last 7 days</span>
          <strong>{{ d.salesWeek | bdt }}</strong>
          <span class="tiny">{{ d.ordersWeek }} order(s)</span>
        </div>
        <div class="kpi">
          <span class="mono-label">Last 30 days</span>
          <strong>{{ d.salesMonth | bdt }}</strong>
          <span class="tiny">{{ d.ordersMonth }} order(s)</span>
        </div>
        <div class="kpi">
          <span class="mono-label">Needs attention</span>
          <strong>{{ d.pendingOrders }}</strong>
          <span class="tiny">orders not yet shipped</span>
        </div>
      </section>

      <!-- CHART + STATUS -->
      <section class="chart-grid" rtReveal>
        <div class="rt-panel chart-panel">
          <div class="rt-row rt-row--between">
            <div>
              <span class="mono-label">Sales, last 14 days</span>
              <h3 class="mt-1 mb-0">{{ chart().total | bdt }}</h3>
            </div>
            <span class="rt-badge-soft">{{ chart().orders }} orders</span>
          </div>

          <svg class="area-chart" viewBox="0 0 640 210" role="img" aria-label="Daily sales for the last 14 days">
            <defs>
              <linearGradient id="salesFill" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stop-color="#c0491f" stop-opacity="0.45" />
                <stop offset="100%" stop-color="#c0491f" stop-opacity="0.04" />
              </linearGradient>
            </defs>

            @for (line of [0, 0.5, 1]; track line) {
              <line
                x1="8"
                [attr.y1]="10 + (190 - line * 180)"
                x2="632"
                [attr.y2]="10 + (190 - line * 180)"
                stroke="rgba(32,26,19,0.16)"
                stroke-dasharray="4 6"
              />
            }

            <path [attr.d]="chart().area" fill="url(#salesFill)" stroke="none" />
            <path [attr.d]="chart().line" fill="none" stroke="#201a13" stroke-width="3" stroke-linejoin="round" />

            @for (point of chart().points; track point.date) {
              <circle [attr.cx]="point.x" [attr.cy]="point.y" r="4" fill="#e8a61c" stroke="#201a13" stroke-width="2">
                <title>{{ point.date }} · {{ point.sales | bdt }} · {{ point.orders }} order(s)</title>
              </circle>
            }
          </svg>

          <div class="chart-axis">
            @for (label of chart().labels; track label) {
              <span>{{ label }}</span>
            }
          </div>
        </div>

        <div class="rt-panel">
          <span class="mono-label">Orders by status</span>
          <div class="status-bars">
            @for (row of d.ordersByStatus; track row.status) {
              <div class="bar-row">
                <span class="bar-label">{{ text(row.status) }}</span>
                <div class="bar-track">
                  <div class="bar-fill" [style.width.%]="barWidth(row.count)" [class.bar-fill--rust]="row.status === 'Cancelled'"></div>
                </div>
                <strong class="bar-value">{{ row.count }}</strong>
              </div>
            }
            @if (d.ordersByStatus.length === 0) {
              <p class="tiny muted">No orders yet.</p>
            }
          </div>

          <div class="rt-dashed"></div>

          <span class="mono-label">Payment mix</span>
          <div class="mix">
            <svg viewBox="0 0 140 140" class="donut" role="img" aria-label="Payment method mix">
              <circle cx="70" cy="70" r="52" fill="none" stroke="rgba(32,26,19,0.1)" stroke-width="20" />
              @for (segment of donut(); track segment.method) {
                <circle
                  cx="70"
                  cy="70"
                  r="52"
                  fill="none"
                  [attr.stroke]="segment.color"
                  stroke-width="20"
                  [attr.stroke-dasharray]="segment.dash"
                  [attr.stroke-dashoffset]="segment.offset"
                  transform="rotate(-90 70 70)"
                >
                  <title>{{ segment.method }} · {{ segment.amount | bdt }}</title>
                </circle>
              }
            </svg>

            <ul class="mix-legend">
              @for (segment of donut(); track segment.method) {
                <li>
                  <span class="swatch" [style.background]="segment.color"></span>
                  {{ segment.method }} <strong>{{ segment.count }}</strong>
                </li>
              }
            </ul>
          </div>
        </div>
      </section>

      <!-- TABLES -->
      <section class="table-grid">
        <div class="rt-panel">
          <div class="rt-row rt-row--between">
            <h3 class="mt-0 mb-0">Top products · 30 days</h3>
            <span class="mono-label">by units</span>
          </div>
          <table class="rt-table rt-table--scroll">
            <thead><tr><th>Product</th><th>Units</th><th class="right">Revenue</th></tr></thead>
            <tbody>
              @for (row of d.topProducts; track row.name) {
                <tr><td>{{ row.name }}</td><td>{{ row.quantity }}</td><td class="right">{{ row.revenue | bdt }}</td></tr>
              }
              @if (d.topProducts.length === 0) {
                <tr><td colspan="3" class="muted">No sales in this window yet.</td></tr>
              }
            </tbody>
          </table>
        </div>

        <div class="rt-panel">
          <div class="rt-row rt-row--between">
            <h3 class="mt-0 mb-0">Low stock</h3>
            <span class="rt-tag" [class.rt-tag--rust]="d.lowStock.length > 0">{{ d.lowStock.length }}</span>
          </div>
          <table class="rt-table rt-table--scroll">
            <thead><tr><th>Variant</th><th>SKU</th><th class="right">Left</th></tr></thead>
            <tbody>
              @for (row of d.lowStock; track row.variantId) {
                <tr>
                  <td>{{ row.productName }}<div class="tiny muted">{{ row.variantLabel }}</div></td>
                  <td class="tiny">{{ row.sku }}</td>
                  <td class="right"><strong [style.color]="row.stock === 0 ? 'var(--rust)' : 'var(--warn)'">{{ row.stock }}</strong></td>
                </tr>
              }
              @if (d.lowStock.length === 0) {
                <tr><td colspan="3" class="muted">Every variant has stock.</td></tr>
              }
            </tbody>
          </table>
        </div>
      </section>

      <section class="rt-panel mt-2">
        <div class="rt-row rt-row--between">
          <h3 class="mt-0 mb-0">Latest orders</h3>
          <a class="mono-label" routerLink="/admin/orders">Full order board →</a>
        </div>
        <table class="rt-table rt-table--scroll">
          <thead>
            <tr><th>Order</th><th>Placed</th><th>Status</th><th>Payment</th><th class="right">Total</th></tr>
          </thead>
          <tbody>
            @for (order of d.recentOrders; track order.id) {
              <tr>
                <td><a [routerLink]="['/admin/orders']" [queryParams]="{ q: order.orderNo }">{{ order.orderNo }}</a></td>
                <td class="small">{{ order.createdAt | date: 'short' }}</td>
                <td><span class="rt-tag" [class.rt-tag--teal]="tone(order.status) === 'ok'">{{ text(order.status) }}</span></td>
                <td class="small">{{ text(order.paymentStatus) }}</td>
                <td class="right"><strong>{{ order.total | bdt }}</strong></td>
              </tr>
            }
            @if (d.recentOrders.length === 0) {
              <tr><td colspan="5" class="muted">No orders yet.</td></tr>
            }
          </tbody>
        </table>
      </section>
    }
  `,
  styles: [
    `
      .kpi-grid {
        display: grid;
        grid-template-columns: repeat(4, minmax(0, 1fr));
        gap: 12px;
        margin-bottom: 18px;
      }

      .kpi {
        display: flex;
        flex-direction: column;
        gap: 3px;
        padding: 16px;
        background: var(--cream);
        border: 2px solid var(--ink);
        border-radius: 12px;
        box-shadow: var(--shadow-sm);
      }

      .kpi strong { font-family: var(--font-display); font-size: 24px; }
      .kpi--hero { background: var(--mustard); }

      .chart-grid {
        display: grid;
        grid-template-columns: 1.6fr 1fr;
        gap: 14px;
        margin-bottom: 16px;
      }

      .area-chart { width: 100%; height: auto; margin-top: 12px; }

      .chart-axis {
        display: flex;
        justify-content: space-between;
        font-family: var(--font-mono);
        font-size: 9px;
        letter-spacing: 0.06em;
        color: var(--ink-faint);
        margin-top: 4px;
      }

      .status-bars { display: flex; flex-direction: column; gap: 7px; margin-top: 10px; }

      .bar-row { display: grid; grid-template-columns: 82px 1fr 28px; gap: 8px; align-items: center; }
      .bar-label { font-size: 12px; font-weight: 600; }

      .bar-track {
        height: 12px;
        background: rgba(32, 26, 19, 0.1);
        border: 1.5px solid var(--ink);
        border-radius: var(--pill);
        overflow: hidden;
      }

      .bar-fill { height: 100%; background: var(--teal); transition: width 500ms var(--ease); }
      .bar-fill--rust { background: var(--rust); }
      .bar-value { font-family: var(--font-mono); font-size: 12px; text-align: right; }

      .mix { display: flex; gap: 14px; align-items: center; margin-top: 8px; }
      .donut { width: 118px; height: 118px; flex: 0 0 auto; }

      .mix-legend { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; font-size: 12.5px; }
      .mix-legend li { display: flex; align-items: center; gap: 7px; }
      .swatch { width: 12px; height: 12px; border-radius: 3px; border: 1.5px solid var(--ink); display: inline-block; }

      .table-grid {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 14px;
      }

      @media (max-width: 1100px) {
        .kpi-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
        .chart-grid, .table-grid { grid-template-columns: 1fr; }
      }
    `
  ]
})
export class AdminDashboardComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly data = signal<Dashboard | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.get<Dashboard>('/admin/dashboard').subscribe({
      next: (dashboard) => this.data.set(dashboard),
      error: () => this.data.set(null)
    });
  }

  readonly chart = computed(() => {
    const series = this.data()?.salesSeries ?? [];
    const width = 624;
    const height = 190;
    const top = 10;
    const max = Math.max(1, ...series.map((point) => point.sales));
    const step = series.length > 1 ? width / (series.length - 1) : width;

    const points = series.map((point, index) => ({
      date: point.date,
      sales: point.sales,
      orders: point.orders,
      x: 8 + index * step,
      y: top + height - (point.sales / max) * height
    }));

    const line = points.map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x.toFixed(1)},${point.y.toFixed(1)}`).join(' ');
    const area = points.length
      ? `${line} L${points[points.length - 1].x.toFixed(1)},${top + height} L${points[0].x.toFixed(1)},${top + height} Z`
      : '';

    return {
      points,
      line,
      area,
      total: series.reduce((sum, point) => sum + point.sales, 0),
      orders: series.reduce((sum, point) => sum + point.orders, 0),
      labels: [series[0]?.date?.slice(5) ?? '', series[Math.floor(series.length / 2)]?.date?.slice(5) ?? '', series[series.length - 1]?.date?.slice(5) ?? '']
    };
  });

  readonly donut = computed(() => {
    const mix = this.data()?.paymentMix ?? [];
    const total = mix.reduce((sum, row) => sum + row.amount, 0) || 1;
    const palette = ['#c0491f', '#0e4d45', '#e8a61c', '#6b7a3a', '#8e3512'];
    const circumference = 2 * Math.PI * 52;

    let cumulative = 0;

    return mix.map((row, index) => {
      const share = row.amount / total;
      const segment = {
        method: row.method,
        count: row.count,
        amount: row.amount,
        color: palette[index % palette.length],
        dash: `${(share * circumference).toFixed(2)} ${circumference.toFixed(2)}`,
        offset: (-cumulative * circumference).toFixed(2)
      };

      cumulative += share;
      return segment;
    });
  });

  readonly maxStatusCount = computed(() =>
    Math.max(1, ...(this.data()?.ordersByStatus ?? []).map((row) => row.count))
  );

  barWidth(count: number): number {
    return Math.round((count / this.maxStatusCount()) * 100);
  }

  text(status: string): string {
    return statusLabel(status);
  }

  tone(status: string): string {
    return statusTone(status);
  }
}
