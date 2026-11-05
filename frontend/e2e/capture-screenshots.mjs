// Regenerates the README screenshots from a running stack:
//   cd frontend && node e2e/capture-screenshots.mjs
// Needs the API on :5129 and the web app on :4200 (see start.bat).
import { chromium } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const out = resolve(here, '../../docs/screenshots');
const web = process.env.E2E_BASE_URL ?? 'http://localhost:4200';
const api = process.env.E2E_API_URL ?? 'http://localhost:5129/api/v1';
mkdirSync(out, { recursive: true });

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'chromium' });

async function shot(name, { path, viewport = { width: 1440, height: 900 }, admin = false, prepare, fullPage = false }) {
  const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
  const page = await context.newPage();

  await page.addInitScript(() => sessionStorage.setItem('rt_help_greeted', '1'));

  if (admin) {
    const login = await page.request.post(`${api}/auth/login`, { data: { phone: '01700000000', password: 'Rocky#2026' } });
    const body = await login.json();
    await page.addInitScript(([token, user]) => {
      localStorage.setItem('rt_token', token);
      localStorage.setItem('rt_user', JSON.stringify(user));
    }, [body.content.accessToken, body.content.user]);
  }

  await page.goto(`${web}${path}`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(1200);
  if (prepare) await prepare(page);
  await page.waitForTimeout(500);
  await page.screenshot({ path: `${out}/${name}.png`, fullPage });
  console.log('saved', name);
  await context.close();
}

await shot('home', { path: '/' });

await shot('mega-menu', {
  path: '/',
  prepare: async (page) => {
    await page.getByRole('button', { name: /Categories/ }).click();
    await page.waitForSelector('#rt-mega-panel');
  }
});

await shot('shop-filters', { path: '/category/smartphones' });

await shot('product', {
  path: '/product/iphone-15-pro',
  prepare: async (page) => {
    await page.evaluate(() => window.scrollTo(0, document.querySelector('.tabs').getBoundingClientRect().top + scrollY - 90));
  }
});

await shot('admin-dashboard', { path: '/admin', admin: true });

await shot('admin-products', {
  path: '/admin/products',
  admin: true
});

await shot('mobile-home', {
  path: '/',
  viewport: { width: 390, height: 844 },
  prepare: async (page) => {
    await page.getByRole('navigation', { name: 'Mobile' }).getByRole('button', { name: /Category/ }).click();
    await page.waitForSelector('.sheet');
  }
});

await browser.close();
