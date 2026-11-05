import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, describeError } from '../core/api.service';

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Footer sign-up: one email field, no account needed. */
@Component({
  selector: 'rt-newsletter-form',
  standalone: true,
  imports: [FormsModule],
  template: `
    @if (done()) {
      <p class="small" role="status" style="color:#cdbfa8;">Thanks — you're subscribed. We'll only email about offers and new arrivals.</p>
    } @else {
      <form class="news" (ngSubmit)="submit()" novalidate>
        <label class="mono-label" for="nl-email" style="color:#a9977d;">Shop news by email</label>
        <div class="row">
          <input
            id="nl-email"
            name="email"
            type="email"
            class="rt-input"
            placeholder="you@example.com"
            autocomplete="email"
            [ngModel]="email()"
            (ngModelChange)="email.set($event)"
          />
          <button class="rt-btn rt-btn--sm" type="submit" [disabled]="busy()">{{ busy() ? '…' : 'Subscribe' }}</button>
        </div>
        @if (error()) {
          <p class="tiny" style="color:#ffb4a8; margin:6px 0 0;" role="alert">{{ error() }}</p>
        }
      </form>
    }
  `,
  styles: [
    `
      .row { display: flex; gap: 8px; margin-top: 6px; }
      .row input { flex: 1; min-width: 0; }
    `
  ]
})
export class NewsletterFormComponent {
  private readonly api = inject(ApiService);

  readonly email = signal('');
  readonly busy = signal(false);
  readonly done = signal(false);
  readonly error = signal('');

  submit(): void {
    const value = this.email().trim();

    if (!EMAIL.test(value)) {
      this.error.set('Enter a valid email address.');
      return;
    }

    if (this.busy()) {
      return;
    }

    this.error.set('');
    this.busy.set(true);

    this.api.post('/newsletter', { email: value }).subscribe({
      next: () => {
        this.busy.set(false);
        this.done.set(true);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(describeError(error));
      }
    });
  }
}
