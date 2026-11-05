import { expect, test } from '@playwright/test';
import { skipGreeting } from './helpers';

test.beforeEach(async ({ page }) => {
  await skipGreeting(page);
});

test('bottom nav has the five PRD items, tap targets are at least 44px', async ({ page }) => {
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: 'Mobile' });

  await expect(nav).toBeVisible();
  const items = nav.locator('a, button');
  await expect(items).toHaveCount(5);

  const labels = (await items.allInnerTexts()).map((text) => text.replace(/[^A-Za-z-]/g, '').toLowerCase());
  expect(labels).toEqual(['home', 'offer', 'category', 'pre-order', 'profile']);

  for (let i = 0; i < 5; i++) {
    const box = await items.nth(i).boundingBox();
    expect(box!.height).toBeGreaterThanOrEqual(44);
    expect(box!.width).toBeGreaterThanOrEqual(44);
  }
});

test('category drawer opens from the bottom nav and leads to a shelf', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('navigation', { name: 'Mobile' }).getByRole('button', { name: /Category/ }).click();

  const sheet = page.getByRole('dialog', { name: 'Browse categories' });
  await expect(sheet).toBeVisible();

  await sheet.getByRole('button', { name: /Smartphones/ }).click();
  await sheet.getByRole('link', { name: /See all Smartphones/ }).click();
  await expect(page).toHaveURL(/\/category\/smartphones/);
  await expect(sheet).toBeHidden();
});

test('search stays reachable and nothing scrolls sideways at phone width', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('search').first()).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow).toBeLessThanOrEqual(1);
});

test('product page purchase bar sits above the bottom nav', async ({ page }) => {
  await page.goto('/product/iphone-15-pro');
  await page.evaluate(() => window.scrollTo(0, 1500));

  const bar = page.locator('.buybar');
  await expect(bar).toBeVisible();
  const barBox = await bar.boundingBox();
  const navBox = await page.getByRole('navigation', { name: 'Mobile' }).boundingBox();
  expect(barBox!.y + barBox!.height).toBeLessThanOrEqual(navBox!.y + 1);
});
