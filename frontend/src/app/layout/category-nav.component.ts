import { Component, DestroyRef, ElementRef, HostListener, OnInit, effect, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { CategoryMenuService } from '../core/category-menu.service';
import { AuthService } from '../core/auth.service';
import { Category } from '../core/models';

/**
 * Desktop mega menu (DESIGN §3.3): parent categories on the left; the active one shows its
 * sub-categories, the brands that have products in it, and quick links. Opens on hover or click,
 * closes on Esc, outside click or navigation, and works from the keyboard.
 */
@Component({
  selector: 'rt-mega-menu',
  standalone: true,
  imports: [RouterLink],
  template: `
    <button
      class="trigger mono-label"
      type="button"
      aria-haspopup="true"
      [attr.aria-expanded]="open()"
      aria-controls="rt-mega-panel"
      (click)="toggle()"
      (mouseenter)="show()"
    >
      <span aria-hidden="true">☰</span> Categories <span aria-hidden="true" class="caret">▾</span>
    </button>

    @if (open()) {
      <div class="panel-wrap" (mouseleave)="scheduleClose()" (mouseenter)="cancelClose()">
        <div class="panel rt-container" id="rt-mega-panel" role="region" aria-label="Shop by category">
          <ul class="parents" role="list">
            @for (c of menu.categories(); track c.id) {
              <li>
                <a
                  class="parent"
                  [class.on]="active()?.id === c.id"
                  [routerLink]="['/category', c.slug]"
                  (mouseenter)="active.set(c)"
                  (focus)="active.set(c)"
                  (click)="close()"
                >
                  <span>{{ c.name }}</span>
                  <small class="muted">{{ menu.productCountFor(c.slug) || '' }}</small>
                </a>
              </li>
            }
          </ul>

          @if (active(); as cat) {
            <div class="detail">
              <div class="head">
                <h3 class="mb-0">{{ cat.name }}</h3>
                <a class="all" [routerLink]="['/category', cat.slug]" (click)="close()">See all {{ cat.name }} →</a>
              </div>

              <div class="cols">
                @if (cat.children.length) {
                  <div>
                    <h4 class="mono-label">Types</h4>
                    @for (child of cat.children; track child.id) {
                      <a class="link" [routerLink]="['/category', child.slug]" (click)="close()">{{ child.name }}</a>
                    }
                  </div>
                }

                <div>
                  <h4 class="mono-label">Shop by brand</h4>
                  @for (b of menu.brandsFor(cat.slug); track b.slug) {
                    <a class="link" [routerLink]="['/category', cat.slug]" [queryParams]="{ brands: b.slug }" (click)="close()">
                      {{ b.name }} <small class="muted">{{ b.productCount }}</small>
                    </a>
                  } @empty {
                    <span class="small muted">Brands appear once products are published.</span>
                  }
                </div>

                <div>
                  <h4 class="mono-label">Quick picks</h4>
                  <a class="link" [routerLink]="['/category', cat.slug]" [queryParams]="{ sort: 'discount' }" (click)="close()">Best deals</a>
                  <a class="link" [routerLink]="['/category', cat.slug]" [queryParams]="{ sort: 'newest' }" (click)="close()">New arrivals</a>
                  <a class="link" [routerLink]="['/category', cat.slug]" [queryParams]="{ sort: 'price-asc' }" (click)="close()">Lowest price first</a>
                  <a class="link" routerLink="/offer" (click)="close()">Running offers</a>
                </div>
              </div>
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: [
    `
      :host { display: inline-block; }
      .trigger {
        min-height: 44px;
        padding: 0 14px;
        border: 2px solid var(--ink);
        border-radius: 999px;
        background: var(--cream);
        box-shadow: 2px 2px 0 var(--ink);
        cursor: pointer;
        font-size: 11px;
        white-space: nowrap;
        color: var(--ink);
      }
      .trigger[aria-expanded='true'] { background: var(--mustard); }
      .caret { font-size: 10px; }
      /* Positioned against the sticky header so the panel spans its full width. */
      .panel-wrap { position: absolute; left: 0; right: 0; top: 100%; z-index: 60; padding-top: 0; }
      .panel {
        display: grid;
        grid-template-columns: 240px 1fr;
        gap: 0;
        background: var(--paper);
        border: 2px solid var(--ink);
        border-top: 0;
        border-radius: 0 0 16px 16px;
        box-shadow: 0 10px 0 rgba(32, 26, 19, 0.12);
        max-height: min(70vh, 460px);
        overflow: auto;
        padding: 0;
      }
      .parents { list-style: none; margin: 0; padding: 10px; border-right: 2px solid var(--ink); background: var(--cream); }
      .parent {
        display: flex; justify-content: space-between; align-items: center; gap: 8px;
        min-height: 44px; padding: 0 12px; border-radius: 10px;
        text-decoration: none; color: var(--ink); font-weight: 600;
      }
      .parent.on, .parent:hover, .parent:focus-visible { background: var(--mustard); outline: none; }
      .detail { padding: 18px 24px 22px; }
      .head { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; margin-bottom: 8px; }
      .all { font-weight: 700; color: var(--rust); text-decoration: none; }
      .all:hover { text-decoration: underline; }
      .cols { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 18px; }
      h4 { margin: 8px 0 6px; color: var(--ink-soft); }
      .link {
        display: flex; justify-content: space-between; gap: 8px; align-items: center;
        min-height: 36px; padding: 0 8px; margin: 0 -8px; border-radius: 8px;
        text-decoration: none; color: var(--ink);
      }
      .link:hover, .link:focus-visible { background: rgba(224, 73, 31, 0.1); color: var(--rust); outline: none; }
    `
  ]
})
export class MegaMenuComponent implements OnInit {
  readonly menu = inject(CategoryMenuService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private closeTimer: ReturnType<typeof setTimeout> | null = null;

  readonly open = signal(false);
  readonly active = signal<Category | null>(null);
  private pinned = false;

  constructor() {
    this.router.events
      .pipe(filter((event) => event instanceof NavigationStart), takeUntilDestroyed())
      .subscribe(() => this.close());
  }

  ngOnInit(): void {
    this.menu.load();
  }

  show(): void {
    this.cancelClose();
    this.open.set(true);
    this.active.update((current) => current ?? this.menu.categories()[0] ?? null);
  }

  /**
   * Hover opens the menu; a click then "pins" it so it stays open while the pointer travels to a link.
   * Clicking a pinned menu closes it. This stops hover-then-click from instantly closing what hover opened.
   */
  toggle(): void {
    if (!this.open()) {
      this.show();
      this.pinned = true;
    } else if (!this.pinned) {
      this.pinned = true;
    } else {
      this.close();
    }
  }

  close(): void {
    this.cancelClose();
    this.pinned = false;
    this.open.set(false);
  }

  scheduleClose(): void {
    this.cancelClose();
    if (this.pinned) {
      return;
    }

    this.closeTimer = setTimeout(() => this.close(), 220);
  }

  cancelClose(): void {
    if (this.closeTimer) {
      clearTimeout(this.closeTimer);
      this.closeTimer = null;
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) {
      this.close();
      (this.host.nativeElement.querySelector('.trigger') as HTMLElement | null)?.focus();
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node | null;
    const header = this.host.nativeElement.closest('.rt-header');
    if (this.open() && target && !(header ?? this.host.nativeElement).contains(target)) {
      this.close();
    }
  }
}

/**
 * Mobile category drawer (DESIGN §3.3): a bottom sheet with an accordion per category, brand chips
 * and the links the condensed header no longer carries. Opened from the bottom nav's Category item.
 */
@Component({
  selector: 'rt-category-drawer',
  standalone: true,
  imports: [RouterLink],
  template: `
    @if (menu.drawerOpen()) {
      <div class="backdrop" (click)="menu.closeDrawer()" aria-hidden="true"></div>
      <div class="sheet" role="dialog" aria-modal="true" aria-label="Browse categories">
        <div class="grab" aria-hidden="true"></div>
        <div class="bar">
          <h3 class="mb-0">Browse</h3>
          <button #closeBtn class="rt-icon-btn small-btn" type="button" (click)="menu.closeDrawer()" aria-label="Close categories">✕</button>
        </div>

        <div class="quick">
          <a routerLink="/shop" (click)="menu.closeDrawer()">All products</a>
          <a routerLink="/brands" (click)="menu.closeDrawer()">Brands</a>
          <a routerLink="/compare" (click)="menu.closeDrawer()">Compare</a>
          <a routerLink="/track" (click)="menu.closeDrawer()">Track order</a>
          @if (auth.isSignedIn()) {
            <a routerLink="/wishlist" (click)="menu.closeDrawer()">Wishlist</a>
          }
        </div>

        <ul class="acc" role="list">
          @for (c of menu.categories(); track c.id) {
            <li>
              <button
                class="acc-head"
                type="button"
                [attr.aria-expanded]="expanded() === c.id"
                [attr.aria-controls]="'cat-' + c.id"
                (click)="expanded.set(expanded() === c.id ? null : c.id)"
              >
                <span>{{ c.name }}</span>
                <span aria-hidden="true">{{ expanded() === c.id ? '−' : '+' }}</span>
              </button>

              @if (expanded() === c.id) {
                <div class="acc-body" [id]="'cat-' + c.id">
                  <a class="all" [routerLink]="['/category', c.slug]" (click)="menu.closeDrawer()">See all {{ c.name }} →</a>

                  @for (child of c.children; track child.id) {
                    <a class="row" [routerLink]="['/category', child.slug]" (click)="menu.closeDrawer()">{{ child.name }}</a>
                  }

                  @if (menu.brandsFor(c.slug).length) {
                    <div class="chips" role="group" [attr.aria-label]="c.name + ' brands'">
                      @for (b of menu.brandsFor(c.slug); track b.slug) {
                        <a class="chip" [routerLink]="['/category', c.slug]" [queryParams]="{ brands: b.slug }" (click)="menu.closeDrawer()">{{ b.name }}</a>
                      }
                    </div>
                  }
                </div>
              }
            </li>
          } @empty {
            <li class="small muted" style="padding:14px;">Loading categories…</li>
          }
        </ul>
      </div>
    }
  `,
  styles: [
    `
      .backdrop { position: fixed; inset: 0; z-index: 70; background: rgba(32, 26, 19, 0.5); }
      .sheet {
        position: fixed; left: 0; right: 0; bottom: 0; z-index: 71;
        max-height: 82vh; overflow: auto;
        background: var(--paper); border: 2px solid var(--ink); border-bottom: 0;
        border-radius: 20px 20px 0 0; padding: 8px 16px calc(16px + env(safe-area-inset-bottom));
        animation: rise 0.22s ease-out;
      }
      @keyframes rise { from { transform: translateY(40px); opacity: 0; } to { transform: none; opacity: 1; } }
      .grab { width: 44px; height: 5px; border-radius: 3px; background: var(--ink); opacity: 0.25; margin: 4px auto 10px; }
      .bar { display: flex; align-items: center; justify-content: space-between; }
      .small-btn { width: 44px; height: 44px; font-size: 14px; }
      .quick { display: flex; flex-wrap: wrap; gap: 8px; margin: 10px 0 12px; }
      .quick a, .chip {
        min-height: 44px; display: inline-flex; align-items: center; padding: 0 14px;
        border: 2px solid var(--ink); border-radius: 999px; background: var(--cream);
        text-decoration: none; color: var(--ink); font-weight: 600; font-size: 14px;
      }
      .acc { list-style: none; margin: 0; padding: 0; border-top: 2px solid var(--ink); }
      .acc li { border-bottom: 1px solid rgba(32, 26, 19, 0.2); }
      .acc-head {
        width: 100%; min-height: 52px; display: flex; justify-content: space-between; align-items: center;
        background: none; border: 0; font: inherit; font-weight: 700; font-size: 16px; color: var(--ink); cursor: pointer; padding: 0 4px;
      }
      .acc-body { display: grid; gap: 4px; padding: 0 4px 14px; }
      .all { min-height: 44px; display: flex; align-items: center; font-weight: 700; color: var(--rust); text-decoration: none; }
      .row { min-height: 44px; display: flex; align-items: center; text-decoration: none; color: var(--ink); }
      .chips { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 6px; }
    `
  ]
})
export class CategoryDrawerComponent {
  readonly menu = inject(CategoryMenuService);
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly expanded = signal<number | null>(null);
  private readonly closeBtn = viewChild<ElementRef<HTMLButtonElement>>('closeBtn');

  constructor() {
    this.router.events
      .pipe(filter((event) => event instanceof NavigationStart), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.menu.closeDrawer());

    // Move focus into the sheet when it opens and stop the page behind it from scrolling.
    effect(() => {
      const open = this.menu.drawerOpen();
      if (typeof document === 'undefined') {
        return;
      }

      document.body.style.overflow = open ? 'hidden' : '';
      if (open) {
        queueMicrotask(() => this.closeBtn()?.nativeElement.focus());
      }
    });
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.menu.closeDrawer();
  }
}
