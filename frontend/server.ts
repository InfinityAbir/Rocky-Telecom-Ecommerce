import { APP_BASE_HREF } from '@angular/common';
import { CommonEngine } from '@angular/ssr';
import express from 'express';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';
import bootstrap from './src/main.server';

interface SitemapData {
  products: { slug: string; lastModified: string }[];
  categories: string[];
  brands: string[];
  offers: string[];
  blogs?: string[];
}

const STATIC_PATHS = [
  '/',
  '/shop',
  '/brands',
  '/offer',
  '/pre-order',
  '/blogs',
  '/shop-location',
  '/about',
  '/faq',
  '/policy/terms',
  '/policy/privacy',
  '/policy/refund',
  '/policy/warranty',
  '/policy/delivery',
  '/policy/emi',
  '/policy/cancellation',
  '/policy/exchange'
];

const xmlEscape = (value: string): string =>
  value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;');

/** Builds sitemap.xml from the API's public, indexable URLs only. */
function buildSitemap(origin: string, data: SitemapData): string {
  const entry = (path: string, lastModified?: string): string =>
    `  <url><loc>${xmlEscape(origin + path)}</loc>${lastModified ? `<lastmod>${xmlEscape(lastModified.slice(0, 10))}</lastmod>` : ''}</url>`;

  const urls = [
    ...STATIC_PATHS.map((path) => entry(path)),
    ...data.categories.map((slug) => entry(`/category/${slug}`)),
    ...data.brands.map((slug) => entry(`/brands/${slug}`)),
    ...data.offers.map((slug) => entry(`/offer/${slug}`)),
    ...(data.blogs ?? []).map((slug) => entry(`/blogs/${slug}`)),
    ...data.products.map((product) => entry(`/product/${product.slug}`, product.lastModified))
  ];

  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls.join('\n')}\n</urlset>\n`;
}

/**
 * Origin the browser calls the API on. Empty in Docker/production (nginx serves /api on the site's own
 * origin); the Kestrel dev URL otherwise. Only this origin is added to the CSP connect/img sources.
 */
const PUBLIC_API_ORIGIN = process.env['PUBLIC_API_ORIGIN'] ?? 'http://localhost:5129';

/** Content-Security-Policy for the storefront (PRD §NFR: HTTPS, HSTS, CSP). No inline or remote scripts. */
function contentSecurityPolicy(): string {
  const api = PUBLIC_API_ORIGIN ? ` ${PUBLIC_API_ORIGIN}` : '';

  return [
    "default-src 'self'",
    "script-src 'self'",
    // Angular writes component styles into <style> tags at runtime, so styles need 'unsafe-inline'; scripts do not.
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
    "font-src 'self' https://fonts.gstatic.com",
    `img-src 'self' data: blob: https:${api}`,
    `connect-src 'self'${api}`,
    'frame-src https://www.openstreetmap.org',
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'"
  ].join('; ');
}

/** Security headers for every storefront response. */
function securityHeaders(req: express.Request, res: express.Response, next: express.NextFunction): void {
  res.setHeader('Content-Security-Policy', contentSecurityPolicy());
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('X-Frame-Options', 'DENY');
  res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin');
  res.setHeader('Permissions-Policy', 'camera=(), microphone=(), payment=(), geolocation=(self)');
  res.setHeader('Cross-Origin-Opener-Policy', 'same-origin');

  // Only meaningful (and only honoured) over HTTPS; nginx terminates TLS and says so in X-Forwarded-Proto.
  if (req.secure || req.headers['x-forwarded-proto'] === 'https') {
    res.setHeader('Strict-Transport-Security', 'max-age=31536000; includeSubDomains');
  }

  next();
}

// The Express app is exported so that it can be used by serverless Functions.
export function app(): express.Express {
  const server = express();
  server.disable('x-powered-by');
  server.set('trust proxy', true);
  server.use(securityHeaders);

  // Liveness probe for Docker/nginx; never cached, no SSR cost.
  server.get('/healthz', (_req, res) => {
    res.setHeader('Cache-Control', 'no-store').type('text/plain').send('ok');
  });
  const serverDistFolder = dirname(fileURLToPath(import.meta.url));
  const browserDistFolder = resolve(serverDistFolder, '../browser');
  const indexHtml = join(serverDistFolder, 'index.server.html');

  const commonEngine = new CommonEngine();

  server.set('view engine', 'html');
  server.set('views', browserDistFolder);

  // Public site origin used in sitemap URLs: SITE_URL in production, the request host otherwise.
  const siteOrigin = (req: express.Request): string =>
    (process.env['SITE_URL'] ?? `${req.protocol}://${req.headers.host}`).replace(/\/+$/, '');

  server.get('/robots.txt', (req, res) => {
    res
      .type('text/plain')
      .send(
        [
          'User-agent: *',
          'Allow: /',
          'Disallow: /admin',
          'Disallow: /cart',
          'Disallow: /checkout',
          'Disallow: /login',
          'Disallow: /account',
          'Disallow: /wishlist',
          'Disallow: /track',
          'Disallow: /search',
          'Disallow: /compare',
          `Sitemap: ${siteOrigin(req)}/sitemap.xml`,
          ''
        ].join('\n')
      );
  });

  server.get('/sitemap.xml', async (req, res) => {
    const origin = siteOrigin(req);
    const apiBase = process.env['API_BASE_URL'] ?? 'http://localhost:5129/api/v1';

    try {
      const response = await fetch(`${apiBase}/sitemap`);
      const envelope = (await response.json()) as { hasError: boolean; content: SitemapData | null };

      if (!response.ok || envelope.hasError || !envelope.content) {
        throw new Error(`Sitemap API returned ${response.status}`);
      }

      res.type('application/xml').send(buildSitemap(origin, envelope.content));
    } catch (error) {
      console.error('sitemap.xml failed', error);
      res.status(503).type('text/plain').send('Sitemap temporarily unavailable.');
    }
  });

  // Serve static files from /browser
  server.get('**', express.static(browserDistFolder, {
    maxAge: '1y',
    index: 'index.html',
  }));

  // All regular routes use the Angular engine
  server.get('**', (req, res, next) => {
    const { protocol, originalUrl, baseUrl, headers } = req;

    commonEngine
      .render({
        bootstrap,
        documentFilePath: indexHtml,
        url: `${protocol}://${headers.host}${originalUrl}`,
        publicPath: browserDistFolder,
        // The inlined-critical-CSS trick uses an inline onload handler, which the script-src CSP forbids.
        inlineCriticalCss: false,
        providers: [{ provide: APP_BASE_HREF, useValue: baseUrl }],
      })
      .then((html) => res.send(html))
      .catch((err) => next(err));
  });

  return server;
}

function run(): void {
  const port = process.env['PORT'] || 4000;

  // Start up the Node server
  const server = app();
  server.listen(port, () => {
    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

run();
