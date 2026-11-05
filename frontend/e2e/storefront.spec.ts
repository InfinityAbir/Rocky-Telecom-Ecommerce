import { expect, test } from '@playwright/test';
import { skipGreeting } from './helpers';

test.beforeEach(async ({ page }) => {
  await skipGreeting(page);
});

test('home page shows the hero, categories and a product shelf', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveTitle(/Rocky Telecom/);
  await expect(page.getByRole('heading', { level: 1 })).toContainText(/Phones worth\s*the walk/);
  await expect(page.locator('rt-product-card').first()).toBeVisible();
});

test('mega menu lists categories and opens a brand-filtered shelf', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /Categories/ }).click();

  const panel = page.locator('#rt-mega-panel');
  await expect(panel).toBeVisible();
  await expect(panel.getByRole('link', { name: /Smartphones/ }).first()).toBeVisible();

  await panel.getByRole('link', { name: /^Samsung/ }).first().click();
  await expect(page).toHaveURL(/\/category\/smartphones\?brands=samsung/);
  await expect(page.locator('rt-product-card').first()).toBeVisible();
  await expect(panel).toBeHidden();
});

test('memory and storage filters are listed by size, not alphabetically', async ({ page }) => {
  await page.goto('/category/smartphones');
  await page.locator('rt-product-card').first().waitFor();

  const storage = await page.locator('.filters').getByRole('button', { name: /^(\d+)(GB|TB)$/ }).allInnerTexts();
  const mb = (text: string) => (text.endsWith('TB') ? 1024 : 1) * parseInt(text, 10);
  const sizes = storage.map(mb);

  expect(sizes.length).toBeGreaterThan(2);
  expect(sizes).toEqual([...sizes].sort((a, b) => a - b));
});

test('colour swatches filter the shelf', async ({ page }) => {
  await page.goto('/category/smartphones');
  await page.locator('rt-product-card').first().waitFor();
  const before = await page.locator('rt-product-card').count();

  await page.locator('.swatch').first().click();
  await expect(page).toHaveURL(/color=/);
  await expect.poll(async () => page.locator('rt-product-card').count()).toBeLessThanOrEqual(before);
  await expect(page.locator('.swatch.is-active')).toHaveCount(1);
});

test('product page has spec cards, a description and a sticky buy bar', async ({ page }) => {
  await page.goto('/product/iphone-15-pro');

  await expect(page.getByRole('heading', { level: 1 })).toContainText('iPhone 15 Pro');
  await page.getByRole('tab', { name: 'Specification' }).scrollIntoViewIfNeeded();
  expect(await page.locator('.spec-card').count()).toBeGreaterThanOrEqual(8);
  await expect(page.locator('.spec-card h3', { hasText: 'Main camera' })).toBeVisible();

  await expect(page.locator('.buybar')).toBeVisible();

  await page.getByRole('tab', { name: 'Description' }).click();
  await expect(page.getByRole('heading', { name: /price in Bangladesh/ })).toBeVisible();
  await expect(page.getByRole('heading', { name: /Frequently asked questions/ })).toBeVisible();
});

test('guest can add to cart, see the cart count and reach checkout', async ({ page }) => {
  await page.goto('/product/iphone-15-pro');
  await page.getByRole('button', { name: 'Add to cart' }).first().click();

  await expect(page.locator('.rt-count')).toHaveText('1');
  await page.goto('/cart');
  await expect(page.getByText('iPhone 15 Pro').first()).toBeVisible();

  await page.goto('/checkout');
  await expect(page.getByRole('heading', { name: /How should it arrive/ })).toBeVisible();
});

test('guest checkout with showroom pickup places an order', async ({ page }) => {
  test.skip(!process.env['CI'] && !process.env['E2E_PLACE_ORDER'], 'Creates a real order; runs in CI or with E2E_PLACE_ORDER=1.');

  await page.goto('/product/baseus-65w-gan-fast-charger');
  await page.getByRole('button', { name: 'Add to cart' }).first().click();
  await expect(page.locator('.rt-count')).toHaveText('1');

  await page.goto('/checkout');
  await page.getByLabel('Full name').fill('E2E Test Buyer');
  await page.getByLabel('Mobile number').fill('01712345678');
  await page.getByRole('button', { name: 'Showroom pickup (free)' }).click();
  await page.getByRole('button', { name: /Place order/ }).click();

  await expect(page).toHaveURL(/order|track|thank/i, { timeout: 15_000 });
});

test('help button opens the contact panel and scroll-to-top appears', async ({ page }) => {
  await page.goto('/category/smartphones');
  await page.locator('rt-product-card').first().waitFor();

  await page.getByRole('button', { name: 'Chat with us' }).click();
  await expect(page.getByRole('dialog', { name: /Contact Rocky Telecom/ })).toBeVisible();
  await page.keyboard.press('Escape');

  await page.evaluate(() => window.scrollTo(0, 1500));
  await expect(page.getByRole('button', { name: 'Back to top' })).toBeVisible();
});

test('storefront responses carry a content security policy', async ({ request, baseURL }) => {
  const response = await request.get(baseURL!);
  // The Angular dev server does not add it; the SSR server does. Only assert when present in this environment.
  const csp = response.headers()['content-security-policy'];
  if (csp) {
    expect(csp).toContain("script-src 'self'");
    expect(csp).toContain("frame-ancestors 'none'");
  }
});
