import { Injectable, computed, inject, signal } from '@angular/core';
import { ToastService } from './toast.service';

const STORAGE_KEY = 'rt_compare';

/** Separator used in shareable compare URLs: /product-compare/{a}-vs-{b} (CONVENTIONS §5). */
export const COMPARE_SEPARATOR = '-vs-';

/**
 * Holds the products a shopper picked for side-by-side comparison. Kept in the browser only;
 * the server validates category match and the 4-product limit when the page loads (FR-14).
 */
@Injectable({ providedIn: 'root' })
export class CompareService {
  static readonly MAX = 4;

  private readonly toast = inject(ToastService);

  readonly slugs = signal<string[]>(this.read());
  readonly count = computed(() => this.slugs().length);
  readonly url = computed(() => this.urlFor(this.slugs()));

  has(slug: string): boolean {
    return this.slugs().includes(slug);
  }

  toggle(slug: string): void {
    if (this.has(slug)) {
      this.remove(slug);
      return;
    }

    if (this.slugs().length >= CompareService.MAX) {
      this.toast.show(`You can compare up to ${CompareService.MAX} products.`, 'error');
      return;
    }

    this.set([...this.slugs(), slug]);
    this.toast.show('Added to compare.');
  }

  remove(slug: string): void {
    this.set(this.slugs().filter((item) => item !== slug));
  }

  clear(): void {
    this.set([]);
  }

  /** Replaces the list, e.g. when a shared compare link is opened. */
  replace(slugs: string[]): void {
    this.set(slugs.slice(0, CompareService.MAX));
  }

  urlFor(slugs: readonly string[]): string {
    return `/product-compare/${slugs.join(COMPARE_SEPARATOR)}`;
  }

  private set(slugs: string[]): void {
    this.slugs.set(slugs);

    try {
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(slugs));
      }
    } catch {
      // Storage can be blocked (private mode); compare still works for this page view.
    }
  }

  private read(): string[] {
    try {
      if (typeof localStorage === 'undefined') {
        return [];
      }

      const raw = localStorage.getItem(STORAGE_KEY);
      const parsed: unknown = raw ? JSON.parse(raw) : [];
      return Array.isArray(parsed)
        ? parsed.filter((item): item is string => typeof item === 'string').slice(0, CompareService.MAX)
        : [];
    } catch {
      return [];
    }
  }
}
