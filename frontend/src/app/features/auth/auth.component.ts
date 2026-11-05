import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toApiError } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { CartService } from '../../core/cart.service';

@Component({
  selector: 'rt-auth',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="rt-container auth-wrap">
      <div class="rt-panel auth-card">
        <div class="rt-row" style="gap:8px; margin-bottom:16px;">
          <button class="rt-chip" [class.is-active]="mode() === 'login'" (click)="mode.set('login')">Sign in</button>
          <button class="rt-chip" [class.is-active]="mode() === 'register'" (click)="mode.set('register')">
            Create account
          </button>
        </div>

        <h1 style="font-size:34px;">{{ mode() === 'login' ? 'Welcome back.' : 'Join the counter.' }}</h1>
        <p class="small">
          Your phone number is your account. Cart, orders and warranty records stay in one place.
        </p>

        <form (ngSubmit)="submit()" #form="ngForm">
          @if (mode() === 'register') {
            <div class="rt-field">
              <label class="rt-label" for="name">Full name</label>
              <input id="name" name="name" class="rt-input" [(ngModel)]="name" required maxlength="120" />
            </div>
          }

          <div class="rt-field">
            <label class="rt-label" for="phone">Mobile number</label>
            <input
              id="phone"
              name="phone"
              class="rt-input"
              [(ngModel)]="phone"
              required
              inputmode="tel"
              placeholder="01XXXXXXXXX"
              pattern="^(?:\\+?88)?01[3-9]\\d{8}$"
            />
          </div>

          @if (mode() === 'register') {
            <div class="rt-field">
              <label class="rt-label" for="email">Email (optional)</label>
              <input id="email" name="email" type="email" class="rt-input" [(ngModel)]="email" />
            </div>
          }

          <div class="rt-field">
            <label class="rt-label" for="password">Password</label>
            <input
              id="password"
              name="password"
              type="password"
              class="rt-input"
              [(ngModel)]="password"
              required
              minlength="8"
              autocomplete="current-password"
            />
            @if (mode() === 'register') {
              <span class="tiny muted">At least 8 characters, including one number.</span>
            }
          </div>

          @if (error()) {
            <p class="rt-error">{{ error() }}</p>
          }

          <button class="rt-btn rt-btn--rust rt-btn--block" type="submit" [disabled]="busy()">
            {{ busy() ? 'One moment…' : mode() === 'login' ? 'Sign in' : 'Create account' }}
          </button>

          @if (mode() === 'login') {
            <p class="small center" style="margin:12px 0 0;"><a routerLink="/forgot-password">Forgot your password?</a></p>
          }
        </form>

        <div class="rt-dashed"></div>
        <p class="tiny muted mb-0">
          By continuing you agree to our warranty and return terms.
          <a routerLink="/track">Track a guest order</a> instead?
        </p>
      </div>

      <aside class="side-note">
        <span class="rt-tag rt-tag--rust">Why sign in?</span>
        <h2 class="mt-2">One counter, one record.</h2>
        <ul class="benefits">
          <li><strong>Faster checkout</strong> — saved details, no retyping.</li>
          <li><strong>Guest cart merge</strong> — everything you picked follows you in.</li>
          <li><strong>Warranty history</strong> — your purchases stay on file at the shop.</li>
          <li><strong>Order timeline</strong> — packed, shipped, delivered, in one place.</li>
        </ul>
        <p class="tiny muted">We never store card numbers. Gateway payments happen on the gateway.</p>
      </aside>
    </div>
  `,
  styles: [
    `
      .auth-wrap {
        display: grid;
        grid-template-columns: minmax(0, 460px) 1fr;
        gap: 30px;
        padding-top: 40px;
        align-items: start;
      }

      .auth-card { padding: 26px; }

      .side-note {
        background: var(--paper-deep);
        border: 2px dashed var(--ink);
        border-radius: 16px;
        padding: 26px;
      }

      .benefits { margin: 14px 0; padding-left: 18px; }
      .benefits li { margin-bottom: 8px; font-size: 14px; }

      @media (max-width: 860px) {
        .auth-wrap { grid-template-columns: 1fr; }
      }
    `
  ]
})
export class AuthComponent {
  private readonly auth = inject(AuthService);
  private readonly cart = inject(CartService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly mode = signal<'login' | 'register'>('login');
  readonly busy = signal(false);
  readonly error = signal('');

  name = '';
  phone = '';
  email = '';
  password = '';

  submit(): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.error.set('');

    const request =
      this.mode() === 'login'
        ? this.auth.login(this.phone.trim(), this.password)
        : this.auth.register(this.name.trim(), this.phone.trim(), this.password, this.email.trim() || undefined);

    request.subscribe({
      next: () => {
        this.cart.mergeAfterLogin();
        this.busy.set(false);

        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        this.router.navigateByUrl(returnUrl && returnUrl.startsWith('/') ? returnUrl : '/');
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(toApiError(error).message);
      }
    });
  }
}
