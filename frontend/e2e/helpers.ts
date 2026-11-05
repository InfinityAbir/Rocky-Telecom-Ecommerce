import { Page } from '@playwright/test';

export const API = process.env['E2E_API_URL'] ?? 'http://localhost:5129/api/v1';

/** Dev seed account from appsettings.Development.json (never exists in production). */
export const ADMIN = { phone: '01700000000', password: 'Rocky#2026' };

/** Signs in through the API and stores the session the way the app does, so tests skip the login form. */
export async function signInAsAdmin(page: Page): Promise<void> {
  const response = await page.request.post(`${API}/auth/login`, { data: ADMIN });
  const body = (await response.json()) as { content: { accessToken: string; user: unknown } };

  await page.addInitScript(
    ([token, user]) => {
      localStorage.setItem('rt_token', token as string);
      localStorage.setItem('rt_user', JSON.stringify(user));
    },
    [body.content.accessToken, body.content.user]
  );
}

/** Keeps the one-time help greeting out of screenshots and click paths. */
export async function skipGreeting(page: Page): Promise<void> {
  await page.addInitScript(() => sessionStorage.setItem('rt_help_greeted', '1'));
}
