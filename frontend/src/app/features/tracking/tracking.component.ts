import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService, toApiError } from '../../core/api.service';
import { Tracking } from '../../core/models';
import { BdtPipe, statusLabel, statusTone } from '../../core/ui';

@Component({
  selector: 'rt-tracking',
  standalone: true,
  imports: [FormsModule, RouterLink, BdtPipe, DatePipe],
  template: `
    <div class="rt-container" style="padding-top:24px; max-width:920px;">
      <div class="center" style="margin-bottom:22px;">
        <div class="kicker mono-label">Order desk</div>
        <h1>Track your order</h1>
        <p>Enter the order number from your receipt and the mobile number used at checkout.</p>
      </div>

      <form class="rt-panel lookup" (ngSubmit)="lookup()">
        <div class="rt-grid rt-cols-2">
          <div class="rt-field" style="margin:0;">
            <label class="rt-label" for="orderNo">Order number</label>
            <input
              id="orderNo"
              name="orderNo"
              class="rt-input"
              [(ngModel)]="orderNo"
              placeholder="RT-261024-0001"
              required
            />
          </div>
          <div class="rt-field" style="margin:0;">
            <label class="rt-label" for="phone">Mobile number</label>
            <input
              id="phone"
              name="phone"
              class="rt-input"
              [(ngModel)]="phone"
              placeholder="01XXXXXXXXX"
              inputmode="tel"
              required
            />
          </div>
        </div>
        <button class="rt-btn rt-btn--rust mt-2" type="submit" [disabled]="loading()">
          {{ loading() ? 'Looking up…' : 'Find my order' }}
        </button>

        @if (error()) {
          <p class="rt-error mt-2 mb-0">{{ error() }}</p>
        }
      </form>

      @if (result(); as order) {
        <section class="rt-panel mt-3">
          <div class="rt-row rt-row--between rt-row--wrap">
            <div>
              <span class="mono-label">Order</span>
              <h2 class="mb-0">{{ order.orderNo }}</h2>
            </div>
            <span class="rt-tag" [class.rt-tag--teal]="tone(order.status) === 'ok'" [class.rt-tag--rust]="tone(order.status) === 'rust'">
              {{ text(order.status) }}
            </span>
          </div>

          <div class="rt-dashed"></div>

          @if (order.trackingCode) {
            <p class="small">
              Courier <strong>{{ order.courier }}</strong> · tracking <strong>{{ order.trackingCode }}</strong>
            </p>
          }

          <ol class="timeline">
            @for (step of order.timeline; track $index) {
              <li [class.done]="$last">
                <span class="dot" aria-hidden="true"></span>
                <div>
                  <strong>{{ text(step.status) }}</strong>
                  <div class="tiny muted">
                    {{ step.at | date: 'medium' }}
                    @if (step.note) { · {{ step.note }} }
                  </div>
                </div>
              </li>
            }
          </ol>

          <div class="rt-row rt-row--wrap mt-2">
            <a class="rt-btn rt-btn--ghost rt-btn--sm" routerLink="/shop">Keep shopping</a>
            <a class="rt-btn rt-btn--sm" routerLink="/track">Track another</a>
          </div>
        </section>
      }
    </div>
  `,
  styles: [
    `
      .lookup { max-width: 640px; margin: 0 auto; }

      .timeline {
        list-style: none;
        margin: 18px 0 0;
        padding: 0 0 0 6px;
        border-left: 2px dashed rgba(32, 26, 19, 0.3);
      }

      .timeline li {
        position: relative;
        padding: 0 0 18px 22px;
        display: flex;
        gap: 10px;
        align-items: flex-start;
      }

      .timeline li:last-child { padding-bottom: 0; }

      .timeline .dot {
        position: absolute;
        left: -8px;
        top: 4px;
        width: 14px;
        height: 14px;
        border: 2px solid var(--ink);
        border-radius: 50%;
        background: var(--paper);
      }

      .timeline li.done .dot { background: var(--mustard); }
      .timeline li.done strong { color: var(--rust); }
    `
  ]
})
export class TrackingComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  orderNo = '';
  phone = '';
  readonly result = signal<Tracking | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');

  ngOnInit(): void {
    const orderNo = this.route.snapshot.paramMap.get('orderNo');
    const phone = this.route.snapshot.queryParamMap.get('phone');

    if (orderNo) {
      this.orderNo = orderNo;
    }

    if (phone) {
      this.phone = phone;
    }

    if (this.orderNo && this.phone) {
      this.lookup();
    }
  }

  lookup(): void {
    this.loading.set(true);
    this.error.set('');
    this.result.set(null);

    this.api
      .get<Tracking>('/orders/track', { orderNo: this.orderNo.trim(), phone: this.phone.trim() })
      .subscribe({
        next: (tracking) => {
          this.result.set(tracking);
          this.loading.set(false);
        },
        error: (error) => {
          this.error.set(toApiError(error).message);
          this.loading.set(false);
        }
      });
  }

  text(status: string): string {
    return statusLabel(status);
  }

  tone(status: string): string {
    return statusTone(status);
  }
}
