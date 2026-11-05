import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeError } from '../core/api.service';
import { AdminBlog } from '../core/models';
import { ToastService } from '../core/toast.service';
import { toIso, toLocalInput, when } from './admin-utils';

interface BlogForm {
  id: number;
  title: string;
  slug: string;
  excerpt: string;
  cover: string;
  body: string;
  isPublished: boolean;
  publishedAt: string;
  seoTitle: string;
  seoDesc: string;
}

const blank = (): BlogForm => ({
  id: 0, title: '', slug: '', excerpt: '', cover: '', body: '', isPublished: false, publishedAt: '', seoTitle: '', seoDesc: ''
});

/** Write and publish blog posts. The body is plain text: blank line between paragraphs, "## " for a heading. */
@Component({
  selector: 'rt-admin-blogs',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="rt-row rt-row--between rt-row--wrap">
      <div>
        <div class="kicker mono-label">Content</div>
        <h1 class="mb-0">Blog posts</h1>
        <p class="small muted mb-0">Published posts appear on the Blog page and the home page (latest three).</p>
      </div>
      <button class="rt-btn rt-btn--sm" type="button" (click)="startNew()">+ New post</button>
    </div>

    @if (form(); as f) {
      <form class="rt-panel mt-2" (ngSubmit)="save()">
        <h3 class="mt-0">{{ f.id ? 'Edit post' : 'New post' }}</h3>

        <div class="rt-field">
          <label class="rt-label" for="b-title">Title</label>
          <input id="b-title" name="title" class="rt-input" [(ngModel)]="f.title" required maxlength="200" />
        </div>

        <div class="rt-grid rt-cols-2" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="b-slug">URL slug {{ f.id ? '(leave as is to keep the link)' : '(optional)' }}</label>
            <input id="b-slug" name="slug" class="rt-input" [(ngModel)]="f.slug" maxlength="220" pattern="[a-z0-9]+(-[a-z0-9]+)*" placeholder="made from the title" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="b-cover">Cover image URL (optional)</label>
            <input id="b-cover" name="cover" class="rt-input" [(ngModel)]="f.cover" maxlength="512" placeholder="/uploads/… or https://…" />
          </div>
        </div>

        <div class="rt-field">
          <label class="rt-label" for="b-excerpt">Short summary (shown on cards)</label>
          <input id="b-excerpt" name="excerpt" class="rt-input" [(ngModel)]="f.excerpt" maxlength="300" />
        </div>

        <div class="rt-field">
          <label class="rt-label" for="b-body">Post text</label>
          <textarea id="b-body" name="body" class="rt-textarea" rows="14" [(ngModel)]="f.body" required maxlength="50000"
            placeholder="First paragraph…&#10;&#10;## A heading&#10;&#10;Next paragraph…"></textarea>
        </div>

        <div class="rt-grid rt-cols-2" style="gap:12px;">
          <div class="rt-field">
            <label class="rt-label" for="b-st">Search title (optional, 70 max)</label>
            <input id="b-st" name="seoTitle" class="rt-input" [(ngModel)]="f.seoTitle" maxlength="70" />
          </div>
          <div class="rt-field">
            <label class="rt-label" for="b-sd">Search description (optional, 160 max)</label>
            <input id="b-sd" name="seoDesc" class="rt-input" [(ngModel)]="f.seoDesc" maxlength="160" />
          </div>
        </div>

        <div class="rt-row rt-row--wrap" style="gap:20px; align-items:flex-end; margin-bottom:14px;">
          <label class="rt-row" style="gap:8px; cursor:pointer; min-height:44px;">
            <input type="checkbox" name="pub" [(ngModel)]="f.isPublished" /> Published
          </label>
          <div class="rt-field" style="margin:0;">
            <label class="rt-label" for="b-at">Publish date (optional)</label>
            <input id="b-at" name="publishedAt" type="datetime-local" class="rt-input" [(ngModel)]="f.publishedAt" />
          </div>
        </div>

        <div class="rt-row" style="gap:8px;">
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="saving() || !f.title.trim() || !f.body.trim()">
            {{ saving() ? 'Saving…' : 'Save post' }}
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
      <div class="rt-panel center mt-3" style="padding:36px;"><h3 class="mt-0">No posts yet</h3></div>
    } @else {
      <div class="rt-panel mt-2" style="padding:0; overflow:hidden;">
        <table class="rt-table rt-table--scroll">
          <thead><tr><th>Title</th><th>Published</th><th>State</th><th class="right">Actions</th></tr></thead>
          <tbody>
            @for (post of items(); track post.id) {
              <tr>
                <td><strong>{{ post.title }}</strong><div class="tiny muted">/blogs/{{ post.slug }}</div></td>
                <td class="small">{{ when(post.publishedAt) }}</td>
                <td>
                  <span class="rt-tag" [class.rt-tag--teal]="post.isPublished" [class.rt-tag--paper]="!post.isPublished">
                    {{ post.isPublished ? 'Published' : 'Draft' }}
                  </span>
                </td>
                <td class="right">
                  <div class="rt-row" style="justify-content:flex-end; gap:6px;">
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="edit(post)">Edit</button>
                    <button class="rt-btn rt-btn--ghost rt-btn--sm" type="button" (click)="remove(post)">Delete</button>
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
export class AdminBlogsComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly items = signal<AdminBlog[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly form = signal<BlogForm | null>(null);
  readonly when = when;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<AdminBlog[]>('/admin/blogs').subscribe({
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
    this.form.set(blank());
  }

  edit(post: AdminBlog): void {
    this.form.set({
      id: post.id,
      title: post.title,
      slug: post.slug,
      excerpt: post.excerpt ?? '',
      cover: post.cover ?? '',
      body: post.body,
      isPublished: post.isPublished,
      publishedAt: toLocalInput(post.publishedAt),
      seoTitle: post.seoTitle ?? '',
      seoDesc: post.seoDesc ?? ''
    });
  }

  save(): void {
    const f = this.form();
    if (!f || this.saving()) {
      return;
    }

    const body = {
      title: f.title.trim(),
      slug: f.slug.trim() || null,
      excerpt: f.excerpt.trim() || null,
      cover: f.cover.trim() || null,
      body: f.body,
      isPublished: f.isPublished,
      publishedAt: toIso(f.publishedAt),
      seoTitle: f.seoTitle.trim() || null,
      seoDesc: f.seoDesc.trim() || null
    };

    this.saving.set(true);
    const request = f.id ? this.api.put(`/admin/blogs/${f.id}`, body) : this.api.post('/admin/blogs', body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.form.set(null);
        this.toast.show('Post saved.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  remove(post: AdminBlog): void {
    if (typeof window !== 'undefined' && !window.confirm(`Delete “${post.title}”?`)) {
      return;
    }

    this.api.delete(`/admin/blogs/${post.id}`).subscribe({
      next: () => {
        this.toast.show('Post deleted.');
        this.load();
      },
      error: (error: unknown) => this.toast.show(describeError(error), 'error')
    });
  }
}
