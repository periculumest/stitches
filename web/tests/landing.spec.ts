import { test, expect } from '@playwright/test';

test.use({ storageState: { cookies: [], origins: [] } });

test('public landing introduces the workspace and offers a local stitching preview', async ({ page }) => {
  const writes: string[] = [];
  page.on('request', request => { if (request.method() === 'POST') writes.push(request.url()); });
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('A little less counting.');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Skip to content', exact: true })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('main')).toBeFocused();
  await expect(page.getByRole('heading', { name: 'My projects', exact: true })).toHaveCount(0);
  const progress = page.getByRole('progressbar', { name: 'Preview stitches completed' });
  await expect(progress).toHaveAttribute('aria-valuenow', '0');
  await page.getByRole('button', { name: 'Try marking stitches', exact: true }).click();
  await expect(progress).not.toHaveAttribute('aria-valuenow', '0');
  await expect(page.locator('.landing-preview-note')).toContainText('That’s progress.');
  await page.getByRole('button', { name: 'Undo preview stitches', exact: true }).click();
  await expect(progress).toHaveAttribute('aria-valuenow', '0');
  expect(writes).toEqual([]);
  await page.getByRole('link', { name: 'Take a look around', exact: true }).click();
  await expect(page).toHaveURL(/#how-it-works$/);
  await expect(page.getByRole('heading', { name: 'Less keeping track. More getting lost in it.' })).toBeInViewport();
  await page.screenshot({ path: '../artifacts/landing-desktop.png', fullPage: true });
});

test('landing fits a phone and its login links use the Google sign-in flow', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  await expect(page.getByRole('link', { name: 'Continue with Google', exact: true })).toBeVisible();
  await expect(page.locator('.landing-header .brand > span:last-child')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  const logins = page.locator('a[href="/auth/google"]');
  await expect(logins).toHaveCount(4);
  await page.getByRole('button', { name: 'Try marking stitches', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Undo preview stitches', exact: true })).toBeVisible();
  await page.screenshot({ path: '../artifacts/landing-mobile.png', fullPage: true });
  // Verify navigation without depending on Google's external sign-in UI.
  await page.route('**/auth/google', route => route.fulfill({ contentType: 'text/plain', body: 'Google sign-in handoff' }));
  await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'Log in', exact: true }).click();
  await expect(page).toHaveURL(/\/auth\/google$/);
});

test('sign-in failures and expired sessions remain visible on the landing page', async ({ page }) => {
  await page.goto('/?signin=failed');
  await expect(page.getByRole('alert')).toContainText('Google sign-in was cancelled or could not be completed.');
  await expect(page.getByRole('link', { name: 'Continue with Google', exact: true })).toHaveAttribute('href', '/auth/google');
  await page.evaluate(() => window.dispatchEvent(new Event('session-expired')));
  await expect(page.getByRole('alert')).toContainText('Your session expired. Sign in again to continue.');
});
