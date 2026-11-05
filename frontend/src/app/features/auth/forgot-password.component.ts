import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, describeError } from '../../core/api.service';
import { SeoService } from '../../core/seo.service';
import { ToastService } from '../../core/toast.service';

const PHONE = /^(?:\+?88)?01[3-9]\d{8}$/;

/** /forgot-password — phone → SMS code → new password. Two short steps, no email needed. */
@Component({
  selector: 'rt-forgot-password',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="rt-container" style="padding-top:32px; max-width:520px;">
      <div class="rt-panel">
        <div class="kicker mono-label">Account</div>
        <h1 style="font-size:30px;">Reset your password</h1>

        @if (step() === 1) {
          <p class="small">Enter the mobile number on your account. We'll text you a 6-digit code.</p>

          <form (ngSubmit)="sendCode()">
            <div class="rt-field">
              <label class="rt-label" for="fp-phone">Mobile number</label>
              <input id="fp-phone" name="phone" class="rt-input" inputmode="tel" placeholder="01XXXXXXXXX" [(ngModel)]="phone" autocomplete="tel" />
              @if (phone && !validPhone()) {
                <p class="rt-error mb-0">Enter a Bangladeshi mobile number, e.g. 01712345678.</p>
              }
            </div>

            <button class="rt-btn rt-btn--rust rt-btn--block" type="submit" [disabled]="busy() || !validPhone()">
              {{ busy() ? 'Sending…' : 'Send code' }}
            </button>
          </form>
        } @else {
          <p class="small">
            We sent a code to <strong>{{ phone }}</strong>. Enter it below with your new password.
          </p>

          <form (ngSubmit)="reset()">
            <div class="rt-field">
              <label class="rt-label" for="fp-code">6-digit code</label>
              <input id="fp-code" name="code" class="rt-input" inputmode="numeric" maxlength="6" [(ngModel)]="code" autocomplete="one-time-code" />
            </div>

            <div class="rt-field">
              <label class="rt-label" for="fp-new">New password (8+ characters)</label>
              <input id="fp-new" name="password" type="password" class="rt-input" [(ngModel)]="password" minlength="8" autocomplete="new-password" />
            </div>

            <button class="rt-btn rt-btn--rust rt-btn--block" type="submit" [disabled]="busy() || code.length < 4 || password.length < 8">
              {{ busy() ? 'Saving…' : 'Set new password' }}
            </button>

            <p class="small center" style="margin:12px 0 0;">
              <button type="button" class="linkish" (click)="step.set(1)">Use a different number</button> ·
              <button type="button" class="linkish" [disabled]="busy()" (click)="sendCode()">Resend code</button>
            </p>
          </form>
        }

        <div class="rt-dashed"></div>
        <p class="tiny mb-0"><a routerLink="/login">← Back to sign in</a></p>
      </div>
    </div>
  `,
  styles: [
    `
      .linkish { background: none; border: 0; padding: 0; min-height: 32px; text-decoration: underline; cursor: pointer; font: inherit; }
    `
  ]
})
export class ForgotPasswordComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly step = signal<1 | 2>(1);
  readonly busy = signal(false);

  phone = '';
  code = '';
  password = '';

  constructor() {
    inject(SeoService).set({ title: 'Reset password', noindex: true });
  }

  validPhone(): boolean {
    return PHONE.test(this.phone.trim());
  }

  sendCode(): void {
    if (this.busy() || !this.validPhone()) {
      return;
    }

    this.busy.set(true);

    this.api.post('/auth/otp/send', { phone: this.phone.trim(), purpose: 'reset' }).subscribe({
      next: () => {
        this.busy.set(false);
        this.step.set(2);
        // The same answer whether or not the number has an account, so this can't be used to probe for users.
        this.toast.show('If that number has an account, a code is on its way.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }

  reset(): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);

    this.api
      .post('/auth/password/reset', { phone: this.phone.trim(), code: this.code.trim(), newPassword: this.password })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.toast.show('Password updated. Please sign in.');
          this.router.navigate(['/login']);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.toast.show(describeError(error), 'error');
        }
      });
  }
}
