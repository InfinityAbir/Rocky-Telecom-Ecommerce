import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeError, parseUtc } from '../core/api.service';
import { AdminNewsItem } from '../core/models';
import { ToastService } from '../core/toast.service';

interface NewsForm {
  id: number;
  title: string;
  linkUrl: string;
  openInNewTab: boolean;
  sortOrder: number;
  startAt: string;
  endAt: string;
  isActive: boolean;
}

/** `datetime-local` inputs work in the browser's local time; the API stores UTC. */
function toLocalInput(iso: string | null): string {
  if (!iso) {
    return '';
  }

  const date = parseUtc(iso);
  const offset = date.getTimezoneOffset() * 60000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

function toIso(local: string): string | null {
  return local ? new Date(local).toISOString() : null;
}

const blank = (): NewsForm => ({
  id: 0,
  title: '',
  linkUrl: '',
  openInNewTab: false,
  sortOrder: 0,
  startAt: '',
  endAt: '',
  isActive: true
});

/** Manage the announcement ticker shown above the storefront header. */
@Component({
  selector: 'rt-admin-news',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Home content</div>
        <h1 class="mb-0">Announcement ticker</h1>
        <p class="small muted mb-0">Lines scroll above the header on every page, in sort order.</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New line</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" #editor="ngForm" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit line' : 'New line' }}</h3>

        <div class="rt-field">
          <label class="rt-label" for="news-title">Text</label>
          <input id="news-title" name="title" class="rt-input" [(ngModel)]="f.title" required maxlength="200" />
        </div>

        <div class="rt-field">
          <label class="rt-label" for="news-link">Link (optional)</label>
          <input
            id="news-link"
            name="linkUrl"
            class="rt-input"
            [(ngModel)]="f.linkUrl"
            maxlength="512"
            placeholder="/policy/delivery or https://…"
          />
          <p class="tiny muted mb-0">A page on this site (starting with /) or a full http(s) address.</p>
        </div>

        <div class="rt-grid rt-cols-3" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="news-order">Sort order</label>
            <input id="news-order" name="sortOrder" type="number" class="rt-input" [(ngModel)]="f.sortOrder" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="news-start">Show from</label>
            <input id="news-start" name="startAt" type="datetime-local" class="rt-input" [(ngModel)]="f.startAt" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="news-end">Show until</label>
            <input id="news-end" name="endAt" type="datetime-local" class="rt-input" [(ngModel)]="f.endAt" />
          </div>
        </div>

        <div class="rt-row rt-row--wrap" style="gap:20px; margin:8px 0 14px;">
          <label class="rt-row" style="gap:8px; cursor:pointer;">
            <input type="checkbox" name="isActive" [(ngModel)]="f.isActive" /> Active
          </label>
          <label class="rt-row" style="gap:8px; cursor:pointer;">
            <input type="checkbox" name="openInNewTab" [(ngModel)]="f.openInNewTab" /> Open link in a new tab
          </label>
        </div>

        <div class="rt-row" style="gap:8px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !f.title.trim()">
            {{ saving() ? 'Saving…' : 'Save' }}
          </button>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="form.set(null)">Cancel</button>
        </div>
      </form>
    }

    @if (loading()) {
      <div class="rt-stack mt-3">
        @for (n of [1, 2, 3]; track n) {
          <div class="rt-skeleton" style="height:56px;"></div>
        }
      </div>
    } @else if (items().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;">
        <h3 class="mt-0">No ticker lines</h3>
        <p>The storefront shows its built-in announcements until you add one.</p>
      </div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead>
            <tr><th>Text</th><th>Link</th><th class="right">Order</th><th>State</th><th class="right">Actions</th></tr>
          </thead>
          <tbody>
            @for (item of items(); track item.id) {
              <tr>
                <td>
                  <strong>{{ item.title }}</strong>
                  @if (item.startAt || item.endAt) {
                    <div class="tiny muted">{{ windowLabel(item) }}</div>
                  }
                </td>
                <td class="small">{{ item.linkUrl || '—' }}</td>
                <td class="right small">{{ item.sortOrder }}</td>
                <td>
                  <span class="rt-tag" [class.rt-tag--teal]="item.isActive" [class.rt-tag--paper]="!item.isActive">
                    {{ item.isActive ? 'Active' : 'Hidden' }}
                  </span>
                </td>
                <td class="right">
                  <div class="rt-row" style="justify-content:flex-end; gap:6px;">
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(item)">Edit</button>
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(item)">Delete</button>
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
export class AdminNewsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly items = signal<AdminNewsItem[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<NewsForm | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);

    this.api.get<AdminNewsItem[]>('/admin/news').subscribe({
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
    const next = Math.max(0, ...this.items().map((item) => item.sortOrder)) + 1;
    this.form.set({ ...blank(), sortOrder: next });
  }

  edit(item: AdminNewsItem): void {
    this.form.set({
      id: item.id,
      title: item.title,
      linkUrl: item.linkUrl ?? '',
      openInNewTab: item.openInNewTab,
      sortOrder: item.sortOrder,
      startAt: toLocalInput(item.startAt),
      endAt: toLocalInput(item.endAt),
      isActive: item.isActive
    });
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving()) {
      return;
    }

    const body = {
      title: f.title.trim(),
      linkUrl: f.linkUrl.trim() || null,
      openInNewTab: f.openInNewTab,
      sortOrder: Number(f.sortOrder) || 0,
      startAt: toIso(f.startAt),
      endAt: toIso(f.endAt),
      isActive: f.isActive
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/news/${f.id}`, body) : this.api.post('/admin/news', body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.form.set(null);
        this.toast.show('Ticker line saved. Shoppers see it on their next visit.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(item: AdminNewsItem): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete “${item.title}”?`)) {
      return;
    }

    this.api.delete(`/admin/news/${item.id}`).subscribe({
      next: () => {
        this.toast.show('Ticker line deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }

  windowLabel(item: AdminNewsItem): string {
    const fmt = (iso: string | null) => (iso ? parseUtc(iso).toLocaleString() : 'always');
    return `${fmt(item.startAt)} → ${fmt(item.endAt)}`;
  }
}
