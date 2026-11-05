import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService, describeError, mediaUrl } from '../core/api.service';
import { ToastService } from '../core/toast.service';

interface Option {
  id: number;
  name: string;
}

interface VariantRow {
  id: number | null;
  sku: string;
  color: string;
  colorHex: string;
  ram: string;
  storage: string;
  region: string;
  simType: string;
  regularPrice: number;
  salePrice: number | null;
  stock: number;
}

interface ImageRow {
  id: number | null;
  url: string;
  alt: string;
  variantId: number | null;
}

interface SpecField {
  specFieldId: number;
  key: string;
  label: string;
  unit: string | null;
  options: string | null;
  dataType: 'text' | 'number' | 'bool' | 'option';
  isRequired: boolean;
  value: string | null;
}

interface SpecGroup {
  name: string;
  fields: SpecField[];
}

interface SpecSheet {
  templateName: string | null;
  groups: SpecGroup[];
}

interface ProductDetail {
  id: number;
  name: string;
  slug: string;
  brandId: number;
  categoryId: number;
  shortDesc: string | null;
  description: string | null;
  type: string;
  condition: string;
  warrantyType: string;
  warrantyMonths: number | null;
  isFeatured: boolean;
  isPreOrder: boolean;
  isPublished: boolean;
  badge: string | null;
  seoTitle: string | null;
  seoDesc: string | null;
  variants: VariantRow[];
  images: { id: number; url: string; alt: string | null; sortOrder: number; variantId: number | null }[];
}

interface UploadedImage {
  url: string;
}

const blankVariant = (): VariantRow => ({
  id: null,
  sku: '',
  color: '',
  colorHex: '',
  ram: '',
  storage: '',
  region: '',
  simType: '',
  regularPrice: 0,
  salePrice: null,
  stock: 0
});

