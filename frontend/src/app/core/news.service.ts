import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { NewsItem } from './models';

/** Shown until the API answers, and whenever the shop has no active ticker lines. */
const FALLBACK: NewsItem[] = [
  'Official warranty on every handset',
  'EMI from ৳2,500/month',
  'Free delivery inside Gaibandha',
  'Showroom pickup in 2 hours',
  'Hotline 01700-000000'
].map((title, index) => ({ id: -(index + 1), title, linkUrl: null, openInNewTab: false }));

/** Announcement ticker lines, loaded once per visit and shared by the top strip. */
@Injectable({ providedIn: 'root' })
export class NewsService {
  private readonly api = inject(ApiService);
  private readonly loaded = signal<NewsItem[] | null>(null);

  readonly items = computed(() => {
    const items = this.loaded();
    return items && items.length > 0 ? items : FALLBACK;
  });

  private started = false;

  load(): void {
    if (this.started) {
      return;
    }

    this.started = true;

    this.api.get<NewsItem[]>('/news').subscribe({
      next: (items) => this.loaded.set(items ?? []),
      error: () => this.loaded.set([])
    });
  }

  isExternal(item: NewsItem): boolean {
    return !!item.linkUrl && /^https?:\/\//i.test(item.linkUrl);
  }
}
