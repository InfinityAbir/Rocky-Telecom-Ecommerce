import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeError, mediaUrl } from '../core/api.service';
import { ToastService } from '../core/toast.service';

interface CategoryRow {
  id: number;
  parentId: number | null;
  parentName: string | null;
  name: string;
  slug: string;
  icon: string | null;
  sortOrder: number;
  isActive: boolean;
  seoTitle: string | null;
  seoDesc: string | null;
  productCount: number;
}

interface CategoryForm {
  id: number;
  name: string;
  slug: string;
  parentId: number | null;
  icon: string;
  sortOrder: number;
  isActive: boolean;
  seoTitle: string;
  seoDesc: string;
}

interface BrandRow {
  id: number;
  name: string;
  slug: string;
  logoUrl: string | null;
  sortOrder: number;
  isActive: boolean;
  productCount: number;
}

interface BrandForm {
  id: number;
  name: string;
  slug: string;
  logoUrl: string;
  sortOrder: number;
  isActive: boolean;
}

/** Categories (with parent/child) for the catalogue tree and mega menu. */
@Component({
  selector: 'rt-admin-categories',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Catalogue</div>
        <h1 class="mb-0">Categories</h1>
        <p class="small muted mb-0">Hidden categories disappear from the shop but keep their products.</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New category</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit category' : 'New category' }}</h3>
        <div class="rt-grid rt-cols-2" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="cg-name">Name</label>
            <input id="cg-name" name="name" class="rt-input" [(ngModel)]="f.name" required maxlength="100" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="cg-slug">URL slug</label>
            <input id="cg-slug" name="slug" class="rt-input" [(ngModel)]="f.slug" maxlength="100" placeholder="Made from the name when blank" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="cg-parent">Parent category</label>
            <select id="cg-parent" name="parentId" class="rt-select" [(ngModel)]="f.parentId">
              <option [ngValue]="null">None (top level)</option>
              @for (c of parents(f.id); track c.id) {
                <option [ngValue]="c.id">{{ c.name }}</option>
              }
            </select>
          </div>
          <div class="rt-field">
            <label class="rt-label" for="cg-icon">Icon (a short word or emoji)</label>
            <input id="cg-icon" name="icon" class="rt-input" [(ngModel)]="f.icon" maxlength="30" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="cg-order">Sort order</label>
            <input id="cg-order" name="sortOrder" type="number" class="rt-input" [(ngModel)]="f.sortOrder" />
          </div>
          <label class="rt-row" style="gap:8px; min-height:44px; cursor:pointer; align-self:end;">
            <input type="checkbox" name="active" [(ngModel)]="f.isActive" /> Visible in the shop
          </label>
        </div>
        <details>
          <summary class="small">Search engine text (optional)</summary>
          <div class="rt-field mt-1">
            <label class="rt-label" for="cg-st">SEO title</label>
            <input id="cg-st" name="seoTitle" class="rt-input" [(ngModel)]="f.seoTitle" maxlength="70" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="cg-sd">SEO description</label>
            <input id="cg-sd" name="seoDesc" class="rt-input" [(ngModel)]="f.seoDesc" maxlength="160" />
          </div>
        </details>
        <div class="rt-row" style="gap:8px; margin-top:8px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !f.name.trim()">{{ saving() ? 'Saving…' : 'Save category' }}</button>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="form.set(null)">Cancel</button>
        </div>
      </form>
    }

    @if (loading()) {
      <div class="rt-skeleton mt-3" style="height:160px;"></div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead><tr><th>Category</th><th>Slug</th><th class="right">Products</th><th>State</th><th class="right">Actions</th></tr></thead>
          <tbody>
            @for (c of items(); track c.id) {
              <tr>
                <td>
                  <strong [style.padding-left.px]="c.parentId ? 18 : 0">{{ c.parentId ? '↳ ' : '' }}{{ c.name }}</strong>
                  @if (c.parentName) { <div class="tiny muted" style="padding-left:18px;">in {{ c.parentName }}</div> }
                </td>
                <td class="small mono-label">{{ c.slug }}</td>
                <td class="right">{{ c.productCount }}</td>
                <td><span class="rt-tag" [class.rt-tag--teal]="c.isActive" [class.rt-tag--paper]="!c.isActive">{{ c.isActive ? 'Visible' : 'Hidden' }}</span></td>
                <td class="right">
                  <div class="rt-row" style="justify-content:flex-end; gap:6px;">
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(c)">Edit</button>
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(c)">Delete</button>
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
export class AdminCategoriesComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly items = signal<CategoryRow[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<CategoryForm | null>(null);

  ngOnInit(): void {
    this.load();
  }

  /** Only top-level categories can be parents (two levels), and never the category itself. */
  parents(selfId: number): CategoryRow[] {
    return this.items().filter((c) => !c.parentId && c.id !== selfId);
  }

  load(): void {
    this.loading.set(true);
    this.api.get<CategoryRow[]>('/admin/categories').subscribe({
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
    const next = Math.max(0, ...this.items().map((c) => c.sortOrder)) + 1;
    this.form.set({ id: 0, name: '', slug: '', parentId: null, icon: '', sortOrder: next, isActive: true, seoTitle: '', seoDesc: '' });
  }

  edit(c: CategoryRow): void {
    this.form.set({
      id: c.id,
      name: c.name,
      slug: c.slug,
      parentId: c.parentId,
      icon: c.icon ?? '',
      sortOrder: c.sortOrder,
      isActive: c.isActive,
      seoTitle: c.seoTitle ?? '',
      seoDesc: c.seoDesc ?? ''
    });
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving()) {
      return;
    }

    const body = {
      name: f.name.trim(),
      slug: f.slug.trim() || null,
      parentId: f.parentId,
      icon: f.icon.trim() || null,
      sortOrder: Number(f.sortOrder) || 0,
      isActive: f.isActive,
      seoTitle: f.seoTitle.trim() || null,
      seoDesc: f.seoDesc.trim() || null
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/categories/${f.id}`, body) : this.api.post('/admin/categories', body);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.form.set(null);
        this.toast.show('Category saved.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(c: CategoryRow): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete “${c.name}”? Categories that still have products or sub-categories cannot be deleted.`)) {
      return;
    }

    this.api.delete(`/admin/categories/${c.id}`).subscribe({
      next: () => {
        this.toast.show('Category deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}

/** Brands shown on the brand strip and in filters. */
@Component({
  selector: 'rt-admin-brands',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Catalogue</div>
        <h1 class="mb-0">Brands</h1>
        <p class="small muted mb-0">Hidden brands disappear from the shop and filters.</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New brand</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit brand' : 'New brand' }}</h3>
        <div class="rt-grid rt-cols-2" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="br-name">Name</label>
            <input id="br-name" name="name" class="rt-input" [(ngModel)]="f.name" required maxlength="100" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="br-slug">URL slug</label>
            <input id="br-slug" name="slug" class="rt-input" [(ngModel)]="f.slug" maxlength="100" placeholder="Made from the name when blank" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="br-logo">Logo URL (optional)</label>
            <input id="br-logo" name="logoUrl" class="rt-input" [(ngModel)]="f.logoUrl" maxlength="512" placeholder="/uploads/… or https://…" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="br-order">Sort order</label>
            <input id="br-order" name="sortOrder" type="number" class="rt-input" [(ngModel)]="f.sortOrder" />
          </div>
        </div>
        <label class="rt-row" style="gap:8px; min-height:44px; cursor:pointer;">
          <input type="checkbox" name="active" [(ngModel)]="f.isActive" /> Visible in the shop
        </label>
        <div class="rt-row" style="gap:8px; margin-top:8px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !f.name.trim()">{{ saving() ? 'Saving…' : 'Save brand' }}</button>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="form.set(null)">Cancel</button>
        </div>
      </form>
    }

    @if (loading()) {
      <div class="rt-skeleton mt-3" style="height:160px;"></div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead><tr><th>Brand</th><th>Slug</th><th class="right">Products</th><th>State</th><th class="right">Actions</th></tr></thead>
          <tbody>
            @for (b of items(); track b.id) {
              <tr>
                <td>
                  <div class="rt-row" style="gap:10px;">
                    @if (b.logoUrl) { <img [src]="media(b.logoUrl)" [alt]="b.name" style="height:26px; max-width:70px; object-fit:contain;" /> }
                    <strong>{{ b.name }}</strong>
                  </div>
                </td>
                <td class="small mono-label">{{ b.slug }}</td>
                <td class="right">{{ b.productCount }}</td>
                <td><span class="rt-tag" [class.rt-tag--teal]="b.isActive" [class.rt-tag--paper]="!b.isActive">{{ b.isActive ? 'Visible' : 'Hidden' }}</span></td>
                <td class="right">
                  <div class="rt-row" style="justify-content:flex-end; gap:6px;">
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(b)">Edit</button>
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(b)">Delete</button>
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
export class AdminBrandsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly items = signal<BrandRow[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<BrandForm | null>(null);

  ngOnInit(): void {
    this.load();
  }

  media(path: string): string {
    return mediaUrl(path);
  }

  load(): void {
    this.loading.set(true);
    this.api.get<BrandRow[]>('/admin/brands').subscribe({
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
    const next = Math.max(0, ...this.items().map((b) => b.sortOrder)) + 1;
    this.form.set({ id: 0, name: '', slug: '', logoUrl: '', sortOrder: next, isActive: true });
  }

  edit(b: BrandRow): void {
    this.form.set({ id: b.id, name: b.name, slug: b.slug, logoUrl: b.logoUrl ?? '', sortOrder: b.sortOrder, isActive: b.isActive });
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving()) {
      return;
    }

    const body = {
      name: f.name.trim(),
      slug: f.slug.trim() || null,
      logoUrl: f.logoUrl.trim() || null,
      sortOrder: Number(f.sortOrder) || 0,
      isActive: f.isActive
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/brands/${f.id}`, body) : this.api.post('/admin/brands', body);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.form.set(null);
        this.toast.show('Brand saved.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(b: BrandRow): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete “${b.name}”? Brands that still have products cannot be deleted.`)) {
      return;
    }

    this.api.delete(`/admin/brands/${b.id}`).subscribe({
      next: () => {
        this.toast.show('Brand deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}
