import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';

export const SITE_NAME = 'Rocky Telecom';
const DEFAULT_TITLE = 'Rocky Telecom — Phones & Electronics, Gaibandha';
const DEFAULT_DESCRIPTION =
  'Genuine phones, laptops, tablets and accessories at Rocky Telecom, Sadullapur, Gaibandha. Official warranty, EMI, home delivery and showroom pickup.';

export interface SeoConfig {
  /** Page title; the site name is appended unless it is already present. */
  title: string;
  description?: string | null;
  /** Path used for the canonical URL and og:url (defaults to the current path, without the query). */
  path?: string;
  /** Absolute image URL for social sharing. */
  image?: string | null;
  type?: 'website' | 'product' | 'article';
  noindex?: boolean;
  /** One or more schema.org objects, written as JSON-LD. */
  jsonLd?: object | object[] | null;
}

/**
 * Per-route search and sharing metadata (CONVENTIONS §5): title, description, canonical, Open Graph,
 * robots and JSON-LD. Pages call {@link set} once their data has loaded; the app shell calls
 * {@link reset} on every navigation so a page never inherits the previous page's tags.
 * Runs during SSR too, so crawlers see the tags in the first HTML response.
 */
@Injectable({ providedIn: 'root' })
export class SeoService {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly doc = inject(DOCUMENT);

  /** Absolute origin of the site (the request host during SSR). */
  origin(): string {
    return this.doc.location?.origin ?? '';
  }

  /** Back to site defaults; called on every navigation. */
  reset(path: string, noindex: boolean): void {
    this.set({ title: DEFAULT_TITLE, description: DEFAULT_DESCRIPTION, path, noindex, jsonLd: null }, true);
  }

  set(config: SeoConfig, isDefault = false): void {
    const title = isDefault || config.title.includes(SITE_NAME) ? config.title : `${config.title} | ${SITE_NAME}`;
    const description = (config.description ?? DEFAULT_DESCRIPTION).replace(/\s+/g, ' ').trim().slice(0, 300);
    const path = config.path ?? this.currentPath();
    const url = `${this.origin()}${path}`;

    this.title.setTitle(title);
    this.meta.updateTag({ name: 'description', content: description });

    this.meta.updateTag({ property: 'og:site_name', content: SITE_NAME });
    this.meta.updateTag({ property: 'og:title', content: title });
    this.meta.updateTag({ property: 'og:description', content: description });
    this.meta.updateTag({ property: 'og:type', content: config.type ?? 'website' });
    this.meta.updateTag({ property: 'og:url', content: url });

    if (config.image) {
      this.meta.updateTag({ property: 'og:image', content: config.image });
      this.meta.updateTag({ name: 'twitter:card', content: 'summary_large_image' });
    } else {
      this.meta.removeTag("property='og:image'");
      this.meta.updateTag({ name: 'twitter:card', content: 'summary' });
    }

    if (config.noindex) {
      this.meta.updateTag({ name: 'robots', content: 'noindex, nofollow' });
    } else {
      this.meta.removeTag("name='robots'");
    }

    this.setCanonical(url);
    this.setJsonLd(config.jsonLd ?? null);
  }

  private currentPath(): string {
    const path = this.doc.location?.pathname ?? '/';
    return path || '/';
  }

  private setCanonical(url: string): void {
    const head = this.doc.head;
    let link = head.querySelector<HTMLLinkElement>('link[rel="canonical"]');

    if (!link) {
      link = this.doc.createElement('link');
      link.setAttribute('rel', 'canonical');
      head.appendChild(link);
    }

    link.setAttribute('href', url);
  }

  private setJsonLd(data: object | object[] | null): void {
    const head = this.doc.head;
    head.querySelectorAll('script[data-rt-jsonld]').forEach((node) => node.remove());

    if (!data) {
      return;
    }

    const items = Array.isArray(data) ? data : [data];

    for (const item of items) {
      const script = this.doc.createElement('script');
      script.setAttribute('type', 'application/ld+json');
      script.setAttribute('data-rt-jsonld', '');
      // "<" is escaped so catalogue text such as "</script>" can never break out of the tag.
      script.textContent = JSON.stringify(item).replace(/</g, '\\u003c');
      head.appendChild(script);
    }
  }
}
