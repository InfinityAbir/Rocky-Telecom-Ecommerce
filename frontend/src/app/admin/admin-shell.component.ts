import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'rt-admin-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="rt-container admin-wrap">
      <aside class="admin-nav">
        <div class="admin-brand">
          <span class="rt-tag rt-tag--rust">Admin</span>
          <h2 class="mt-2 mb-0">The back counter</h2>
          <p class="tiny muted mb-0">{{ auth.user()?.name }} · {{ auth.user()?.roles?.join(', ') }}</p>
        </div>

        <nav class="rt-stack" style="gap:6px;">
          <a routerLink="/admin" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">▦ Dashboard</a>
          <a routerLink="/admin/orders" routerLinkActive="active">▤ Orders</a>
          <a routerLink="/admin/products" routerLinkActive="active">▥ Products</a>
          <a routerLink="/admin/categories" routerLinkActive="active">☷ Categories</a>
          <a routerLink="/admin/brands" routerLinkActive="active">◈ Brands</a>
          @if (canManageOrders()) {
            <a routerLink="/admin/inventory" routerLinkActive="active">▣ Inventory</a>
          }
          @if (canManageOrders()) {
            <a routerLink="/admin/pre-orders" routerLinkActive="active">✎ Pre-orders</a>
            <a routerLink="/admin/inbox" routerLinkActive="active">✉ Inbox</a>
          }
          <a routerLink="/admin/campaigns" routerLinkActive="active">% Offers</a>
          @if (isAdminOnly()) {
            <a routerLink="/admin/flash-sales" routerLinkActive="active">⚡ Flash sales</a>
            <a routerLink="/admin/coupons" routerLinkActive="active">🏷 Coupons</a>
            <a routerLink="/admin/emi" routerLinkActive="active">₹ EMI plans</a>
          }
          <a routerLink="/admin/banners" routerLinkActive="active">▭ Banners</a>
          <a routerLink="/admin/showcases" routerLinkActive="active">★ Home shelves</a>
          <a routerLink="/admin/news" routerLinkActive="active">✳ Ticker</a>
          <a routerLink="/admin/blogs" routerLinkActive="active">✎ Blog</a>
          @if (canManageOrders()) {
            <a routerLink="/admin/reviews" routerLinkActive="active">☆ Reviews</a>
            <a routerLink="/admin/settings" routerLinkActive="active">⚙ Settings</a>
          }
        </nav>

        <div class="rt-dashed"></div>

        <a class="rt-btn rt-btn--ghost rt-btn--sm rt-btn--block" routerLink="/">← Storefront</a>

        <p class="tiny muted mt-2 mb-0">
          Staff see orders and stock. Pricing, refunds and roles stay with Admin.
        </p>
      </aside>

      <section class="admin-body">
        <router-outlet />
      </section>
    </div>
  `,
  styles: [
    `
      .admin-wrap {
        display: grid;
        grid-template-columns: 230px 1fr;
        gap: 22px;
        padding-top: 24px;
        align-items: start;
      }

      .admin-nav {
        position: sticky;
        top: 92px;
        /* Many links: let the menu scroll on its own instead of running off-screen. */
        max-height: calc(100vh - 112px);
        overflow-y: auto;
        background: var(--ink);
        color: var(--cream);
        border-radius: 16px;
        padding: 18px;
      }

      .admin-nav h2 { color: var(--cream); font-size: 20px; }
      .admin-nav p { color: #c9b9a0; }

      .admin-nav nav a {
        display: block;
        padding: 10px 12px;
        border-radius: 9px;
        color: #e9dcc6;
        text-decoration: none;
        font-weight: 600;
        font-size: 14px;
      }

      .admin-nav nav a:hover { background: rgba(255, 249, 236, 0.08); }
      .admin-nav nav a.active { background: var(--mustard); color: var(--ink); }

      @media (max-width: 900px) {
        .admin-wrap { grid-template-columns: 1fr; }
        .admin-nav { position: static; }
      }
    `
  ]
})
export class AdminShellComponent {
  readonly auth = inject(AuthService);

  /** Pre-order requests hold customer phone numbers, so they are limited to Admin and Staff. */
  readonly isAdminOnly = computed(() => (this.auth.user()?.roles ?? []).includes('Admin'));

  readonly canManageOrders = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    return roles.includes('Admin') || roles.includes('Staff');
  });
}
