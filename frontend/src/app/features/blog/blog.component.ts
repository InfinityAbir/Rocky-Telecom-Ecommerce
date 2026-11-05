import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService, mediaUrl } from '../../core/api.service';
import { BlogDetail, BlogSummary, Paged } from '../../core/models';
import { SeoService } from '../../core/seo.service';

/** A line of post text: a heading ("## …") or a paragraph. The body is plain text, never HTML. */
interface Block {
  heading: boolean;
  text: string;
}

function toBlocks(body: string): Block[] {
  return body
    .split(/\n{2,}/)
    .map((chunk) => chunk.trim())
    .filter(Boolean)
    .map((chunk) => (chunk.startsWith('## ') ? { heading: true, text: chunk.slice(3).trim() } : { heading: false, text: chunk }));
}

/** /blogs — latest posts. */
@Component({
  selector: 'rt-blog-list',
  standalone: true,
  imports: [RouterLink, DatePipe],
  template: `
    <div class="rt-container" style="padding-top:24px;">
      <div class="kicker mono-label">Blog</div>
      <h1 style="margin-bottom:8px;">Guides &amp; news</h1>
      <p>Buying tips, how-tos and news from the Rocky Telecom showroom.</p>

      @if (loading()) {
        <div class="rt-grid rt-cols-3">
          @for (n of [1, 2, 3]; track n) {
            <div class="rt-skeleton" style="height:260px;"></div>
          }
        </div>
      } @else if (posts().length === 0) {
        <div class="rt-panel center" style="padding:40px 20px;">
          <h3 class="mt-0">No posts yet</h3>
          <p>Check back soon.</p>
        </div>
      } @else {
        <div class="rt-grid rt-cols-3">
          @for (post of posts(); track post.id) {
            <a class="rt-card post-card" [routerLink]="['/blogs', post.slug]">
              @if (post.cover) {
                <img [src]="media(post.cover)" [alt]="post.title" loading="lazy" />
              }
              <div class="body">
                <span class="mono-label">{{ post.publishedAt | date: 'd MMM y' }}</span>
                <strong>{{ post.title }}</strong>
                @if (post.excerpt) {
                  <span class="small">{{ post.excerpt }}</span>
                }
              </div>
            </a>
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      .post-card { display: grid; text-decoration: none; color: inherit; overflow: hidden; padding: 0; }
      .post-card img { width: 100%; aspect-ratio: 1600 / 520; object-fit: cover; border-bottom: 2px solid var(--ink, #101828); }
      .post-card .body { display: grid; gap: 6px; padding: 14px 16px 18px; }
    `
  ]
})
export class BlogListComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly seo = inject(SeoService);

  readonly posts = signal<BlogSummary[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.seo.set({ title: 'Guides & news', description: 'Buying tips, how-tos and news from the Rocky Telecom showroom in Gaibandha.', path: '/blogs' });

    this.api.get<Paged<BlogSummary>>('/blogs', { pageSize: 12 }).subscribe({
      next: (page) => {
        this.posts.set(page.items ?? []);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }
}

/** /blogs/{slug} — one post. */
@Component({
  selector: 'rt-blog-post',
  standalone: true,
  imports: [RouterLink, DatePipe],
  template: `
    <article class="rt-container post" style="padding-top:24px;">
      <nav class="small" aria-label="Breadcrumb"><a routerLink="/">Home</a> / <a routerLink="/blogs">Blog</a></nav>

      @if (post(); as p) {
        <h1 style="margin:14px 0 6px;">{{ p.title }}</h1>
        <p class="mono-label">{{ p.publishedAt | date: 'd MMMM y' }}</p>

        @if (p.cover) {
          <img class="cover" [src]="media(p.cover)" [alt]="p.title" />
        }

        @for (block of blocks(); track $index) {
          @if (block.heading) {
            <h2 style="font-size:24px; margin-top:28px;">{{ block.text }}</h2>
          } @else {
            <p>{{ block.text }}</p>
          }
        }

        <div class="rt-dashed" style="margin:30px 0;"></div>
        <p class="small"><a routerLink="/blogs">← More guides</a> · <a routerLink="/shop">Browse the shop</a></p>
      } @else if (loading()) {
        <div class="rt-skeleton" style="height:300px; margin-top:16px;"></div>
      } @else {
        <div class="rt-panel center" style="padding:40px 20px; margin-top:16px;">
          <h3 class="mt-0">We couldn't find that post</h3>
          <a class="rt-btn rt-btn--ghost" routerLink="/blogs">See all posts</a>
        </div>
      }
    </article>
  `,
  styles: [
    `
      .post { max-width: 760px; }
      .cover { width: 100%; border-radius: 12px; margin: 14px 0 6px; }
      p { line-height: 1.7; }
    `
  ]
})
export class BlogPostComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly seo = inject(SeoService);
  private readonly destroy$ = new Subject<void>();

  readonly post = signal<BlogDetail | null>(null);
  readonly blocks = signal<Block[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((params) => this.load(params.get('slug') ?? ''));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }

  private load(slug: string): void {
    this.loading.set(true);
    this.post.set(null);

    this.api.get<BlogDetail>(`/blogs/${encodeURIComponent(slug)}`).subscribe({
      next: (post) => {
        this.post.set(post);
        this.blocks.set(toBlocks(post.body));
        this.loading.set(false);

        const origin = this.seo.origin();
        this.seo.set({
          title: post.seoTitle || post.title,
          description: post.seoDesc || post.excerpt,
          path: `/blogs/${post.slug}`,
          image: post.cover ? mediaUrl(post.cover) : null,
          type: 'article',
          jsonLd: {
            '@context': 'https://schema.org',
            '@type': 'BlogPosting',
            headline: post.title,
            description: post.excerpt ?? undefined,
            image: post.cover ? [mediaUrl(post.cover)] : undefined,
            datePublished: post.publishedAt ?? undefined,
            mainEntityOfPage: `${origin}/blogs/${post.slug}`,
            publisher: { '@type': 'Organization', name: 'Rocky Telecom' }
          }
        });
      },
      error: () => {
        this.loading.set(false);
        this.seo.set({ title: 'Post not found', noindex: true });
      }
    });
  }
}
