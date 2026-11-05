import { Directive, ElementRef, OnDestroy, OnInit, Pipe, PipeTransform, inject } from '@angular/core';

/**
 * Strips trailing timestamp numbers (e.g. 1792781455) from product display names.
 */
export function cleanProductName(name: string | null | undefined): string {
  if (!name) return '';
  return name.replace(/\s+\d{8,12}(?=\s*\(|\s*$)/g, '').trim();
}

@Pipe({ name: 'cleanName', standalone: true })
export class CleanNamePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return cleanProductName(value);
  }
}

/**
 * FR-01 display rule: Bangladeshi lakh grouping — 209990 renders as "2,09,990".
 * Prices are always server-computed; this pipe only formats.
 */
@Pipe({ name: 'bdt', standalone: true })
export class BdtPipe implements PipeTransform {
  transform(value: number | null | undefined, withSymbol = true): string {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return '';
    }

    const rounded = Math.round(Number(value) * 100) / 100;
    const [whole, decimals] = rounded.toFixed(2).split('.');
    const sign = whole.startsWith('-') ? '-' : '';
    const digits = whole.replace('-', '');

    const lastThree = digits.slice(-3);
    const rest = digits.slice(0, -3);
    const grouped = rest ? `${rest.replace(/\B(?=(\d{2})+(?!\d))/g, ',')},${lastThree}` : lastThree;
    const fraction = decimals === '00' ? '' : `.${decimals}`;

    return `${sign}${withSymbol ? '৳' : ''}${grouped}${fraction}`;
  }
}

/**
 * Adds the `.reveal` class and flips it to `.is-in` the first time the element scrolls into
 * view. No-ops during SSR and for users who prefer reduced motion (CSS already handles that).
 */
@Directive({ selector: '[rtReveal]', standalone: true })
export class RevealDirective implements OnInit, OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);
  private observer?: IntersectionObserver;

  ngOnInit(): void {
    const element = this.host.nativeElement as HTMLElement;
    element.classList.add('reveal');

    if (typeof IntersectionObserver === 'undefined') {
      element.classList.add('is-in');
      return;
    }

    this.observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            element.classList.add('is-in');
            this.observer?.disconnect();
          }
        }
      },
      { threshold: 0.12, rootMargin: '0px 0px -40px 0px' }
    );

    this.observer.observe(element);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}

const STATUS_LABELS: Record<string, string> = {
  Placed: 'Placed',
  Confirmed: 'Confirmed',
  Packed: 'Packed',
  Shipped: 'Shipped',
  Delivered: 'Delivered',
  Cancelled: 'Cancelled',
  Returned: 'Returned',
  Pending: 'Pending',
  Paid: 'Paid',
  Failed: 'Failed',
  Refunded: 'Refunded'
};

export function statusLabel(status: string): string {
  return STATUS_LABELS[status] ?? status;
}

export function statusTone(status: string): 'ok' | 'warn' | 'rust' | 'neutral' {
  switch (status) {
    case 'Delivered':
    case 'Paid':
      return 'ok';
    case 'Cancelled':
    case 'Failed':
    case 'Refunded':
      return 'rust';
    case 'Shipped':
    case 'Packed':
      return 'warn';
    case 'Placed':
    case 'Confirmed':
    case 'Pending':
      return 'warn';
    default:
      return 'neutral';
  }
}
