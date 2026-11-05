import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, takeUntil } from 'rxjs';
import { ApiService, describeError, parseUtc } from '../core/api.service';
import { AdminCampaign, ShowcaseProduct } from '../core/models';
import { ToastService } from '../core/toast.service';

const MAX_PRODUCTS = 48;

interface CampaignForm {
  id: number;
  slug: string;
  title: string;
  subtitle: string;
  description: string;
  bannerUrl: string;
  startAt: string;
  endAt: string;
  sortOrder: number;
  isActive: boolean;
  products: ShowcaseProduct[];
}

const slugify = (value: string): string =>
  value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');

/** `datetime-local` works in local time; the API stores UTC (zone-less strings are UTC). */
function toLocalInput(iso: string | null): string {
  if (!iso) {
    return '';
  }

  const date = parseUtc(iso);
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
}

const toIso = (local: string): string | null => (local ? new Date(local).toISOString() : null);

/** Manage offer pages shown at /offer and /offer/{slug}. */
@Component({
  selector: 'rt-admin-campaigns',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Home content</div>
        <h1 class="mb-0">Offers</h1>
        <p class="small muted mb-0">Time-boxed offer pages. Shoppers only see an offer while it is active and inside its dates.</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New offer</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit offer' : 'New offer' }}</h3>

        <div class="rt-grid rt-cols-2" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="c-title">Title</label>
            <input id="c-title" name="title" class="rt-input" [(ngModel)]="f.title" (ngModelChange)="onTitle($event)" required maxlength="140" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-slug">Slug</label>
            <input id="c-slug" name="slug" class="rt-input" [(ngModel)]="f.slug" (ngModelChange)="slugTouched = true" required maxlength="100" pattern="[a-z0-9]+(-[a-z0-9]+)*" />
          </div>
        </div>

        <div class="rt-field">
          <label class="rt-label" for="c-sub">Subtitle (optional)</label>
          <input id="c-sub" name="subtitle" class="rt-input" [(ngModel)]="f.subtitle" maxlength="200" />
        </div>

        <div class="rt-field">
          <label class="rt-label" for="c-desc">Description (optional, plain text)</label>
          <textarea id="c-desc" name="description" class="rt-textarea" rows="4" [(ngModel)]="f.description" maxlength="4000"></textarea>
        </div>

        <div class="rt-field">
          <label class="rt-label" for="c-banner">Banner image URL (optional)</label>
          <input id="c-banner" name="bannerUrl" class="rt-input" [(ngModel)]="f.bannerUrl" maxlength="512" placeholder="/uploads/… or https://…" />
        </div>

        <div class="rt-grid rt-cols-3" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="c-start">Starts</label>
            <input id="c-start" name="startAt" type="datetime-local" class="rt-input" [(ngModel)]="f.startAt" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-end">Ends</label>
            <input id="c-end" name="endAt" type="datetime-local" class="rt-input" [(ngModel)]="f.endAt" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="c-order">Sort order</label>
            <input id="c-order" name="sortOrder" type="number" class="rt-input" [(ngModel)]="f.sortOrder" />
          </div>
        </div>

        <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
          <input type="checkbox" name="isActive" [(ngModel)]="f.isActive" /> Active
        </label>

        <h4 class="mb-1">Products in this offer ({{ f.products.length }}/{{ max }})</h4>

        @if (f.products.length === 0) {
          <p class="small muted">No products yet.</p>
        } @else {
          <ol class="picked">
            @for (product of f.products; track product.id; let i = $index) {
              <li>
                <span class="name">{{ product.name }}</span>
                <span class="rt-row" style="gap:6px;">
                  <button type="button" class="rt-btn rt-btn--ghost rt-btn--sm" [disabled]="i === 0" (click)="move(i, -1)" aria-label="Move up">↑</button>
                  <button type="button" class="rt-btn rt-btn--ghost rt-btn--sm" [disabled]="i === f.products.length - 1" (click)="move(i, 1)" aria-label="Move down">↓</button>
                  <button type="button" class="rt-btn rt-btn--ghost rt-btn--sm" (click)="removeProduct(product.id)">Remove</button>
                </span>
              </li>
            }
          </ol>
        }

        <div class="rt-field mt-2">
          <label class="rt-label" for="c-search">Add a product</label>
          <input id="c-search" name="search" class="rt-input" placeholder="Search by name…" [ngModel]="search" (ngModelChange)="onSearch($event)" autocomplete="off" />
        </div>

        @if (results().length > 0) {
          <ul class="results">
            @for (product of results(); track product.id) {
              <li>
                <span>{{ product.name }}</span>
                <button type="button" class="rt-btn rt-btn--sm" [disabled]="isPicked(product.id) || f.products.length >= max" (click)="addProduct(product)">
                  {{ isPicked(product.id) ? 'Added' : 'Add' }}
                </button>
              </li>
            }
          </ul>
        }

        <div class="rt-row" style="gap:8px; margin-top:16px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !f.title.trim() || !f.slug.trim()">
            {{ saving() ? 'Saving…' : 'Save offer' }}
          </button>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="close()">Cancel</button>
        </div>
      </form>
    }

    @if (loading()) {
      <div class="rt-stack mt-3">
        @for (n of [1, 2]; track n) {
          <div class="rt-skeleton" style="height:72px;"></div>
        }
      </div>
    } @else if (items().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;">
        <h3 class="mt-0">No offers yet</h3>
        <p>Create an offer to give shoppers a dedicated page of discounted products.</p>
      </div>
    } @else {
      <div class="rt-stack mt-2">
        @for (offer of items(); track offer.id) {
          <div class="rt-panel">
            <div class="rt-row rt-row--between rt-row--wrap">
              <div>
                <strong>{{ offer.title }}</strong>
                <span class="rt-tag" style="margin-left:8px;" [class.rt-tag--teal]="offer.isActive" [class.rt-tag--paper]="!offer.isActive">
                  {{ offer.isActive ? 'Active' : 'Hidden' }}
                </span>
                <div class="tiny muted">
                  /offer/{{ offer.slug }} · {{ offer.products.length }} product(s) · {{ windowLabel(offer) }}
                </div>
              </div>
              <div class="rt-row" style="gap:6px;">
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(offer)">Edit</button>
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(offer)">Delete</button>
              </div>
            </div>
          </div>
        }
      </div>
    }
  `,
  styles: [
    `
      .picked, .results { list-style: none; margin: 0 0 8px; padding: 0; display: grid; gap: 6px; }
      .picked { counter-reset: pick; }
      .picked li, .results li {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 10px;
        padding: 8px 12px;
        border: 1px dashed rgba(0, 0, 0, 0.25);
        border-radius: 10px;
      }
      .picked li::before { counter-increment: pick; content: counter(pick) '.'; font-weight: 700; margin-right: 10px; }
      .picked .name { flex: 1; }
    `
  ]
})
export class AdminCampaignsComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly destroy$ = new Subject<void>();
  private readonly search$ = new Subject<string>();

  readonly max = MAX_PRODUCTS;
  readonly items = signal<AdminCampaign[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<CampaignForm | null>(null);
  readonly results = signal<ShowcaseProduct[]>([]);

  search = '';
  slugTouched = false;

  ngOnInit(): void {
    this.load();
    this.search$.pipe(debounceTime(250), takeUntil(this.destroy$)).subscribe((term) => this.runSearch(term));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);

    this.api.get<AdminCampaign[]>('/admin/campaigns').subscribe({
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
    this.slugTouched = false;
    this.openForm({
      id: 0, slug: '', title: '', subtitle: '', description: '', bannerUrl: '',
      startAt: '', endAt: '', sortOrder: next, isActive: true, products: []
    });
  }

  edit(offer: AdminCampaign): void {
    this.slugTouched = true;
    this.openForm({
      id: offer.id,
      slug: offer.slug,
      title: offer.title,
      subtitle: offer.subtitle ?? '',
      description: offer.description ?? '',
      bannerUrl: offer.bannerUrl ?? '',
      startAt: toLocalInput(offer.startAt),
      endAt: toLocalInput(offer.endAt),
      sortOrder: offer.sortOrder,
      isActive: offer.isActive,
      products: [...offer.products]
    });
  }

  close(): void {
    this.form.set(null);
    this.results.set([]);
    this.search = '';
  }

  onTitle(title: string): void {
    const f = this.form();
    if (f && !f.id && !this.slugTouched) {
      f.slug = slugify(title);
    }
  }

  onSearch(term: string): void {
    this.search = term;
    this.search$.next(term);
  }

  isPicked(id: number): boolean {
    return !!this.form()?.products.some((product) => product.id === id);
  }

  addProduct(product: ShowcaseProduct): void {
    const f = this.form();
    if (f && !this.isPicked(product.id) && f.products.length < MAX_PRODUCTS) {
      f.products = [...f.products, product];
    }
  }

  removeProduct(id: number): void {
    const f = this.form();
    if (f) {
      f.products = f.products.filter((product) => product.id !== id);
    }
  }

  move(index: number, delta: -1 | 1): void {
    const f = this.form();
    const target = index + delta;
    if (!f || target < 0 || target >= f.products.length) {
      return;
    }

    const next = [...f.products];
    [next[index], next[target]] = [next[target], next[index]];
    f.products = next;
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving()) {
      return;
    }

    const body = {
      slug: f.slug.trim().toLowerCase(),
      title: f.title.trim(),
      subtitle: f.subtitle.trim() || null,
      description: f.description.trim() || null,
      bannerUrl: f.bannerUrl.trim() || null,
      startAt: toIso(f.startAt),
      endAt: toIso(f.endAt),
      sortOrder: Number(f.sortOrder) || 0,
      isActive: f.isActive,
      productIds: f.products.map((product) => product.id)
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/campaigns/${f.id}`, body) : this.api.post('/admin/campaigns', body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.close();
        this.toast.show('Offer saved.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(offer: AdminCampaign): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete the “${offer.title}” offer?`)) {
      return;
    }

    this.api.delete(`/admin/campaigns/${offer.id}`).subscribe({
      next: () => {
        this.toast.show('Offer deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }

  windowLabel(offer: AdminCampaign): string {
    if (!offer.startAt && !offer.endAt) {
      return 'no end date';
    }

    const fmt = (iso: string | null) => (iso ? parseUtc(iso).toLocaleDateString() : '…');
    return `${fmt(offer.startAt)} → ${fmt(offer.endAt)}`;
  }

  private openForm(form: CampaignForm): void {
    this.form.set(form);
    this.search = '';
    this.results.set([]);
    this.runSearch('');
  }

  private runSearch(term: string): void {
    this.api.get<ShowcaseProduct[]>('/admin/showcases/product-search', { q: term.trim() || undefined }).subscribe({
      next: (results) => this.results.set(results ?? []),
      error: () => this.results.set([])
    });
  }
}
