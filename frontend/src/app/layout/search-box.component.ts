import { Component, ElementRef, HostListener, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subject, catchError, debounceTime, distinctUntilChanged, of, switchMap, takeUntil, tap } from 'rxjs';
import { ApiService, mediaUrl } from '../core/api.service';
import { ProductCard } from '../core/models';
import { BdtPipe, CleanNamePipe } from '../core/ui';

interface Suggestion {
  type: 'product' | 'category' | 'brand';
  label: string;
  subLabel: string | null;
  url: string;
  thumbUrl: string | null;
  price: number | null;
}

interface SuggestResult {
  query: string;
  suggestions: Suggestion[];
}

const RECENT_KEY = 'rt_recent_searches';
const RECENT_MAX = 6;

/**
 * Header search: live suggestions (products, categories, brands) as you type, and — before you type —
 * recent searches (this browser only) and trending products. Fully keyboard-operable (↑ ↓ Enter Esc)
 * and announced as an ARIA combobox.
 */
@Component({
  selector: 'rt-search-box',
  standalone: true,
  imports: [FormsModule, RouterLink, BdtPipe, CleanNamePipe],
  template: `
    <form class="rt-search header-search" role="search" (ngSubmit)="submit()" autocomplete="off">
      <span aria-hidden="true">⌕</span>
      <input
        #input
        name="q"
        type="search"
        role="combobox"
        aria-autocomplete="list"
        aria-label="Search products"
        placeholder="Search phones, laptops, accessories…"
        aria-controls="search-listbox"
        [attr.aria-expanded]="open()"
        [attr.aria-activedescendant]="active() >= 0 ? 'search-opt-' + active() : null"
        [ngModel]="term()"
        (ngModelChange)="onInput($event)"
        (focus)="onFocus()"
        (keydown)="onKey($event)"
      />
      <button class="rt-btn rt-btn--sm rt-btn--teal" type="submit">Find</button>
    </form>

    @if (open() && hasPanel()) {
      <div class="panel rt-panel" id="search-listbox" role="listbox" aria-label="Search suggestions">
        @if (mode() === 'suggest') {
          @for (item of suggestions(); track item.url; let i = $index) {
            <a
              class="row"
              role="option"
              [id]="'search-opt-' + i"
              [class.is-active]="active() === i"
              [attr.aria-selected]="active() === i"
              [routerLink]="item.url"
              (click)="choose(item)"
              (mouseenter)="active.set(i)"
            >
              @if (item.thumbUrl) {
                <img class="thumb" [src]="media(item.thumbUrl)" alt="" loading="lazy" />
              } @else {
                <span class="thumb kind" aria-hidden="true">{{ item.type === 'category' ? '▦' : '★' }}</span>
              }
              <span class="text">
                <span class="label">{{ item.label | cleanName }}</span>
                <span class="sub">{{ item.subLabel }}</span>
              </span>
              @if (item.price !== null) {
                <span class="price">{{ item.price | bdt }}</span>
              }
            </a>
          }

          <a
            class="row all"
            role="option"
            [id]="'search-opt-' + suggestions().length"
            [class.is-active]="active() === suggestions().length"
            [attr.aria-selected]="active() === suggestions().length"
            [routerLink]="['/search']"
            [queryParams]="{ q: term().trim() }"
            (click)="remember(term())"
            (mouseenter)="active.set(suggestions().length)"
          >
            See all results for “{{ term().trim() }}” →
          </a>
        } @else if (mode() === 'empty') {
          <p class="none">No matches for “{{ term().trim() }}”. Try a brand or model name.</p>
        } @else {
          @if (recent().length > 0) {
            <div class="head">
              <span class="mono-label">Recent searches</span>
              <button type="button" class="linkish" (click)="clearRecent()">Clear</button>
            </div>
            <div class="chips">
              @for (item of recent(); track item) {
                <button type="button" class="rt-chip" (click)="searchFor(item)">{{ item }}</button>
              }
            </div>
          }

          @if (trending().length > 0) {
            <div class="head"><span class="mono-label">Trending</span></div>
            @for (product of trending(); track product.id) {
              <a class="row" [routerLink]="['/product', product.slug]" (click)="close()">
                @if (product.thumbUrl) {
                  <img class="thumb" [src]="media(product.thumbUrl)" alt="" loading="lazy" />
                }
                <span class="text">
                  <span class="label">{{ product.name | cleanName }}</span>
                  <span class="sub">{{ product.brandName }}</span>
                </span>
                <span class="price">{{ product.effectivePrice | bdt }}</span>
              </a>
            }
          }
        }
      </div>
    }
  `,
  styles: [
    `
      :host { position: relative; flex: 1; min-width: 0; display: block; }
      :host .header-search { width: 100%; }

      .panel {
        position: absolute;
        left: 0;
        right: 0;
        top: calc(100% + 8px);
        z-index: 60;
        max-height: min(70vh, 520px);
        overflow-y: auto;
        padding: 8px;
      }

      .head { display: flex; justify-content: space-between; align-items: center; padding: 8px 8px 4px; }
      .chips { display: flex; flex-wrap: wrap; gap: 6px; padding: 4px 8px 8px; }

      .row {
        display: flex;
        align-items: center;
        gap: 10px;
        min-height: 52px;
        padding: 6px 8px;
        border-radius: 10px;
        color: inherit;
        text-decoration: none;
      }

      .row.is-active,
      .row:hover { background: rgba(212, 169, 122, 0.28); }

      .row.all { justify-content: center; font-weight: 700; border-top: 1px dashed rgba(0, 0, 0, 0.25); border-radius: 0 0 10px 10px; margin-top: 4px; }

      .thumb { width: 40px; height: 40px; border-radius: 8px; object-fit: contain; background: #fff; flex: none; }
      .thumb.kind { display: grid; place-items: center; background: rgba(0, 0, 0, 0.06); }

      .text { display: grid; flex: 1; min-width: 0; }
      .label { font-weight: 600; font-size: 14px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
      .sub { font-size: 12px; opacity: 0.7; }
      .price { font-weight: 700; font-size: 13px; white-space: nowrap; }

      .none { margin: 0; padding: 14px 10px; }

      .linkish { background: none; border: 0; padding: 0; min-height: 32px; text-decoration: underline; cursor: pointer; font: inherit; font-size: 12px; }
    `
  ]
})
export class SearchBoxComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroy$ = new Subject<void>();
  private readonly query$ = new Subject<string>();

  readonly term = signal('');
  readonly open = signal(false);
  readonly active = signal(-1);
  readonly suggestions = signal<Suggestion[]>([]);
  readonly searched = signal(false);
  readonly recent = signal<string[]>([]);
  readonly trending = signal<ProductCard[]>([]);

  private trendingLoaded = false;

  readonly mode = computed<'idle' | 'suggest' | 'empty'>(() => {
    if (this.term().trim().length < 2) {
      return 'idle';
    }

    if (this.suggestions().length > 0) {
      return 'suggest';
    }

    return this.searched() ? 'empty' : 'idle';
  });

  readonly hasPanel = computed(
    () => this.mode() !== 'idle' || this.recent().length > 0 || this.trending().length > 0
  );

  ngOnInit(): void {
    this.recent.set(this.readRecent());

    this.query$
      .pipe(
        debounceTime(220),
        distinctUntilChanged(),
        tap((term) => {
          if (term.length < 2) {
            this.suggestions.set([]);
            this.searched.set(false);
          }
        }),
        switchMap((term) =>
          term.length < 2
            ? of(null)
            : this.api.get<SuggestResult>('/search/suggest', { q: term, limit: 8 }).pipe(catchError(() => of(null)))
        ),
        takeUntil(this.destroy$)
      )
      .subscribe((result) => {
        if (result) {
          this.suggestions.set(result.suggestions ?? []);
          this.searched.set(true);
          this.active.set(-1);
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  media(path: string | null): string {
    return mediaUrl(path);
  }

  onFocus(): void {
    this.open.set(true);

    if (!this.trendingLoaded) {
      this.trendingLoaded = true;
      this.api.get<ProductCard[]>('/search/trending').subscribe({
        next: (items) => this.trending.set(items ?? []),
        error: () => undefined
      });
    }
  }

  onInput(value: string): void {
    this.term.set(value ?? '');
    this.open.set(true);
    this.active.set(-1);
    this.query$.next((value ?? '').trim());
  }

  onKey(event: KeyboardEvent): void {
    const count = this.mode() === 'suggest' ? this.suggestions().length + 1 : 0;

    if (event.key === 'Escape') {
      this.close();
    } else if (event.key === 'ArrowDown' && count > 0) {
      event.preventDefault();
      this.active.set((this.active() + 1) % count);
    } else if (event.key === 'ArrowUp' && count > 0) {
      event.preventDefault();
      this.active.set(this.active() <= 0 ? count - 1 : this.active() - 1);
    } else if (event.key === 'Enter' && this.mode() === 'suggest') {
      const index = this.active();
      const item = this.suggestions()[index];

      if (item) {
        event.preventDefault();
        this.choose(item);
        this.router.navigateByUrl(item.url);
      }
    }
  }

  submit(): void {
    const term = this.term().trim();
    if (!term) {
      return;
    }

    this.remember(term);
    this.close();
    this.router.navigate(['/search'], { queryParams: { q: term } });
  }

  searchFor(term: string): void {
    this.term.set(term);
    this.submit();
  }

  choose(item: Suggestion): void {
    this.remember(this.term());
    this.term.set(item.type === 'product' ? item.label : this.term());
    this.close();
  }

  remember(term: string): void {
    const value = term.trim();
    if (value.length < 2) {
      return;
    }

    const next = [value, ...this.recent().filter((item) => item.toLowerCase() !== value.toLowerCase())].slice(0, RECENT_MAX);
    this.recent.set(next);
    this.writeRecent(next);
    this.close();
  }

  clearRecent(): void {
    this.recent.set([]);
    this.writeRecent([]);
  }

  close(): void {
    this.open.set(false);
    this.active.set(-1);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    if (!this.host.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }

  private readRecent(): string[] {
    try {
      if (typeof localStorage === 'undefined') {
        return [];
      }

      const parsed: unknown = JSON.parse(localStorage.getItem(RECENT_KEY) ?? '[]');
      return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === 'string').slice(0, RECENT_MAX) : [];
    } catch {
      return [];
    }
  }

  private writeRecent(items: string[]): void {
    try {
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(RECENT_KEY, JSON.stringify(items));
      }
    } catch {
      // Storage can be blocked; recent searches simply are not remembered.
    }
  }
}
