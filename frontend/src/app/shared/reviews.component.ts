import { DatePipe } from '@angular/common';
import { Component, OnChanges, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, describeError } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { Review, ReviewEligibility } from '../core/models';
import { ToastService } from '../core/toast.service';

/** Average, histogram, approved reviews, and — for verified buyers only — a form to add one. */
@Component({
  selector: 'rt-reviews',
  standalone: true,
  imports: [FormsModule, RouterLink, DatePipe],
  template: `
    <section class="reviews" aria-labelledby="reviews-title">
      <h2 id="reviews-title" style="font-size:24px;">Customer reviews</h2>

      @if (summary().count > 0) {
        <div class="summary">
          <div class="big">
            <strong>{{ summary().average }}</strong>
            <span class="stars" aria-hidden="true">{{ stars(summary().average) }}</span>
            <span class="tiny muted">{{ summary().count }} review{{ summary().count === 1 ? '' : 's' }}</span>
          </div>

          <div class="bars">
            @for (n of [5, 4, 3, 2, 1]; track n) {
              <div class="bar-row">
                <span class="tiny">{{ n }}★</span>
                <span class="bar"><span class="fill" [style.width.%]="percent(n)"></span></span>
                <span class="tiny muted">{{ histogram()[n] || 0 }}</span>
              </div>
            }
          </div>
        </div>
      }

      @for (review of reviews(); track review.id) {
        <article class="review">
          <div class="rt-row rt-row--between">
            <span class="stars" [attr.aria-label]="review.rating + ' out of 5 stars'">{{ stars(review.rating) }}</span>
            <span class="tiny muted">{{ review.createdAt | date: 'd MMM y' }}</span>
          </div>
          @if (review.body) {
            <p class="mb-0">{{ review.body }}</p>
          }
          <span class="tiny muted">Verified buyer</span>
        </article>
      } @empty {
        <p class="muted">No reviews yet. Reviews come from customers whose order has been delivered.</p>
      }

      <!-- WRITE A REVIEW -->
      @if (!auth.isSignedIn()) {
        <p class="small">
          <a routerLink="/login" [queryParams]="{ returnUrl: '/product/' + slug() }">Sign in</a> to review a product you have bought.
        </p>
      } @else if (done()) {
        <p class="small" role="status">Thanks! Your review will appear once the team approves it.</p>
      } @else if (eligibility()?.canReview) {
        <form class="rt-panel" (ngSubmit)="submit()">
          <h3 class="mt-0">Write a review</h3>

          <div class="stars-pick" role="radiogroup" aria-label="Your rating">
            @for (n of [1, 2, 3, 4, 5]; track n) {
              <button
                type="button"
                class="star"
                role="radio"
                [attr.aria-checked]="formRating() === n"
                [attr.aria-label]="n + ' out of 5'"
                [class.is-on]="formRating() >= n"
                (click)="formRating.set(n)"
              >★</button>
            }
          </div>

          <div class="rt-field">
            <label class="rt-label" for="rv-body">Your review (optional)</label>
            <textarea id="rv-body" name="body" class="rt-textarea" rows="3" maxlength="2000" [(ngModel)]="body"></textarea>
          </div>

          <button class="rt-btn rt-btn--rust rt-btn--sm" type="submit" [disabled]="busy() || formRating() === 0">
            {{ busy() ? 'Sending…' : 'Submit review' }}
          </button>
        </form>
      } @else if (eligibility()?.reason) {
        <p class="small muted">{{ eligibility()?.reason }}</p>
      }
    </section>
  `,
  styles: [
    `
      .reviews { margin-top: 36px; }
      .summary { display: flex; flex-wrap: wrap; gap: 28px; align-items: center; margin-bottom: 18px; }
      .big { display: grid; gap: 2px; }
      .big strong { font-size: 44px; line-height: 1; }
      .stars { color: var(--mustard, #e6a817); letter-spacing: 2px; }
      .bars { display: grid; gap: 4px; min-width: 220px; }
      .bar-row { display: grid; grid-template-columns: 28px 1fr 24px; gap: 8px; align-items: center; }
      .bar { height: 8px; border-radius: 99px; background: rgba(0, 0, 0, 0.1); overflow: hidden; }
      .fill { display: block; height: 100%; background: var(--mustard, #e6a817); }
      .review { display: grid; gap: 6px; padding: 14px 0; border-top: 1px dashed rgba(0, 0, 0, 0.25); }
      .stars-pick { display: flex; gap: 2px; margin-bottom: 8px; }
      .star { width: 44px; height: 44px; border: 0; background: none; font-size: 30px; line-height: 1; cursor: pointer; color: rgba(0, 0, 0, 0.25); }
      .star.is-on { color: var(--mustard, #e6a817); }
    `
  ]
})
export class ReviewsComponent implements OnInit, OnChanges {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);

  readonly slug = input.required<string>();
  /** The product's rating summary, already loaded with the product. */
  readonly summaryInput = input.required<{ average: number | null; count: number; histogram: Record<string, number> }>({ alias: 'summary' });

  readonly reviews = signal<Review[]>([]);
  readonly eligibility = signal<ReviewEligibility | null>(null);
  readonly busy = signal(false);
  readonly done = signal(false);

  // The form's own rating, separate from the product summary input above.
  readonly formRating = signal(0);

  body = '';

  ngOnInit(): void {
    this.load();
  }

  ngOnChanges(): void {
    // Navigating from one product to another reuses this component.
    this.done.set(false);
    this.formRating.set(0);
    this.body = '';
  }

  // ---- template helpers (kept as methods/signals so the template stays declarative)
  summary = () => ({ average: this.summaryInput().average ?? 0, count: this.summaryInput().count });
  histogram = () => this.summaryInput().histogram as unknown as Record<number, number>;
  percent = (n: number) => (this.summaryInput().count > 0 ? ((this.histogram()[n] || 0) / this.summaryInput().count) * 100 : 0);
  stars = (value: number) => '★'.repeat(Math.round(value)) + '☆'.repeat(5 - Math.round(value));

  private load(): void {
    const slug = this.slug();

    this.api.get<Review[]>(`/products/${encodeURIComponent(slug)}/reviews`).subscribe({
      next: (items) => this.reviews.set(items ?? []),
      error: () => this.reviews.set([])
    });

    if (this.auth.isSignedIn()) {
      this.api.get<ReviewEligibility>(`/products/${encodeURIComponent(slug)}/reviews/eligibility`).subscribe({
        next: (result) => this.eligibility.set(result),
        error: () => this.eligibility.set(null)
      });
    }
  }

  submit(): void {
    if (this.busy() || this.formRating() === 0) {
      return;
    }

    this.busy.set(true);

    this.api
      .post(`/products/${encodeURIComponent(this.slug())}/reviews`, { rating: this.formRating(), body: this.body.trim() || null })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.done.set(true);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.toast.show(describeError(error), 'error');
        }
      });
  }
}
