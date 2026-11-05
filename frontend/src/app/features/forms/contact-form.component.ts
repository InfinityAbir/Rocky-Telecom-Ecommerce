import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService, describeError } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { SeoService } from '../../core/seo.service';
import { ToastService } from '../../core/toast.service';

const PHONE = /^(?:\+?88)?01[3-9]\d{8}$/;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type FieldType = 'text' | 'tel' | 'email' | 'textarea' | 'select' | 'rating';

interface Field {
  key: string;
  label: string;
  type: FieldType;
  required?: boolean;
  max?: number;
  placeholder?: string;
  options?: string[];
  /** Pre-filled from the signed-in user when present. */
  prefill?: 'name' | 'phone' | 'email';
}

interface FormConfig {
  kicker: string;
  title: string;
  intro: string;
  endpoint: string;
  submit: string;
  success: string;
  fields: Field[];
}

/**
 * The shop's contact forms share one component; the route's `form` data picks the fields and the
 * endpoint. Validation mirrors the server (BD phone, email shape, required fields) and the server
 * remains the authority.
 */
const FORMS: Record<string, FormConfig> = {
  support: {
    kicker: 'Support',
    title: 'Contact support',
    intro: 'Tell us what went wrong or what you need. The showroom team replies by phone, usually the same day.',
    endpoint: '/support',
    submit: 'Send request',
    success: 'Your request is with the support team. We will call you soon.',
    fields: [
      { key: 'name', label: 'Your name', type: 'text', required: true, max: 120, prefill: 'name' },
      { key: 'phone', label: 'Mobile number', type: 'tel', required: true, placeholder: '01XXXXXXXXX', prefill: 'phone' },
      { key: 'email', label: 'Email (optional)', type: 'email', max: 256, prefill: 'email' },
      { key: 'subject', label: 'Subject', type: 'text', required: true, max: 200, placeholder: 'e.g. Warranty claim for my phone' },
      { key: 'message', label: 'How can we help?', type: 'textarea', required: true, max: 2000 }
    ]
  },
  'trade-in': {
    kicker: 'Trade in',
    title: 'Trade in your phone',
    intro: 'Describe your current phone and we will value it. Bring it to the showroom for the final price.',
    endpoint: '/trade-in',
    submit: 'Request a valuation',
    success: 'We have your trade-in request. The showroom will contact you with an estimate.',
    fields: [
      { key: 'name', label: 'Your name', type: 'text', required: true, max: 120, prefill: 'name' },
      { key: 'phone', label: 'Mobile number', type: 'tel', required: true, placeholder: '01XXXXXXXXX', prefill: 'phone' },
      { key: 'deviceModel', label: 'Your phone (brand and model)', type: 'text', required: true, max: 160, placeholder: 'e.g. iPhone 12, 128GB' },
      { key: 'condition', label: 'Condition', type: 'select', options: ['Like new', 'Good', 'Fair', 'Poor'] },
      { key: 'note', label: 'Anything else? (optional)', type: 'textarea', max: 500, placeholder: 'Battery health, repairs, box and charger included…' }
    ]
  },
  corporate: {
    kicker: 'Corporate',
    title: 'Corporate & bulk orders',
    intro: 'Buying for a company, school or office? Tell us what you need and we will quote a price.',
    endpoint: '/corporate',
    submit: 'Send inquiry',
    success: 'Thank you. Our team will contact you with a quotation.',
    fields: [
      { key: 'companyName', label: 'Company or organisation', type: 'text', required: true, max: 160 },
      { key: 'contactName', label: 'Contact person', type: 'text', required: true, max: 120, prefill: 'name' },
      { key: 'phone', label: 'Mobile number', type: 'tel', required: true, placeholder: '01XXXXXXXXX', prefill: 'phone' },
      { key: 'email', label: 'Email (optional)', type: 'email', max: 256, prefill: 'email' },
      { key: 'message', label: 'What do you need?', type: 'textarea', required: true, max: 2000, placeholder: 'Products, quantities and timing…' }
    ]
  },
  feedback: {
    kicker: 'Feedback',
    title: 'Tell us how we are doing',
    intro: 'Good or bad, we read every message. You can stay anonymous.',
    endpoint: '/feedback',
    submit: 'Send feedback',
    success: 'Thank you — your feedback has been passed to the team.',
    fields: [
      { key: 'rating', label: 'Your rating (optional)', type: 'rating' },
      { key: 'name', label: 'Your name (optional)', type: 'text', max: 120, prefill: 'name' },
      { key: 'email', label: 'Email (optional)', type: 'email', max: 254, prefill: 'email' },
      { key: 'message', label: 'Your feedback', type: 'textarea', required: true, max: 2000 }
    ]
  }
};

