import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeError, parseUtc } from '../core/api.service';
import { InboxItem, InboxPage } from '../core/models';
import { ToastService } from '../core/toast.service';

interface Tab {
  kind: string;
  label: string;
}

const TABS: Tab[] = [
  { kind: 'support', label: 'Support' },
  { kind: 'trade-in', label: 'Trade-in' },
  { kind: 'corporate', label: 'Corporate' },
  { kind: 'feedback', label: 'Feedback' },
  { kind: 'newsletter', label: 'Newsletter' }
];

/** Everything customers send through the storefront forms, in one place. */
@Component({
  selector: 'rt-admin-inbox',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Customers</div>
        <h1 class="mb-0">Inbox</h1>
        <p class="small muted mb-0">Support tickets, trade-in requests, corporate inquiries, feedback and newsletter sign-ups.</p>
      </div>
      <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="load()" aria-label="Refresh">↻</button>
    </div>

    <nav class="tabs" aria-label="Inbox type">
      @for (t of tabs; track t.kind) {
        <button type="button" class="rt-chip" [class.is-active]="kind() === t.kind" (click)="select(t.kind)">
          {{ t.label }}
          @if (page()?.counts?.[t.kind]) {
            <span class="count">{{ page()!.counts[t.kind] }}</span>
          }
        </button>
      }
    </nav>

    @if (loading()) {
      <div class="rt-stack mt-2">
        @for (n of [1, 2, 3]; track n) {
          <div class="rt-skeleton" style="height:72px;"></div>
        }
      </div>
    } @else if (!page() || page()!.items.length === 0) {
      <div class="rt-panel center mt-2" style="padding:36px;">
        <h3 class="mt-0">Nothing here yet</h3>
        <p class="mb-0">New submissions from the storefront appear in this tab.</p>
      </div>
    } @else {
      <p class="tiny muted">{{ page()!.total }} total · showing the latest {{ page()!.items.length }}</p>

      <div class="rt-stack">
        @for (item of page()!.items; track item.id) {
          <article class="rt-panel">
            <div class="rt-row rt-row--between rt-row--wrap" style="gap:12px;">
              <div>
                <strong>{{ item.name }}</strong>
                @if (item.subject) {
                  <span class="subject"> — {{ item.subject }}</span>
                }
                <div class="small">
                  @if (item.phone) {
                    <a [href]="'tel:' + item.phone">{{ item.phone }}</a>
                  }
                  @if (item.phone && item.email) {
                    ·
                  }
                  @if (item.email && item.email !== item.name) {
                    <a [href]="'mailto:' + item.email">{{ item.email }}</a>
                  }
                </div>
                <div class="tiny muted">{{ received(item.createdAt) }}</div>
              </div>

              @if (page()!.statuses.length > 0) {
                <select
                  class="rt-select"
                  style="min-width:150px;"
                  [attr.aria-label]="'Status for ' + item.name"
                  [ngModel]="item.status"
                  (ngModelChange)="change(item, $event)"
                >
                  @for (s of page()!.statuses; track s) {
                    <option [value]="s">{{ s }}</option>
                  }
                </select>
              } @else {
                <span class="rt-tag rt-tag--teal">{{ item.status }}</span>
              }
            </div>

            @if (item.message) {
              <p class="msg">{{ item.message }}</p>
            }
          </article>
        }
      </div>
    }
  `,
  styles: [
    `
      .tabs { display: flex; flex-wrap: wrap; gap: 8px; margin: 16px 0; }
      .count {
        display: inline-block;
        min-width: 20px;
        margin-left: 6px;
        padding: 0 6px;
        border-radius: 999px;
        background: var(--rust, #965c20);
        color: #fff;
        font-size: 11px;
        font-weight: 700;
        text-align: center;
      }
      .subject { font-weight: 500; }
      .msg { margin: 10px 0 0; white-space: pre-wrap; }
    `
  ]
})
export class AdminInboxComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly tabs = TABS;
  readonly kind = signal('support');
  readonly page = signal<InboxPage | null>(null);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.load();
  }

  select(kind: string): void {
    this.kind.set(kind);
    this.load();
  }

  load(): void {
    this.loading.set(true);

    this.api.get<InboxPage>(`/admin/inbox/${this.kind()}`, { take: 50 }).subscribe({
      next: (page) => {
        this.page.set(page);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  change(item: InboxItem, status: string): void {
    if (status === item.status) {
      return;
    }

    this.api.post(`/admin/inbox/${this.kind()}/${item.id}/status`, { status }).subscribe({
      next: () => {
        this.toast.show(`${item.name}: ${status}.`);
        this.load();
      },
      error: (error: unknown) => {
        this.toast.show(describeError(error), 'error');
        this.load();
      }
    });
  }

  received(iso: string): string {
    return parseUtc(iso).toLocaleString();
  }
}
