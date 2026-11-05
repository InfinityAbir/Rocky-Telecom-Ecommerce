import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, describeError, toApiError } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { AdminProductRow, Paged } from '../core/models';
import { ToastService } from '../core/toast.service';
import { BdtPipe } from '../core/ui';

interface ImportReport {
  applied: boolean;
  dryRun: boolean;
  totalRows: number;
  productsToCreate: number;
  productsToUpdate: number;
  variantsToCreate: number;
  variantsToUpdate: number;
  errors: { row: number; column: string | null; message: string }[];
}

@Component({
  selector: 'rt-admin-products',
  standalone: true,
  imports: [FormsModule, RouterLink, BdtPipe],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Catalogue</div>
        <h1 class="mb-0">Products</h1>
      </div>

      <div class="rt-row" style="gap:8px;">
        <select class="rt-select" style="min-width:150px;" [(ngModel)]="status" (ngModelChange)="load(1)">
          <option value="">All</option>
          <option value="published">Published</option>
          <option value="draft">Drafts</option>
        </select>
        <input class="rt-input" style="min-width:200px;" placeholder="Search name or slug" [(ngModel)]="search" (change)="load(1)" />
        <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="load(1)">↻</button>
        <a class="rt-btn rt-btn--sm" routerLink="/admin/products/new">+ New product</a>
      </div>
    </div>

    @if (isAdmin()) {
      <div class="rt-panel mt-2">
        <div class="rt-row rt-row--between rt-row--wrap" style="gap:10px;">
          <div>
            <strong>Bulk edit with a spreadsheet</strong>
            <div class="tiny muted">One row per variant. Download, edit in Excel or Sheets, upload. Nothing is saved unless every row is valid.</div>
          </div>
          <div class="rt-row rt-row--wrap" style="gap:8px;">
            <select class="rt-select" style="min-width:170px;" [(ngModel)]="csvCategory" aria-label="Category for export or template">
              <option value="">All categories</option>
              @for (c of categories(); track c.slug) {
                <option [value]="c.slug">{{ c.name }}</option>
              }
            </select>
            <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="exportCsv(false)">Export CSV</button>
            <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="exportCsv(true)" [disabled]="!csvCategory" title="Choose a category first">Blank template</button>
            <label class="rt-btn rt-btn--sm" style="cursor:pointer;">
              {{ importing() ? 'Checking…' : 'Import CSV' }}
              <input type="file" accept=".csv,text/csv" hidden (change)="pickFile($event)" [disabled]="importing()" />
            </label>
          </div>
        </div>

        @if (report(); as r) {
          <div class="mt-2" role="status">
            @if (r.errors.length) {
              <strong style="color: var(--rust);">{{ r.errors.length }} problem(s) in “{{ fileName }}”. Nothing was imported.</strong>
              <div class="errs">
                <table class="rt-table">
                  <thead><tr><th>Row</th><th>Column</th><th>Problem</th></tr></thead>
                  <tbody>
                    @for (e of r.errors; track $index) {
                      <tr><td>{{ e.row || '—' }}</td><td class="mono-label">{{ e.column || '—' }}</td><td class="small">{{ e.message }}</td></tr>
                    }
                  </tbody>
                </table>
              </div>
              <div class="tiny muted">Fix the file and upload it again.</div>
            } @else if (r.applied) {
              <strong style="color: var(--teal);">Imported {{ r.productsToCreate }} new and {{ r.productsToUpdate }} updated product(s).</strong>
            } @else {
              <strong>Ready to import “{{ fileName }}”</strong>
              <div class="small">
                {{ r.productsToCreate }} new product(s), {{ r.productsToUpdate }} updated · {{ r.variantsToCreate }} new variant(s), {{ r.variantsToUpdate }} updated · {{ r.totalRows }} row(s)
              </div>
              <div class="rt-row mt-1" style="gap:8px;">
                <button class="rt-btn rt-btn--sm" type="button" (click)="confirmImport()" [disabled]="importing()">{{ importing() ? 'Importing…' : 'Import now' }}</button>
                <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="clearReport()">Cancel</button>
              </div>
            }
          </div>
        }
      </div>
    }

    @if (loading()) {
      <div class="rt-stack mt-3">
        @for (n of [1, 2, 3]; track n) {
          <div class="rt-skeleton" style="height:64px;"></div>
        }
      </div>
    } @else if (items().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;">
        <h3 class="mt-0">No products here</h3>
        <p>Add one, or clear the filter.</p>
        <a class="rt-btn" routerLink="/admin/products/new">+ New product</a>
      </div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead>
            <tr><th>Product</th><th>Category</th><th class="right">Price</th><th class="right">Stock</th><th>State</th><th class="right">Actions</th></tr>
          </thead>
          <tbody>
            @for (product of items(); track product.id) {
              <tr>
                <td>
                  <strong>{{ product.name }}</strong>
                  <div class="tiny muted">{{ product.brandName }} · {{ product.variantCount }} variant(s)</div>
                </td>
                <td class="small">{{ product.categoryName }}</td>
                <td class="right small">
                  {{ product.minPrice | bdt }}
                  @if (product.maxPrice > product.minPrice) {
                    <div class="tiny muted">to {{ product.maxPrice | bdt }}</div>
                  }
                </td>
                <td class="right"><strong [style.color]="product.totalStock === 0 ? 'var(--rust)' : 'inherit'">{{ product.totalStock }}</strong></td>
                <td>
                  <span class="rt-tag" [class.rt-tag--teal]="product.isPublished" [class.rt-tag--paper]="!product.isPublished">
                    {{ product.isPublished ? 'Published' : 'Draft' }}
                  </span>
                </td>
                <td class="right">
                  <div class="rt-row" style="justify-content:flex-end; gap:6px;">
                    @if (product.isPublished) {
                      <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="unpublish(product)">Unpublish</button>
                    } @else {
                      <button class="rt-btn rt-btn--sm" type="button" (click)="publish(product)">Publish</button>
                    }
                    <a class="rt-btn rt-btn--ghost rt-btn--sm" [routerLink]="['/admin/products', product.id]">Edit</a>
                    <a class="rt-btn rt-btn--ghost rt-btn--sm" [routerLink]="['/product', product.slug]" target="_blank">View</a>
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
          <span class="mono-label">Page {{ page()!.page }} / {{ page()!.totalPages }}</span>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" [disabled]="page()!.page >= page()!.totalPages" (click)="load(page()!.page + 1)">Next →</button>
        </div>
      }
    }
  `,
  styles: [
    `
      .errs { max-height: 280px; overflow: auto; margin-top: 8px; border: 1px solid var(--line, #ddd); border-radius: 10px; }
    `
  ]
})
export class AdminProductsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);

  readonly isAdmin = computed(() => (this.auth.user()?.roles ?? []).includes('Admin'));
  readonly categories = signal<{ name: string; slug: string }[]>([]);
  readonly importing = signal(false);
  readonly report = signal<ImportReport | null>(null);
  csvCategory = '';
  fileName = '';
  private file: File | null = null;

  readonly items = signal<AdminProductRow[]>([]);
  readonly page = signal<Paged<AdminProductRow> | null>(null);
  readonly loading = signal(true);

  status = '';
  search = '';

  ngOnInit(): void {
    this.load(1);
    this.api.get<{ name: string; slug: string }[]>('/admin/categories').subscribe({
      next: (list) => this.categories.set(list ?? []),
      error: () => undefined
    });
  }

  exportCsv(template: boolean): void {
    this.api
      .download('/admin/products/export', 'products.csv', { categorySlug: this.csvCategory, template })
      .subscribe({ error: (error: unknown) => this.toast.show(describeError(error), 'error') });
  }

  pickFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';

    if (!file) {
      return;
    }

    this.file = file;
    this.fileName = file.name;
    this.runImport(true);
  }

  confirmImport(): void {
    this.runImport(false);
  }

  clearReport(): void {
    this.report.set(null);
    this.file = null;
  }

  private runImport(dryRun: boolean): void {
    if (!this.file) {
      return;
    }

    const form = new FormData();
    form.append('file', this.file);
    this.importing.set(true);

    this.api.upload<ImportReport>('/admin/products/import', form, { dryRun }).subscribe({
      next: (report) => {
        this.importing.set(false);
        this.report.set(report);

        if (report.applied) {
          this.toast.show('Import complete.');
          this.file = null;
          this.load(1);
        }
      },
      error: (error: unknown) => {
        this.importing.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  load(page: number): void {
    this.loading.set(true);

    this.api
      .get<Paged<AdminProductRow>>('/admin/products', {
        status: this.status || undefined,
        search: this.search || undefined,
        page,
        pageSize: 15
      })
      .subscribe({
        next: (result) => {
          this.items.set(result.items ?? []);
          this.page.set(result);
          this.loading.set(false);
        },
        error: (error) => {
          this.loading.set(false);
          this.toast.show(toApiError(error).message, 'error');
        }
      });
  }

  publish(product: AdminProductRow): void {
    this.api.post(`/admin/products/${product.id}/publish`).subscribe({
      next: () => {
        this.toast.show(`${product.name} is live on the storefront.`);
        this.load(this.page()?.page ?? 1);
      },
      error: (error) => this.toast.show(toApiError(error).message, 'error')
    });
  }

  unpublish(product: AdminProductRow): void {
    this.api.post(`/admin/products/${product.id}/unpublish`).subscribe({
      next: () => {
        this.toast.show(`${product.name} was unpublished.`);
        this.load(this.page()?.page ?? 1);
      },
      error: (error) => this.toast.show(toApiError(error).message, 'error')
    });
  }
}