@Component({
  selector: 'rt-contact-form',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="rt-container" style="padding-top:24px; max-width:720px;">
      @if (config(); as c) {
        <div class="kicker mono-label">{{ c.kicker }}</div>
        <h1 style="margin-bottom:8px;">{{ c.title }}</h1>
        <p>{{ c.intro }}</p>

        @if (done()) {
          <div class="rt-panel" role="status">
            <h3 class="mt-0">Received</h3>
            <p>{{ c.success }}</p>
            <a class="rt-btn rt-btn--ghost rt-btn--sm" routerLink="/">Back to the shop</a>
            <button type="button" class="rt-btn rt-btn--ghost rt-btn--sm" style="margin-left:8px;" (click)="done.set(false)">Send another</button>
          </div>
        } @else {
          <form class="rt-panel" (ngSubmit)="submit()" novalidate>
            @for (f of c.fields; track f.key) {
              <div class="rt-field">
                <label class="rt-label" [for]="'cf-' + f.key">{{ f.label }}</label>

                @switch (f.type) {
                  @case ('textarea') {
                    <textarea
                      [id]="'cf-' + f.key"
                      [name]="f.key"
                      class="rt-textarea"
                      rows="4"
                      [attr.maxlength]="f.max"
                      [attr.placeholder]="f.placeholder"
                      [ngModel]="values[f.key]"
                      (ngModelChange)="values[f.key] = $event"
                    ></textarea>
                  }
                  @case ('select') {
                    <select [id]="'cf-' + f.key" [name]="f.key" class="rt-select" [ngModel]="values[f.key]" (ngModelChange)="values[f.key] = $event">
                      <option value="">Choose…</option>
                      @for (o of f.options; track o) {
                        <option [value]="o">{{ o }}</option>
                      }
                    </select>
                  }
                  @case ('rating') {
                    <div class="stars" role="radiogroup" [attr.aria-label]="f.label">
                      @for (n of [1, 2, 3, 4, 5]; track n) {
                        <button
                          type="button"
                          class="star"
                          role="radio"
                          [attr.aria-checked]="rating() === n"
                          [attr.aria-label]="n + ' out of 5'"
                          [class.is-on]="rating() >= n"
                          (click)="rating.set(rating() === n ? 0 : n)"
                        >★</button>
                      }
                    </div>
                  }
                  @default {
                    <input
                      [id]="'cf-' + f.key"
                      [name]="f.key"
                      class="rt-input"
                      [type]="f.type === 'tel' ? 'tel' : f.type === 'email' ? 'email' : 'text'"
                      [attr.inputmode]="f.type === 'tel' ? 'tel' : null"
                      [attr.maxlength]="f.max"
                      [attr.placeholder]="f.placeholder"
                      [ngModel]="values[f.key]"
                      (ngModelChange)="values[f.key] = $event"
                    />
                  }
                }

                @if (touched() && errors()[f.key]; as message) {
                  <p class="rt-error mb-0">{{ message }}</p>
                }
              </div>
            }

            <button class="rt-btn rt-btn--rust" type="submit" [disabled]="busy()">
              {{ busy() ? 'Sending…' : c.submit }}
            </button>
          </form>
        }
      } @else {
        <div class="rt-panel center" style="padding:40px 20px;">
          <h3 class="mt-0">Page not found</h3>
          <a class="rt-btn rt-btn--ghost" routerLink="/">Back to the shop</a>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .stars { display: flex; gap: 4px; }
      .star {
        width: 44px;
        height: 44px;
        border: 0;
        background: none;
        font-size: 30px;
        line-height: 1;
        cursor: pointer;
        color: rgba(0, 0, 0, 0.25);
      }
      .star.is-on { color: var(--mustard, #e6a817); }
    `
  ]
})
export class ContactFormComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly seo = inject(SeoService);
  private readonly destroy$ = new Subject<void>();

  readonly config = signal<FormConfig | null>(null);
  readonly busy = signal(false);
  readonly done = signal(false);
  readonly touched = signal(false);
  readonly rating = signal(0);

  values: Record<string, string> = {};

  ngOnInit(): void {
    this.route.data.pipe(takeUntil(this.destroy$)).subscribe((data) => {
      const config = FORMS[data['form'] as string] ?? null;
      this.config.set(config);
      this.done.set(false);
      this.touched.set(false);
      this.rating.set(0);
      this.values = {};

      if (config) {
        const user = this.auth.user();
        for (const field of config.fields) {
          if (field.prefill && user) {
            this.values[field.key] = (user as unknown as Record<string, string | null>)[field.prefill] ?? '';
          }
        }

        this.seo.set({ title: config.title, description: config.intro, path: `/${this.route.snapshot.url.map((s) => s.path).join('/')}` });
      }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Field → message for the current values; empty when the form can be sent. */
  errors(): Record<string, string> {
    const config = this.config();
    const result: Record<string, string> = {};
    if (!config) {
      return result;
    }

    for (const f of config.fields) {
      const value = (this.values[f.key] ?? '').trim();

      if (f.required && !value) {
        result[f.key] = 'This field is required.';
      } else if (value && f.type === 'tel' && !PHONE.test(value)) {
        result[f.key] = 'Enter a Bangladeshi mobile number, e.g. 01712345678.';
      } else if (value && f.type === 'email' && !EMAIL.test(value)) {
        result[f.key] = 'Enter a valid email address.';
      }
    }

    return result;
  }

  submit(): void {
    const config = this.config();
    if (!config || this.busy()) {
      return;
    }

    this.touched.set(true);
    if (Object.keys(this.errors()).length > 0) {
      return;
    }

    const body: Record<string, string | number | null> = {};
    for (const f of config.fields) {
      body[f.key] = f.type === 'rating' ? (this.rating() > 0 ? this.rating() : null) : (this.values[f.key] ?? '').trim() || null;
    }

    this.busy.set(true);

    this.api.post(config.endpoint, body).subscribe({
      next: () => {
        this.busy.set(false);
        this.done.set(true);
        this.values = {};
        this.rating.set(0);
        this.touched.set(false);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toast.show(describeError(error), 'error');
      }
    });
  }
}
