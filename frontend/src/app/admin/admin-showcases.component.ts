import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime, takeUntil } from 'rxjs';
import { ApiService, describeError } from '../core/api.service';
import { AdminShowcase, ShowcaseProduct } from '../core/models';
import { ToastService } from '../core/toast.service';

const MAX_PRODUCTS = 24;

interface ShowcaseForm {
  id: number;
  slug: string;
  title: string;
  subtitle: string;
  sortOrder: number;
  isActive: boolean;
  products: ShowcaseProduct[];
}

const slugify = (value: string): string =>
  value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');

/** Manage the hand-picked product shelves on the storefront home page. */
@Component({
  selector: 'rt-admin-showcases',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Home content</div>
        <h1 class="mb-0">Home shelves</h1>
        <p class="small muted mb-0">Hand-picked product rows shown on the home page, in sort order.</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New shelf</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit shelf' : 'New shelf' }}</h3>

        <div class="rt-grid rt-cols-2" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="shelf-title">Title</label>
            <input
              id="shelf-title"
              name="title"
              class="rt-input"
              [(ngModel)]="f.title"
              (ngModelChange)="onTitle($event)"
              required
              maxlength="120"
            />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="shelf-slug">Slug</label>
            <input
              id="shelf-slug"
              name="slug"
              class="rt-input"
              [(ngModel)]="f.slug"
              (ngModelChange)="slugTouched = true"
              required
              maxlength="80"
              pattern="[a-z0-9]+(-[a-z0-9]+)*"
            />
          </div>
        </div>

        <div class="rt-field">
          <label class="rt-label" for="shelf-sub">Subtitle (optional)</label>
          <input id="shelf-sub" name="subtitle" class="rt-input" [(ngModel)]="f.subtitle" maxlength="200" />
        </div>

        <div class="rt-row rt-row--wrap" style="gap:20px; align-items:flex-end; margin-bottom:14px;">
          <div class="rt-field" style="margin:0; max-width:140px;">
            <label class="rt-label" for="shelf-order">Sort order</label>
            <input id="shelf-order" name="sortOrder" type="number" class="rt-input" [(ngModel)]="f.sortOrder" />
          </div>
          <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
            <input type="checkbox" name="isActive" [(ngModel)]="f.isActive" /> Active
          </label>
        </div>

        <h4 class="mb-1">Products on this shelf ({{ f.products.length }}/{{ max }})</h4>

        @if (f.products.length === 0) {
          <p class="small muted">No products yet — a shelf with no visible products is hidden on the storefront.</p>
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
          <label class="rt-label" for="shelf-search">Add a product</label>
          <input
            id="shelf-search"
            name="search"
            class="rt-input"
            placeholder="Search by name…"
            [ngModel]="search"
            (ngModelChange)="onSearch($event)"
            autocomplete="off"
          />
        </div>

        @if (results().length > 0) {
          <ul class="results">
            @for (product of results(); track product.id) {
              <li>
                <span>{{ product.name }}</span>
                <button
                  type="button"
                  class="rt-btn rt-btn--sm"
                  [disabled]="isPicked(product.id) || f.products.length >= max"
                  (click)="addProduct(product)"
                >
                  {{ isPicked(product.id) ? 'Added' : 'Add' }}
                </button>
              </li>
            }
          </ul>
        }

        <div class="rt-row" style="gap:8px; margin-top:16px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !f.title.trim() || !f.slug.trim()">
            {{ saving() ? 'Saving…' : 'Save shelf' }}
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
        <h3 class="mt-0">No shelves yet</h3>
        <p>Create a shelf such as “Trending now” to feature products on the home page.</p>
      </div>
    } @else {
      <div class="rt-stack mt-2">
        @for (shelf of items(); track shelf.id) {
          <div class="rt-panel">
            <div class="rt-row rt-row--between rt-row--wrap">
              <div>
                <strong>{{ shelf.title }}</strong>
                <span class="rt-tag" style="margin-left:8px;" [class.rt-tag--teal]="shelf.isActive" [class.rt-tag--paper]="!shelf.isActive">
                  {{ shelf.isActive ? 'Active' : 'Hidden' }}
                </span>
                <div class="tiny muted">
                  /{{ shelf.slug }} · order {{ shelf.sortOrder }} · {{ shelf.products.length }} product(s)
                </div>
              </div>
              <div class="rt-row" style="gap:6px;">
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(shelf)">Edit</button>
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(shelf)">Delete</button>
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
      .picked li::before {
        counter-increment: pick;
        content: counter(pick) '.';
        font-weight: 700;
        margin-right: 10px;
      }
      .picked .name { flex: 1; }
    `
  ]
})
export class AdminShowcasesComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly destroy$ = new Subject<void>();
  private readonly search$ = new Subject<string>();

  readonly max = MAX_PRODUCTS;
  readonly items = signal<AdminShowcase[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<ShowcaseForm | null>(null);
  readonly results = signal<ShowcaseProduct[]>([]);

  search = '';
  /** Stops the slug following the title once the editor has typed their own. */
  slugTouched = false;

  ngOnInit(): void {
    this.load();
    this.search$
      .pipe(debounceTime(250), takeUntil(this.destroy$))
      .subscribe((term) => this.runSearch(term));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);

    this.api.get<AdminShowcase[]>('/admin/showcases').subscribe({
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
    const next = Math.max(0, ...this.items().map((shelf) => shelf.sortOrder)) + 1;
    this.slugTouched = false;
    this.openForm({ id: 0, slug: '', title: '', subtitle: '', sortOrder: next, isActive: true, products: [] });
  }

  edit(shelf: AdminShowcase): void {
    this.slugTouched = true;
    this.openForm({
      id: shelf.id,
      slug: shelf.slug,
      title: shelf.title,
      subtitle: shelf.subtitle ?? '',
      sortOrder: shelf.sortOrder,
      isActive: shelf.isActive,
      products: [...shelf.products]
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
    if (!f || this.isPicked(product.id) || f.products.length >= MAX_PRODUCTS) {
      return;
    }

    f.products = [...f.products, product];
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
      sortOrder: Number(f.sortOrder) || 0,
      isActive: f.isActive,
      productIds: f.products.map((product) => product.id)
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/showcases/${f.id}`, body) : this.api.post('/admin/showcases', body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.close();
        this.toast.show('Shelf saved. It is live on the home page.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(shelf: AdminShowcase): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete the “${shelf.title}” shelf?`)) {
      return;
    }

    this.api.delete(`/admin/showcases/${shelf.id}`).subscribe({
      next: () => {
        this.toast.show('Shelf deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }

  private openForm(form: ShowcaseForm): void {
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
