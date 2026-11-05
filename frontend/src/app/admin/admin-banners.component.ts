import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeError, mediaUrl } from '../core/api.service';
import { AdminBanner } from '../core/models';
import { ToastService } from '../core/toast.service';
import { toIso, toLocalInput, when } from './admin-utils';

interface BannerForm {
  id: number;
  placement: string;
  title: string;
  imageUrl: string;
  link: string;
  sortOrder: number;
  startAt: string;
  endAt: string;
  isActive: boolean;
}

const PLACEMENTS = [
  { value: 'home.hero', label: 'Home — rotating hero banners' },
  { value: 'home.promo', label: 'Home — promo pair (first two shown)' }
];

/** Banners on the home page. Wide images (about 1600 × 520) look best. */
@Component({
  selector: 'rt-admin-banners',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Home content</div>
        <h1 class="mb-0">Banners</h1>
        <p class="small muted mb-0">Shown on the home page in sort order, only while active and inside their dates.</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New banner</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit banner' : 'New banner' }}</h3>

        <div class="rt-grid rt-cols-2" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="bn-place">Where it appears</label>
            <select id="bn-place" name="placement" class="rt-select" [(ngModel)]="f.placement">
              @for (p of placements; track p.value) {
                <option [value]="p.value">{{ p.label }}</option>
              }
            </select>
          </div>
          <div class="rt-field">
            <label class="rt-label" for="bn-title">Title (used as the image description)</label>
            <input id="bn-title" name="title" class="rt-input" [(ngModel)]="f.title" maxlength="140" />
          </div>
        </div>

        <div class="rt-field">
          <label class="rt-label" for="bn-img">Image URL</label>
          <input id="bn-img" name="imageUrl" class="rt-input" [(ngModel)]="f.imageUrl" required maxlength="512" placeholder="/uploads/… or https://…" />
        </div>

        @if (f.imageUrl.trim()) {
          <img class="preview" [src]="media(f.imageUrl)" alt="Banner preview" />
        }

        <div class="rt-field">
          <label class="rt-label" for="bn-link">Click goes to (optional)</label>
          <input id="bn-link" name="link" class="rt-input" [(ngModel)]="f.link" maxlength="512" placeholder="/offer or https://…" />
        </div>

        <div class="rt-grid rt-cols-3" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="bn-order">Sort order</label>
            <input id="bn-order" name="sortOrder" type="number" class="rt-input" [(ngModel)]="f.sortOrder" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="bn-start">Show from</label>
            <input id="bn-start" name="startAt" type="datetime-local" class="rt-input" [(ngModel)]="f.startAt" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="bn-end">Show until</label>
            <input id="bn-end" name="endAt" type="datetime-local" class="rt-input" [(ngModel)]="f.endAt" />
          </div>
        </div>

        <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
          <input type="checkbox" name="active" [(ngModel)]="f.isActive" /> Active
        </label>

        <div class="rt-row" style="gap:8px; margin-top:8px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !f.imageUrl.trim()">{{ saving() ? 'Saving…' : 'Save banner' }}</button>
          <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="form.set(null)">Cancel</button>
        </div>
      </form>
    }

    @if (loading()) {
      <div class="rt-skeleton mt-3" style="height:120px;"></div>
    } @else if (items().length === 0) {
      <div class="rt-panel center mt-3" style="padding:36px;"><h3 class="mt-0">No banners yet</h3></div>
    } @else {
      <div class="rt-stack mt-2">
        @for (b of items(); track b.id) {
          <div class="rt-panel row">
            <img [src]="media(b.imageUrl)" [alt]="b.title || 'Banner'" loading="lazy" />
            <div class="info">
              <strong>{{ b.title || '(untitled)' }}</strong>
              <span class="rt-tag" [class.rt-tag--teal]="b.isActive" [class.rt-tag--paper]="!b.isActive" style="margin-left:8px;">
                {{ b.isActive ? 'Active' : 'Hidden' }}
              </span>
              <div class="tiny muted">{{ label(b.placement) }} · order {{ b.sortOrder }}</div>
              <div class="tiny muted">{{ b.startAt || b.endAt ? when(b.startAt) + ' → ' + when(b.endAt) : 'No dates' }}</div>
            </div>
            <div class="rt-row" style="gap:6px;">
              <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(b)">Edit</button>
              <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(b)">Delete</button>
            </div>
          </div>
        }
      </div>
    }
  `,
  styles: [
    `
      .preview { width: 100%; max-height: 180px; object-fit: cover; border-radius: 10px; margin: 0 0 12px; }
      .row { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
      .row img { width: 220px; max-width: 100%; aspect-ratio: 1600 / 520; object-fit: cover; border-radius: 8px; }
      .info { flex: 1; min-width: 200px; }
    `
  ]
})
export class AdminBannersComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly placements = PLACEMENTS;
  readonly items = signal<AdminBanner[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<BannerForm | null>(null);
  readonly when = when;

  ngOnInit(): void {
    this.load();
  }

  media(path: string): string {
    return mediaUrl(path);
  }

  label(placement: string): string {
    return PLACEMENTS.find((p) => p.value === placement)?.label ?? placement;
  }

  load(): void {
    this.loading.set(true);
    this.api.get<AdminBanner[]>('/admin/banners').subscribe({
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
    this.form.set({ id: 0, placement: 'home.hero', title: '', imageUrl: '', link: '', sortOrder: next, startAt: '', endAt: '', isActive: true });
  }

  edit(b: AdminBanner): void {
    this.form.set({
      id: b.id,
      placement: b.placement,
      title: b.title ?? '',
      imageUrl: b.imageUrl,
      link: b.link ?? '',
      sortOrder: b.sortOrder,
      startAt: toLocalInput(b.startAt),
      endAt: toLocalInput(b.endAt),
      isActive: b.isActive
    });
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving()) {
      return;
    }

    const body = {
      placement: f.placement,
      title: f.title.trim() || null,
      imageUrl: f.imageUrl.trim(),
      link: f.link.trim() || null,
      sortOrder: Number(f.sortOrder) || 0,
      startAt: toIso(f.startAt),
      endAt: toIso(f.endAt),
      isActive: f.isActive
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/banners/${f.id}`, body) : this.api.post('/admin/banners', body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.form.set(null);
        this.toast.show('Banner saved.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(b: AdminBanner): void {
    if (typeof window !== 'undefined' && !window.confirm('Delete this banner?')) {
      return;
    }

    this.api.delete(`/admin/banners/${b.id}`).subscribe({
      next: () => {
        this.toast.show('Banner deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}
