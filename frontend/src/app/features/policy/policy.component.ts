import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { SeoService } from '../../core/seo.service';

interface PolicyPage {
  /** Small label above the title; policies use 'Policy'. */
  kicker?: string;
  /** Pages that are not policies are left out of the "Other policies" list. */
  info?: boolean;
  title: string;
  intro: string;
  sections: { heading: string; body: string }[];
}

/**
 * Static policy pages at /policy/{slug}. The wording below is a working draft written from the
 * PRD (single showroom, COD + online payment, official warranty, EMI calculator only). The
 * owner must review and replace it with the final legal text before launch (PRD §11).
 */
export const POLICY_PAGES: Record<string, PolicyPage> = {
  cookies: {
    title: 'Cookies Policy',
    intro: 'What small files this website stores in your browser.',
    sections: [
      { heading: 'What we store', body: 'Rocky Telecom keeps a few small items in your browser: a sign-in session, your cart, your compare list and your recent searches. They make the shop work and are not used for advertising.' },
      { heading: 'Your choices', body: 'You can clear this data in your browser settings at any time. Some features, such as staying signed in, will stop working until you sign in again.' }
    ]
  },
  'pre-order': {
    title: 'Pre-order Policy',
    intro: 'How reserving a new arrival works.',
    sections: [
      { heading: 'A request, not a payment', body: 'A pre-order request is a reservation of interest. No money is taken online. The showroom calls you to confirm the price, availability and expected date.' },
      { heading: 'Changing your mind', body: 'You can cancel a pre-order request at any time before you confirm it with the showroom.' }
    ]
  },
  membership: {
    title: 'Membership Policy',
    intro: 'Your Rocky Telecom account and what comes with it.',
    sections: [
      { heading: 'Your account', body: 'An account keeps your orders, addresses and wishlist together. Your mobile number is your sign-in.' },
      { heading: 'Closing your account', body: 'Ask the showroom to close your account. Order records needed for warranty and accounting are kept as the law requires.' }
    ]
  },
  about: {
    kicker: 'About us',
    info: true,
    title: 'About Rocky Telecom',
    intro: 'A mobile and electronics showroom in Sadullapur, Gaibandha — now online.',
    sections: [
      { heading: 'Who we are', body: 'Rocky Telecom sells phones, tablets, laptops, smart watches, audio gear and accessories from one showroom in Sadullapur, Gaibandha.' },
      { heading: 'How we work', body: 'Every product has its warranty type stated on the page. You can order online for home delivery, or reserve and collect from the showroom.' },
      { heading: 'Visit us', body: 'Sadullapur, Gaibandha. Call the showroom for opening hours and directions.' }
    ]
  },
  faq: {
    kicker: 'Help',
    info: true,
    title: 'Frequently asked questions',
    intro: 'Quick answers about ordering, delivery and warranty.',
    sections: [
      { heading: 'Do I need an account to order?', body: 'No. You can check out as a guest with your name and mobile number. An account lets you track orders and save addresses.' },
      { heading: 'How do I track my order?', body: 'Use Track Order with your order number and the mobile number you ordered with. Signed-in customers can see all orders in their account.' },
      { heading: 'Which payment methods can I use?', body: 'Cash on delivery, bKash, Nagad and card or bank payment through SSLCommerz. EMI is shown as a calculator; the final plan is set by your bank.' },
      { heading: 'Can I pick up from the showroom?', body: 'Yes. Choose showroom pickup at checkout and bring your order number.' },
      { heading: 'What if my phone has a problem?', body: 'Bring it to the showroom with your order number. We handle the claim with the warranty provider.' }
    ]
  },
  career: {
    kicker: 'Careers',
    info: true,
    title: 'Work with us',
    intro: 'Join the Rocky Telecom team.',
    sections: [
      { heading: 'Open roles', body: 'We do not list open roles online right now. If you would like to work in sales, support or delivery, visit the showroom or send us a message.' },
      { heading: 'How to get in touch', body: 'Use the support form and write “Careers” in the subject. Tell us a little about yourself and the role you are interested in.' }
    ]
  },
  terms: {
    title: 'Terms & Conditions',
    intro: 'The rules for using Rocky Telecom and buying from us.',
    sections: [
      { heading: 'Orders', body: 'An order is confirmed once we verify stock and, for online payment, the payment is received. We may cancel an order if an item is unavailable and will refund any payment made.' },
      { heading: 'Prices', body: 'Prices are in Bangladeshi Taka and are calculated by our server at checkout. If a listed price is wrong we may correct it before confirming your order.' },
      { heading: 'Your account', body: 'Keep your password private. You are responsible for activity on your account.' }
    ]
  },
  privacy: {
    title: 'Privacy Policy',
    intro: 'What we collect, why, and how we protect it.',
    sections: [
      { heading: 'What we collect', body: 'Name, phone number, delivery address and order history, plus basic usage data that helps us keep the shop working.' },
      { heading: 'How we use it', body: 'To process and deliver orders, send order updates by SMS or email, and provide warranty support. We do not sell personal data.' },
      { heading: 'Payments', body: 'Card and mobile-wallet details are handled by the payment provider. We never store card numbers.' }
    ]
  },
  refund: {
    title: 'Refund Policy',
    intro: 'When and how you can get your money back.',
    sections: [
      { heading: 'Eligibility', body: 'Refunds are considered for items that arrive damaged, are not as described, or cannot be supplied. Contact the showroom with your order number.' },
      { heading: 'How refunds are paid', body: 'Approved refunds go back to the original payment method. Cash-on-delivery refunds are arranged with the showroom.' }
    ]
  },
  warranty: {
    title: 'Warranty Policy',
    intro: 'How warranty works for products bought from us.',
    sections: [
      { heading: 'Official warranty', body: 'Official-variant devices carry the manufacturer or distributor warranty shown on the product page.' },
      { heading: 'Claims', body: 'Bring the device and your order number or invoice to the Sadullapur showroom. We will handle the claim with the warranty provider.' },
      { heading: 'Not covered', body: 'Physical damage, liquid damage and unauthorised repairs are not covered unless a care plan says otherwise.' }
    ]
  },
  delivery: {
    title: 'Delivery Policy',
    intro: 'How your order reaches you.',
    sections: [
      { heading: 'Home delivery', body: 'Charges and estimated days depend on your delivery zone and are shown at checkout before you pay.' },
      { heading: 'Showroom pickup', body: 'Choose pickup at checkout and collect from the Sadullapur showroom with your order number and a phone number we can verify.' },
      { heading: 'Tracking', body: 'Use Track Order with your order number and phone to see the latest status.' }
    ]
  },
  emi: {
    title: 'EMI Policy',
    intro: 'Paying in instalments with a partner bank card.',
    sections: [
      { heading: 'Calculator only', body: 'The EMI calculator on product pages shows an estimate by bank and months. The final plan, interest and approval are set by your bank.' },
      { heading: 'Minimum price', body: 'EMI options appear only when the product price meets the bank’s minimum amount.' }
    ]
  },
  cancellation: {
    title: 'Cancellation Policy',
    intro: 'Changing your mind after ordering.',
    sections: [
      { heading: 'Before shipping', body: 'You can cancel an order from your account or by contacting the showroom while it has not been shipped. Stock is returned and any online payment is refunded.' },
      { heading: 'After shipping', body: 'Once an order is shipped it can no longer be cancelled online. Contact the showroom for help.' }
    ]
  },
  exchange: {
    title: 'Exchange Policy',
    intro: 'Swapping a product for another.',
    sections: [
      { heading: 'Eligibility', body: 'Exchanges are handled at the showroom and depend on the product’s condition and availability. Bring the original packaging and invoice.' }
    ]
  }
};

