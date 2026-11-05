import { expect, test } from '@playwright/test';
import { signInAsAdmin, skipGreeting } from './helpers';

test.beforeEach(async ({ page }) => {
  await skipGreeting(page);
  await signInAsAdmin(page);
});

test('anonymous visitors are sent to the login page for the admin area', async ({ browser }) => {
  const page = await (await browser.newContext()).newPage();
  await page.goto('/admin');

  await expect(page).toHaveURL(/\/login/);
});

test('dashboard loads for an admin', async ({ page }) => {
  await page.goto('/admin');

  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
  await expect(page.getByRole('link', { name: /Products/ }).first()).toBeVisible();
});

test('product editor opens an existing phone with its spec sheet and publish state', async ({ page }) => {
  await page.goto('/admin/products');
  await page.getByPlaceholder('Search name or slug').fill('iPhone 15 Pro Max');
  await page.getByPlaceholder('Search name or slug').press('Enter');
  await page.getByRole('link', { name: 'Edit' }).first().click();

  await expect(page.getByRole('heading', { level: 1 })).toContainText('iPhone 15 Pro Max');
  await expect(page.getByRole('heading', { name: 'Variants' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Specifications' })).toBeVisible();
  await expect(page.getByText('Published', { exact: true }).first()).toBeVisible();
});

test('CSV import reports problems by row and column and writes nothing', async ({ page }) => {
  await page.goto('/admin/products');

  const csv = 'name,slug,brand,category,sku,regular_price,stock\nGhost,ghost-e2e,NoSuchBrand,smartphones,RT-E2E-1,abc,1\n';
  await page.locator('input[type=file][accept*=csv]').setInputFiles({
    name: 'broken.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from(csv)
  });

  await expect(page.getByText(/Nothing was imported/)).toBeVisible();
  await expect(page.getByRole('cell', { name: 'brand', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'regular_price', exact: true })).toBeVisible();
});

test('categories and brands screens list the catalogue', async ({ page }) => {
  await page.goto('/admin/categories');
  await expect(page.getByRole('cell', { name: 'Smartphones' }).first()).toBeVisible();

  await page.goto('/admin/brands');
  await expect(page.getByRole('cell', { name: 'Samsung' }).first()).toBeVisible();
});
