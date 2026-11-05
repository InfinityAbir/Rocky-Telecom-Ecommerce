import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { CartService } from '../core/cart.service';
import { CategoryMenuService } from '../core/category-menu.service';
import { CompareService } from '../core/compare.service';
import { NewsService } from '../core/news.service';
import { ToastService } from '../core/toast.service';
import { WishlistService } from '../core/wishlist.service';
import { MegaMenuComponent } from './category-nav.component';
import { NewsletterFormComponent } from './newsletter-form.component';
import { SearchBoxComponent } from './search-box.component';

@Component({
  selector: 'rt-top-strip',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="rt-strip" aria-label="Store announcements">
      <div class="rt-marquee">
        <!-- The list is rendered twice so the marquee loops without a gap; the copy is hidden from assistive tech. -->
        @for (copy of [0, 1]; track copy) {
          @for (item of news.items(); track item.id) {
            <span [attr.aria-hidden]="copy === 1 ? 'true' : null">
              @if (item.linkUrl && news.isExternal(item)) {
                <a
                  [href]="item.linkUrl"
                  [attr.target]="item.openInNewTab ? '_blank' : null"
                  [attr.rel]="item.openInNewTab ? 'noopener noreferrer' : null"
                  [attr.tabindex]="copy === 1 ? -1 : null"
                  >{{ item.title }}</a
                >
              } @else if (item.linkUrl) {
                <a [routerLink]="item.linkUrl" [attr.tabindex]="copy === 1 ? -1 : null">{{ item.title }}</a>
              } @else {
                {{ item.title }}
              }
              <b class="dot">✳</b>
            </span>
          }
        }
      </div>
    </div>
  `,
  styles: [
    `
      a { color: inherit; text-decoration: underline; text-underline-offset: 3px; }
    `
  ]
})
export class TopStripComponent implements OnInit {
  readonly news = inject(NewsService);

  ngOnInit(): void {
    this.news.load();
  }
}

@Component({
  selector: 'rt-header',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, SearchBoxComponent, MegaMenuComponent],
  template: `
    <header class="rt-header">
      <div class="rt-container header-row" style="display:flex; align-items:center; gap:14px; padding-top:12px; padding-bottom:12px;">
        <a class="rt-logo" routerLink="/">
          <span class="mark">RT</span>
          <span>
            Rocky Telecom
            <span class="mono-label" style="display:block; font-size:9px; letter-spacing:0.2em;">SADULLAPUR · GAIBANDHA</span>
          </span>
        </a>

        <rt-mega-menu class="mega-trigger" />

        <rt-search-box />

        <nav class="rt-row header-links" aria-label="Primary" style="gap:18px; margin-left:auto;">
          <a class="mono-label" routerLink="/shop" routerLinkActive="active">Shop</a>
          <a class="mono-label" routerLink="/offer" routerLinkActive="active">Offers</a>
          <a class="mono-label hide-md" routerLink="/pre-order" routerLinkActive="active">Pre-order</a>
          <a class="mono-label hide-md" routerLink="/brands" routerLinkActive="active">Brands</a>
          <a class="mono-label" [class.hide-md]="compare.count() === 0" routerLink="/compare" routerLinkActive="active">
            Compare{{ compare.count() > 0 ? ' (' + compare.count() + ')' : '' }}
          </a>
          <a class="mono-label" routerLink="/track" routerLinkActive="active">Track</a>
          @if (auth.isSignedIn()) {
            <a class="mono-label hide-md" routerLink="/wishlist" routerLinkActive="active">
              Wishlist{{ wishlist.count() > 0 ? ' (' + wishlist.count() + ')' : '' }}
            </a>
            <a class="mono-label" routerLink="/account/orders" routerLinkActive="active">Orders</a>
            @if (auth.isAdmin()) {
              <a class="mono-label" routerLink="/admin" routerLinkActive="active">Admin</a>
            }
          }
        </nav>

        <a class="rt-icon-btn" routerLink="/cart" aria-label="Cart">
          <span aria-hidden="true">🛒</span>
          @if (cart.count() > 0) {
            <span class="rt-count">{{ cart.count() }}</span>
          }
        </a>

        @if (auth.isSignedIn()) {
          <button class="rt-icon-btn" type="button" (click)="signOut()" aria-label="Sign out" title="Sign out">
            <span aria-hidden="true">⏻</span>
          </button>
        } @else {
          <a class="rt-icon-btn" routerLink="/login" aria-label="Sign in" title="Sign in">
            <span aria-hidden="true">☺</span>
          </a>
        }
      </div>
    </header>
  `,
  styles: [
    `
      .header-links .active { color: var(--rust); }
      /* Secondary links step aside on medium screens so the search box keeps its width. */
      @media (max-width: 1100px) {
        .header-links .hide-md { display: none; }
      }
      /* Below 1024px the header condenses (DESIGN §3.2): the bottom nav and category drawer take over. */
      @media (max-width: 1023px) {
        .header-links, .mega-trigger { display: none; }
        /* Row one: logo and icons. Row two: the search field at full width, always reachable. */
        .header-row { flex-wrap: wrap; row-gap: 10px; }
        .rt-logo { margin-right: auto; }
        rt-search-box { order: 10; flex: 1 0 100%; }
      }
    `
  ]
})
export class HeaderComponent {
  readonly auth = inject(AuthService);
  readonly cart = inject(CartService);
  readonly compare = inject(CompareService);
  readonly wishlist = inject(WishlistService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  signOut(): void {
    this.auth.logout();
    this.toast.show('Signed out.');
    this.router.navigate(['/']);
  }
}

@Component({
  selector: 'rt-bottom-nav',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="rt-bottom-nav" aria-label="Mobile">
      <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
        <span class="ico" aria-hidden="true">⌂</span> Home
      </a>
      <a routerLink="/offer" routerLinkActive="active">
        <span class="ico" aria-hidden="true">％</span> Offer
      </a>
      <button type="button" [class.active]="menu.drawerOpen()" [attr.aria-expanded]="menu.drawerOpen()" aria-haspopup="dialog" (click)="menu.toggleDrawer()">
        <span class="ico" aria-hidden="true">▦</span> Category
      </button>
      <a routerLink="/pre-order" routerLinkActive="active">
        <span class="ico" aria-hidden="true">◷</span> Pre-order
      </a>
      <a [routerLink]="auth.isSignedIn() ? '/account/orders' : '/login'" routerLinkActive="active">
        <span class="ico" aria-hidden="true">☺</span> Profile
      </a>
    </nav>
  `
})
export class BottomNavComponent {
  readonly auth = inject(AuthService);
  readonly menu = inject(CategoryMenuService);
}