@Component({
  selector: 'rt-policy',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="rt-container policy" style="padding-top:24px;">
      @if (page(); as p) {
        <div class="kicker mono-label">{{ p.kicker || 'Policy' }}</div>
        <h1 style="margin-bottom:8px;">{{ p.title }}</h1>
        <p>{{ p.intro }}</p>

        <p class="small draft-note" role="note">Draft wording — subject to final review by Rocky Telecom.</p>

        @for (section of p.sections; track section.heading) {
          <h2 style="font-size:22px; margin-top:24px;">{{ section.heading }}</h2>
          <p>{{ section.body }}</p>
        }

        <div class="rt-dashed" style="margin:28px 0;"></div>
        <p class="small">Other policies:</p>
        <div class="policy-links">
          @for (entry of entries; track entry.slug) {
            <a class="rt-chip" [routerLink]="['/policy', entry.slug]">{{ entry.title }}</a>
          }
        </div>
      } @else {
        <div class="rt-panel center" style="padding:40px 20px;">
          <h3 class="mt-0">Page not found</h3>
          <p>We don't have a policy page at this address.</p>
          <a class="rt-btn rt-btn--ghost" routerLink="/">Back to the shop</a>
        </div>
      }
    </div>
  `,
  styles: [
    `
      .policy { max-width: 760px; }
      .draft-note { color: var(--rust, #965c20); font-weight: 600; }
      .policy-links { display: flex; flex-wrap: wrap; gap: 8px; }
    `
  ]
})
export class PolicyComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly seo = inject(SeoService);
  private readonly destroy$ = new Subject<void>();

  readonly page = signal<PolicyPage | null>(null);
  readonly entries = Object.entries(POLICY_PAGES)
    .filter(([, value]) => !value.info)
    .map(([slug, value]) => ({ slug, title: value.title }));

  ngOnInit(): void {
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      // /policy/{slug} for policies; /about, /faq and /career pass their slug as route data.
      const slug = params.get('slug') ?? (this.route.snapshot.data['slug'] as string | undefined) ?? '';
      const page = POLICY_PAGES[slug] ?? null;
      this.page.set(page);
      this.seo.set({ title: page?.title ?? 'Page not found', description: page?.intro, path: page?.info ? `/${slug}` : `/policy/${slug}`, noindex: !page });
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