/** Create or edit one product: basics, variants, images and the category's spec sheet. */
@Component({
  selector: 'rt-admin-product-editor',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <a class="small" routerLink="/admin/products">← All products</a>
        <h1 class="mb-0">{{ id() ? name || 'Edit product' : 'New product' }}</h1>
        @if (id()) {
          <span class="rt-tag" [class.rt-tag--teal]="published()" [class.rt-tag--paper]="!published()">
            {{ published() ? 'Published' : 'Draft' }}
          </span>
        }
      </div>

      @if (id()) {
        <div class="rt-row" style="gap:8px;">
          @if (published()) {
            <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="unpublish()">Unpublish</button>
          } @else {
            <button class="rt-btn rt-btn--sm" type="button" (click)="publish()">Publish</button>
          }
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="duplicate()">Duplicate</button>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove()">Delete</button>
        </div>
      }
    </div>

    @if (blockers().length) {
      <div class="rt-panel mt-2" style="border-color: var(--rust);">
        <strong>Cannot publish yet</strong>
        <ul class="small mb-0">
          @for (b of blockers(); track b) {
            <li>{{ b }}</li>
          }
        </ul>
      </div>
    }

    @if (loading()) {
      <div class="rt-skeleton mt-3" style="height:260px;"></div>
    } @else {
      <form class="rt-stack mt-2" (ngSubmit)="save()" #productForm="ngForm">
        <section class="rt-panel">
          <h3 class="mt-0">Basics</h3>
          <div class="rt-grid rt-cols-2" style="gap:12px;">
            <div class="rt-field">
              <label class="rt-label" for="pe-name">Name</label>
              <input id="pe-name" name="name" class="rt-input" [(ngModel)]="name" required maxlength="200" />
            </div>
            <div class="rt-field">
              <label class="rt-label" for="pe-slug">URL slug</label>
              <input id="pe-slug" name="slug" class="rt-input" [(ngModel)]="slug" maxlength="120" [disabled]="published()" placeholder="Made from the name when blank" />
              @if (published()) {
                <span class="tiny muted">Locked while published so links keep working.</span>
              }
            </div>
            <div class="rt-field">
              <label class="rt-label" for="pe-brand">Brand</label>
              <select id="pe-brand" name="brandId" class="rt-select" [(ngModel)]="brandId" required>
                <option [ngValue]="0" disabled>Choose…</option>
                @for (b of brands(); track b.id) {
                  <option [ngValue]="b.id">{{ b.name }}</option>
                }
              </select>
            </div>
            <div class="rt-field">
              <label class="rt-label" for="pe-cat">Category</label>
              <select id="pe-cat" name="categoryId" class="rt-select" [(ngModel)]="categoryId" required>
                <option [ngValue]="0" disabled>Choose…</option>
                @for (c of categories(); track c.id) {
                  <option [ngValue]="c.id">{{ c.name }}</option>
                }
              </select>
              @if (id()) {
                <span class="tiny muted">Changing the category changes which spec sheet applies.</span>
              }
            </div>
          </div>

          <div class="rt-field">
            <label class="rt-label" for="pe-short">Short description (shown on cards)</label>
            <input id="pe-short" name="shortDesc" class="rt-input" [(ngModel)]="shortDesc" maxlength="300" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="pe-desc">Description</label>
            <textarea id="pe-desc" name="description" class="rt-input" rows="5" [(ngModel)]="description"></textarea>
          </div>

          <div class="rt-grid rt-cols-3" style="gap:12px;">
            <div class="rt-field">
              <label class="rt-label" for="pe-type">Type</label>
              <select id="pe-type" name="type" class="rt-select" [(ngModel)]="type">
                <option value="official">Official</option>
                <option value="unofficial">Unofficial</option>
                <option value="used">Used</option>
              </select>
            </div>
            @if (type === 'used') {
              <div class="rt-field">
                <label class="rt-label" for="pe-cond">Condition</label>
                <select id="pe-cond" name="condition" class="rt-select" [(ngModel)]="condition">
                  <option value="likeNew">Like new</option>
                  <option value="good">Good</option>
                  <option value="fair">Fair</option>
                </select>
              </div>
            }
            <div class="rt-field">
              <label class="rt-label" for="pe-war">Warranty</label>
              <select id="pe-war" name="warrantyType" class="rt-select" [(ngModel)]="warrantyType">
                <option value="none">None</option>
                <option value="official">Official</option>
                <option value="shop">Shop</option>
              </select>
            </div>
            @if (warrantyType !== 'none') {
              <div class="rt-field">
                <label class="rt-label" for="pe-wm">Warranty months</label>
                <input id="pe-wm" name="warrantyMonths" type="number" min="1" max="120" class="rt-input" [(ngModel)]="warrantyMonths" />
              </div>
            }
            <div class="rt-field">
              <label class="rt-label" for="pe-badge">Badge</label>
              <input id="pe-badge" name="badge" class="rt-input" [(ngModel)]="badge" maxlength="30" placeholder="Hot, New, Top selling…" />
            </div>
          </div>

          <div class="rt-row rt-row--wrap" style="gap:22px;">
            <label class="rt-row" style="gap:8px; min-height:44px; cursor:pointer;"><input type="checkbox" name="featured" [(ngModel)]="isFeatured" /> Featured on home</label>
            <label class="rt-row" style="gap:8px; min-height:44px; cursor:pointer;"><input type="checkbox" name="preorder" [(ngModel)]="isPreOrder" /> Pre-order only</label>
          </div>

          <details>
            <summary class="small">Search engine text (optional)</summary>
            <div class="rt-field mt-1">
              <label class="rt-label" for="pe-st">SEO title (up to 70)</label>
              <input id="pe-st" name="seoTitle" class="rt-input" [(ngModel)]="seoTitle" maxlength="70" />
            </div>
            <div class="rt-field">
              <label class="rt-label" for="pe-sd">SEO description (up to 160)</label>
              <input id="pe-sd" name="seoDesc" class="rt-input" [(ngModel)]="seoDesc" maxlength="160" />
            </div>
          </details>
        </section>

        <section class="rt-panel">
          <div class="rt-row rt-row--between">
            <h3 class="mt-0 mb-0">Variants</h3>
            <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="addVariant()">+ Add variant</button>
          </div>
          <p class="small muted">One row per sellable combination. Each needs its own SKU, price and stock. Leave sale price empty for no discount.</p>

          <div class="scroll">
            <table class="rt-table">
              <thead>
                <tr><th>SKU</th><th>Colour</th><th>Hex</th><th>RAM</th><th>Storage</th><th>Region</th><th>SIM</th><th>Price</th><th>Sale</th><th>Stock</th><th></th></tr>
              </thead>
              <tbody>
                @for (v of variants(); track $index; let i = $index) {
                  <tr>
                    <td><input class="rt-input cell wide" [name]="'sku' + i" [(ngModel)]="v.sku" maxlength="60" required aria-label="SKU" /></td>
                    <td><input class="rt-input cell" [name]="'col' + i" [(ngModel)]="v.color" maxlength="40" aria-label="Colour" /></td>
                    <td><input class="rt-input cell narrow" [name]="'hex' + i" [(ngModel)]="v.colorHex" maxlength="7" placeholder="#000000" aria-label="Colour code" /></td>
                    <td><input class="rt-input cell narrow" [name]="'ram' + i" [(ngModel)]="v.ram" maxlength="20" aria-label="RAM" /></td>
                    <td><input class="rt-input cell narrow" [name]="'sto' + i" [(ngModel)]="v.storage" maxlength="20" aria-label="Storage" /></td>
                    <td><input class="rt-input cell narrow" [name]="'reg' + i" [(ngModel)]="v.region" maxlength="30" aria-label="Region" /></td>
                    <td><input class="rt-input cell narrow" [name]="'sim' + i" [(ngModel)]="v.simType" maxlength="30" aria-label="SIM type" /></td>
                    <td><input class="rt-input cell narrow" type="number" min="0" [name]="'pr' + i" [(ngModel)]="v.regularPrice" aria-label="Regular price" /></td>
                    <td><input class="rt-input cell narrow" type="number" min="0" [name]="'sp' + i" [(ngModel)]="v.salePrice" aria-label="Sale price" /></td>
                    <td><input class="rt-input cell narrow" type="number" min="0" [name]="'st' + i" [(ngModel)]="v.stock" aria-label="Stock" /></td>
                    <td><button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="removeVariant(i)" aria-label="Remove variant">✕</button></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>

        <section class="rt-panel">
          <div class="rt-row rt-row--between rt-row--wrap">
            <h3 class="mt-0 mb-0">Images</h3>
            <label class="rt-btn rt-btn--ghost rt-btn--sm" style="cursor:pointer;">
              {{ uploading() ? 'Uploading…' : '+ Upload images' }}
              <input type="file" accept="image/jpeg,image/png,image/webp" multiple hidden (change)="upload($event)" [disabled]="uploading()" />
            </label>
          </div>
          <p class="small muted">JPEG, PNG or WebP up to 5 MB, five at a time. The first image is the main one; use the arrows to reorder.</p>

          @if (images().length === 0) {
            <p class="small">No images yet.</p>
          } @else {
            <div class="gallery">
              @for (img of images(); track img.url; let i = $index) {
                <div class="tile">
                  <img [src]="media(img.url)" [alt]="img.alt || name" loading="lazy" />
                  @if (i === 0) { <span class="rt-tag rt-tag--teal main">Main</span> }
                  <div class="rt-row" style="gap:4px; justify-content:center;">
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="moveImage(i, -1)" [disabled]="i === 0" aria-label="Move earlier">←</button>
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="moveImage(i, 1)" [disabled]="i === images().length - 1" aria-label="Move later">→</button>
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="removeImage(i)" aria-label="Remove image">✕</button>
                  </div>
                </div>
              }
            </div>
          }
        </section>

        <div class="rt-row" style="gap:8px;">
          <button class="rt-btn" type="submit" [disabled]="saving() || !productForm.valid">{{ saving() ? 'Saving…' : id() ? 'Save changes' : 'Create draft' }}</button>
          <a class="rt-btn rt-btn--ghost" routerLink="/admin/products">Cancel</a>
        </div>
      </form>

      @if (id()) {
        <section class="rt-panel mt-2">
          <div class="rt-row rt-row--between rt-row--wrap">
            <div>
              <h3 class="mt-0 mb-0">Specifications</h3>
              <span class="tiny muted">{{ sheet()?.templateName ? 'From the template “' + sheet()!.templateName + '”' : 'This category has no spec template yet.' }}</span>
            </div>
            @if (sheet()?.groups?.length) {
              <button class="rt-btn rt-btn--sm" type="button" (click)="saveSpecs()" [disabled]="savingSpecs()">{{ savingSpecs() ? 'Saving…' : 'Save specs' }}</button>
            }
          </div>

          @for (group of sheet()?.groups ?? []; track group.name) {
            <h4>{{ group.name }}</h4>
            <div class="rt-grid rt-cols-2" style="gap:10px 12px;">
              @for (field of group.fields; track field.specFieldId) {
                <div class="rt-field">
                  <label class="rt-label" [for]="'sf' + field.specFieldId">
                    {{ field.label }}@if (field.unit) { ({{ field.unit }}) }@if (field.isRequired) { <span class="req" title="Required to publish"> *</span> }
                  </label>
                  @if (field.dataType === 'option' && optionList(field).length) {
                    <select [id]="'sf' + field.specFieldId" class="rt-select" [(ngModel)]="field.value" [name]="'sf' + field.specFieldId">
                      <option [ngValue]="null">—</option>
                      @for (o of optionList(field); track o) {
                        <option [ngValue]="o">{{ o }}</option>
                      }
                    </select>
                  } @else if (field.dataType === 'bool') {
                    <select [id]="'sf' + field.specFieldId" class="rt-select" [(ngModel)]="field.value" [name]="'sf' + field.specFieldId">
                      <option [ngValue]="null">—</option>
                      <option value="Yes">Yes</option>
                      <option value="No">No</option>
                    </select>
                  } @else {
                    <input [id]="'sf' + field.specFieldId" class="rt-input" [(ngModel)]="field.value" [name]="'sf' + field.specFieldId" [attr.inputmode]="field.dataType === 'number' ? 'decimal' : null" />
                  }
                </div>
              }
            </div>
          }
        </section>
      } @else {
        <p class="small muted mt-2">Save the draft first, then fill in the specifications and publish.</p>
      }
    }
  `,
  styles: [
    `
      .scroll { overflow-x: auto; }
      .cell { min-height: 40px; padding: 6px 8px; }
      .cell.wide { min-width: 150px; }
      .cell.narrow { min-width: 86px; }
      .gallery { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 12px; }
      .tile { position: relative; display: grid; gap: 6px; }
      .tile img { width: 100%; aspect-ratio: 1; object-fit: contain; background: var(--paper-2, #f4f1ea); border-radius: 10px; }
      .main { position: absolute; top: 6px; left: 6px; }
      .req { color: var(--rust, #b3402a); }
    `
  ]
})
export class AdminProductEditorComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly id = signal<number | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly savingSpecs = signal(false);
  readonly uploading = signal(false);
  readonly published = signal(false);
  readonly brands = signal<Option[]>([]);
  readonly categories = signal<Option[]>([]);
  readonly variants = signal<VariantRow[]>([blankVariant()]);
  readonly images = signal<ImageRow[]>([]);
  readonly sheet = signal<SpecSheet | null>(null);
  readonly blockers = signal<string[]>([]);

  name = '';
  slug = '';
  brandId = 0;
  categoryId = 0;
  shortDesc = '';
  description = '';
  type = 'official';
  condition = 'new';
  warrantyType = 'none';
  warrantyMonths: number | null = null;
  isFeatured = false;
  isPreOrder = false;
  badge = '';
  seoTitle = '';
  seoDesc = '';

  readonly hasSheet = computed(() => (this.sheet()?.groups.length ?? 0) > 0);

  media(path: string): string {
    return mediaUrl(path);
  }

  optionList(field: SpecField): string[] {
    return (field.options ?? '').split('|').map((o) => o.trim()).filter(Boolean);
  }

  ngOnInit(): void {
    this.api.get<Option[]>('/admin/brands').subscribe({ next: (b) => this.brands.set(b ?? []) });
    this.api.get<Option[]>('/admin/categories').subscribe({ next: (c) => this.categories.set(c ?? []) });

    const param = this.route.snapshot.paramMap.get('id');
    const id = param && param !== 'new' ? Number(param) : null;

    if (!id) {
      this.loading.set(false);
      return;
    }

    this.load(id);
  }

  private load(id: number): void {
    this.loading.set(true);
    this.id.set(id);

    this.api.get<ProductDetail>(`/admin/products/${id}`).subscribe({
      next: (p) => {
        this.name = p.name;
        this.slug = p.slug;
        this.brandId = p.brandId;
        this.categoryId = p.categoryId;
        this.shortDesc = p.shortDesc ?? '';
        this.description = p.description ?? '';
        this.type = p.type;
        this.condition = p.condition;
        this.warrantyType = p.warrantyType;
        this.warrantyMonths = p.warrantyMonths;
        this.isFeatured = p.isFeatured;
        this.isPreOrder = p.isPreOrder;
        this.badge = p.badge ?? '';
        this.seoTitle = p.seoTitle ?? '';
        this.seoDesc = p.seoDesc ?? '';
        this.published.set(p.isPublished);
        this.variants.set(
          p.variants.length
            ? p.variants.map((v) => ({
                ...v,
                color: v.color ?? '',
                colorHex: v.colorHex ?? '',
                ram: v.ram ?? '',
                storage: v.storage ?? '',
                region: v.region ?? '',
                simType: v.simType ?? ''
              }))
            : [blankVariant()]
        );
        this.images.set(
          [...p.images].sort((a, b) => a.sortOrder - b.sortOrder).map((i) => ({ id: i.id, url: i.url, alt: i.alt ?? '', variantId: i.variantId }))
        );
        this.loading.set(false);
        this.loadSpecs();
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  private loadSpecs(): void {
    const id = this.id();
    if (!id) {
      return;
    }

    this.api.get<SpecSheet>(`/admin/products/${id}/specs`).subscribe({
      next: (sheet) => this.sheet.set(sheet),
      error: () => this.sheet.set(null)
    });
  }

  addVariant(): void {
    this.variants.update((list) => [...list, blankVariant()]);
  }

  removeVariant(index: number): void {
    this.variants.update((list) => (list.length > 1 ? list.filter((_, i) => i !== index) : list));
  }

  moveImage(index: number, delta: number): void {
    this.images.update((list) => {
      const next = [...list];
      const target = index + delta;
      if (target < 0 || target >= next.length) {
        return list;
      }

      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });
  }

  removeImage(index: number): void {
    this.images.update((list) => list.filter((_, i) => i !== index));
  }

  upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';

    if (files.length === 0) {
      return;
    }

    const form = new FormData();
    files.slice(0, 5).forEach((file) => form.append('files', file));

    this.uploading.set(true);
    this.api.upload<UploadedImage[]>('/admin/products/images', form).subscribe({
      next: (stored) => {
        this.uploading.set(false);
        this.images.update((list) => [
          ...list,
          ...stored.map((s) => ({ id: null, url: s.url, alt: this.name, variantId: null }))
        ]);
        this.toast.show(`${stored.length} image(s) added. Save to keep them.`);
      },
      error: (error: unknown) => {
        this.uploading.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  private body(): Record<string, unknown> {
    const trim = (value: string) => value.trim() || null;

    return {
      name: this.name.trim(),
      slug: this.published() ? undefined : trim(this.slug),
      brandId: this.brandId,
      categoryId: this.categoryId,
      shortDesc: trim(this.shortDesc),
      description: trim(this.description),
      type: this.type,
      condition: this.type === 'used' ? this.condition : 'new',
      warrantyType: this.warrantyType,
      warrantyMonths: this.warrantyType === 'none' ? null : this.warrantyMonths || null,
      isFeatured: this.isFeatured,
      isPreOrder: this.isPreOrder,
      badge: trim(this.badge),
      seoTitle: trim(this.seoTitle),
      seoDesc: trim(this.seoDesc),
      variants: this.variants().map((v) => ({
        id: v.id,
        sku: v.sku.trim(),
        color: trim(v.color),
        colorHex: trim(v.colorHex),
        ram: trim(v.ram),
        storage: trim(v.storage),
        region: trim(v.region),
        simType: trim(v.simType),
        regularPrice: Number(v.regularPrice) || 0,
        salePrice: v.salePrice === null || (v.salePrice as unknown) === '' ? null : Number(v.salePrice),
        stock: Number(v.stock) || 0
      })),
      images: this.images().map((img, index) => ({
        id: img.id,
        url: img.url,
        alt: img.alt || this.name.trim(),
        sortOrder: index + 1,
        variantId: img.variantId
      }))
    };
  }

  save(): void {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.blockers.set([]);
    const id = this.id();

    if (id) {
      this.api.put(`/admin/products/${id}`, this.body()).subscribe({
        next: () => {
          this.saving.set(false);
          this.toast.show('Product saved.');
          this.load(id);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.toast.show(describeError(error), 'error');
        }
      });
      return;
    }

    this.api.post<{ id: number }>('/admin/products', this.body()).subscribe({
      next: (created) => {
        this.saving.set(false);
        this.toast.show('Draft created. Add specs, then publish.');
        void this.router.navigate(['/admin/products', created.id]).then(() => this.load(created.id));
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  saveSpecs(): void {
    const id = this.id();
    const sheet = this.sheet();
    if (!id || !sheet) {
      return;
    }

    const values = sheet.groups.flatMap((g) =>
      g.fields.map((f) => ({ specFieldId: f.specFieldId, value: f.value === null || f.value === undefined ? null : String(f.value).trim() || null }))
    );

    this.savingSpecs.set(true);
    this.api.put(`/admin/products/${id}/specs`, { values }).subscribe({
      next: () => {
        this.savingSpecs.set(false);
        this.toast.show('Specs saved.');
        this.blockers.set([]);
        this.loadSpecs();
      },
      error: (error: unknown) => {
        this.savingSpecs.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  publish(): void {
    const id = this.id();
    if (!id) {
      return;
    }

    this.api.post(`/admin/products/${id}/publish`).subscribe({
      next: () => {
        this.published.set(true);
        this.blockers.set([]);
        this.toast.show('Published. It is live on the storefront.');
      },
      error: (error: unknown) => {
        const fields = (error as { fields?: Record<string, string[]> })?.fields;
        const list = Object.values(fields ?? {}).flat();
        this.blockers.set(list.length ? list : [describeError(error)]);
        this.toast.show('Fix the items listed above, then publish again.', 'error');
      }
    });
  }

  unpublish(): void {
    const id = this.id();
    if (!id) {
      return;
    }

    this.api.post(`/admin/products/${id}/unpublish`).subscribe({
      next: () => {
        this.published.set(false);
        this.toast.show('Back to draft.');
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }

  duplicate(): void {
    const id = this.id();
    if (!id) {
      return;
    }

    this.api.post<{ id: number }>(`/admin/products/${id}/duplicate`).subscribe({
      next: (copy) => {
        this.toast.show('Copied as a draft.');
        void this.router.navigate(['/admin/products', copy.id]).then(() => this.load(copy.id));
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }

  remove(): void {
    const id = this.id();
    if (!id || (typeof window !== 'undefined' && !window.confirm(`Delete “${this.name}”? Products with orders cannot be deleted; unpublish them instead.`))) {
      return;
    }

    this.api.delete(`/admin/products/${id}`).subscribe({
      next: () => {
        this.toast.show('Product deleted.');
        void this.router.navigate(['/admin/products']);
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}
