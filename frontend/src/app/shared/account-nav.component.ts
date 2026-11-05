import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** Tabs shared by the signed-in customer pages. */
@Component({
  selector: 'rt-account-nav',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="tabs" aria-label="Account">
      <a class="rt-chip" routerLink="/account/orders" routerLinkActive="is-active">Orders</a>
      <a class="rt-chip" routerLink="/account/profile" routerLinkActive="is-active">Profile &amp; addresses</a>
      <a class="rt-chip" routerLink="/wishlist" routerLinkActive="is-active">Wishlist</a>
    </nav>
  `,
  styles: [
    `
      .tabs { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0 18px; }
    `
  ]
})
export class AccountNavComponent {}