@Component({
  selector: 'rt-footer',
  standalone: true,
  imports: [RouterLink, NewsletterFormComponent],
  template: `
    <footer class="rt-footer">
      <div class="rt-container">
        <div class="rt-grid rt-cols-4" style="gap:26px;">
          <div>
            <h4>Rocky Telecom</h4>
            <p class="small" style="color:#cdbfa8;">
              One showroom, honest prices. Genuine phones, laptops and accessories with official
              warranty — serving Gaibandha since 2014.
            </p>
            <p class="mono-label" style="color:#a9977d;">OPENING HOURS · {{ store()?.openingHours || '10:00–21:00' }}</p>
            <p class="small">
              <a routerLink="/about">About us</a> · <a routerLink="/blogs">Blog</a> · <a routerLink="/faq">FAQ</a> · <a routerLink="/career">Careers</a>
            </p>
            <rt-newsletter-form />
          </div>

          <div>
            <h4>Shop</h4>
            <p class="small"><a routerLink="/shop">All products</a></p>
            <p class="small"><a routerLink="/brands">Brands</a></p>
            <p class="small"><a routerLink="/offer">Offers</a></p>
            <p class="small"><a routerLink="/pre-order">Pre-order</a></p>
            <p class="small"><a routerLink="/compare">Compare</a></p>
            <p class="small"><a routerLink="/shop" [queryParams]="{ sort: 'discount' }">Deals</a></p>
            <p class="small"><a routerLink="/search" [queryParams]="{ q: 'phone' }">Smartphones</a></p>
            <p class="small"><a routerLink="/track">Track an order</a></p>
          </div>

          <div>
            <h4>Help</h4>
            <p class="small"><a routerLink="/track">Delivery status</a></p>
            <p class="small"><a routerLink="/login">Your account</a></p>
            <p class="small"><a routerLink="/account/orders">Order history</a></p>
            <p class="small"><a routerLink="/shop-location">Showroom &amp; map</a></p>
            <p class="small"><a routerLink="/support">Contact support</a></p>
            <p class="small"><a routerLink="/trade-in">Trade in your phone</a></p>
            <p class="small"><a routerLink="/corporate">Corporate orders</a></p>
            <p class="small"><a routerLink="/feedback">Give feedback</a></p>
            <p class="small"><a routerLink="/policy/delivery">Delivery policy</a></p>
            <p class="small"><a routerLink="/policy/warranty">Warranty policy</a></p>
            <p class="small"><a routerLink="/policy/refund">Refund policy</a></p>
            <p class="small"><a routerLink="/policy/emi">EMI policy</a></p>
            <p class="small"><a routerLink="/policy/terms">Terms &amp; conditions</a></p>
            <p class="small"><a routerLink="/policy/privacy">Privacy policy</a></p>
            <p class="small"><a routerLink="/policy/cookies">Cookies policy</a></p>
            <p class="small"><a routerLink="/policy/exchange">Exchange policy</a></p>
            <p class="small"><a href="tel:01700000000">Call 01700-000000</a></p>
          </div>

          <div>
            <h4>Visit</h4>
            <p class="small" style="color:#cdbfa8;">{{ store()?.address || 'Sadullapur, Gaibandha' }}</p>
            <p class="small" style="color:#cdbfa8;">{{ store()?.phone || '01700000000' }}</p>
            <p class="mono-label" style="color:#a9977d;">CASH · BKASH · NAGAD · SSLCOMMERZ</p>
          </div>
        </div>

        <div class="rt-divider" style="margin:26px 0; opacity:0.35;"></div>

        <div class="rt-row rt-row--between rt-row--wrap">
          <span class="tiny" style="color:#a9977d;">© {{ year }} Rocky Telecom. All prices in BDT.</span>
          <span class="tiny" style="color:#a9977d;">Warranty claims handled at the Sadullapur showroom.</span>
        </div>
      </div>
    </footer>
  `
})
export class FooterComponent {
  readonly year = new Date().getFullYear();
  readonly store = signal<{ address: string; phone: string; openingHours: string | null } | null>(null);
}

@Component({
  selector: 'rt-toast-host',
  standalone: true,
  template: `
    <div class="rt-toasts" aria-live="polite">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="rt-toast" [class.rt-toast--error]="toast.kind === 'error'" (click)="toasts.dismiss(toast.id)">
          {{ toast.message }}
        </div>
      }
    </div>
  `
})
export class ToastHostComponent {
  readonly toasts = inject(ToastService);
  readonly count = computed(() => this.toasts.toasts().length);
}
