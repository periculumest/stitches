import { test, expect } from './fixtures';

const announcement = { id: 'notification-fixture', title: 'A little update for your next stitching session', message: 'The guided tour now walks you through your first pattern. Try the little garden sampler and let us know how it feels.', linkText: 'See what’s new', linkUrl: '/whats-new', dismissible: true };

test('announcement connectivity errors retry announcements, never tour preferences', async ({ page }) => {
  let offline = true, hideFails = true;
  const tourRequests: string[] = [];
  page.on('request', req => { if (req.url().endsWith('/beta/onboarding')) tourRequests.push(req.method()); });
  await page.route('**/api/beta/announcement', route => offline ? route.abort('failed') : route.fulfill({ json: announcement }));
  await page.route(`**/api/beta/announcements/${announcement.id}/dismiss`, route => hideFails ? route.abort('failed') : route.fulfill({ status: 204 }));
  await page.goto('/');
  const loadError = page.getByRole('alert', { name: 'Updates are temporarily unavailable' });
  await expect(loadError).toBeVisible();
  await expect(page.getByText('Dismiss tour again')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Save tour preference' })).toHaveCount(0);
  offline = false;
  await loadError.getByRole('button', { name: 'Retry updates' }).click();
  await expect(loadError).toHaveCount(0);
  await expect(page.locator('.beta-announcement')).toContainText(announcement.title);
  await page.getByRole('button', { name: 'Dismiss announcement' }).click();
  const hideError = page.getByRole('alert', { name: 'This announcement couldn’t be hidden' });
  await expect(hideError).toBeVisible();
  await expect(page.locator('.beta-announcement')).toBeVisible();
  hideFails = false;
  await hideError.getByRole('button', { name: 'Retry dismissal' }).click();
  await expect(hideError).toHaveCount(0);
  await expect(page.locator('.beta-announcement')).toHaveCount(0);
  expect(tourRequests).toEqual([]);
});

test('tour-save failures stay separate from failed updates and retry without reopening the tour', async ({ page }) => {
  let updatesFail = true, tourFails = true;
  await page.route('**/api/beta/announcement', route => updatesFail ? route.abort('failed') : route.fulfill({ status: 204 }));
  await page.route('**/api/beta/onboarding', route => tourFails ? route.abort('failed') : route.continue());
  await page.goto('/');
  await page.getByRole('button', { name: 'Guided tour', exact: true }).click();
  await page.getByRole('complementary', { name: 'Guided tour' }).getByRole('button', { name: 'End tour' }).click();
  const tourError = page.getByRole('alert', { name: 'Tour preference wasn’t saved' });
  await expect(tourError).toBeVisible();
  await expect(page.getByRole('complementary', { name: 'Guided tour' })).toHaveCount(0);
  updatesFail = false;
  await page.getByRole('button', { name: 'Retry updates' }).click();
  await expect(page.getByRole('alert', { name: 'Updates are temporarily unavailable' })).toHaveCount(0);
  await expect(tourError).toBeVisible();
  tourFails = false;
  await tourError.getByRole('button', { name: 'Save tour preference' }).click();
  await expect(tourError).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('complementary', { name: 'Guided tour' })).toHaveCount(0);
});

test('a late announcement poll cannot restore an announcement after it is dismissed', async ({ page }) => {
  await page.clock.install();
  let polls = 0;
  let release: () => void = () => {};
  const pendingPoll = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/beta/announcement', async route => {
    polls++;
    if (polls > 1) await pendingPoll;
    await route.fulfill({ json: announcement });
  });
  await page.route(`**/api/beta/announcements/${announcement.id}/dismiss`, route => route.fulfill({ status: 204 }));
  await page.goto('/');
  await expect(page.locator('.beta-announcement')).toBeVisible();
  await page.clock.fastForward(60000);
  await expect.poll(() => polls).toBe(2);
  await page.getByRole('button', { name: 'Dismiss announcement' }).click();
  await expect(page.locator('.beta-announcement')).toHaveCount(0);
  const response = page.waitForResponse('**/api/beta/announcement');
  release(); await response;
  await expect(page.locator('.beta-announcement')).toHaveCount(0);
});

test('banners and notifications fit phones and preserve error details and recovery controls', async ({ page }) => {
  await page.route('**/api/beta/announcement', route => route.fulfill({ json: announcement }));
  await page.route('**/api/projects', route => route.abort('failed'));
  await page.goto('/');
  const error = page.getByRole('alert', { name: 'Connection interrupted' });
  await expect(error).toBeVisible();
  await expect(error.locator('.app-notice-details p')).not.toBeVisible();
  await error.locator('summary').click();
  await expect(error.locator('.app-notice-details p')).toContainText('NETWORK_FAILURE');
  await expect(error.getByRole('button', { name: 'Copy error details' })).toBeVisible();
  await error.locator('summary').click();
  await page.screenshot({ path: '../artifacts/notifications-desktop.png' });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(error.getByRole('button', { name: 'Retry connection' })).toBeVisible();
  expect(await error.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  expect(await page.locator('.beta-announcement').evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  expect(await page.locator('.beta-tools').evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  await page.screenshot({ path: '../artifacts/notifications-mobile.png' });
  await page.unroute('**/api/projects');
  await error.getByRole('button', { name: 'Retry connection' }).click();
  await expect(error).toHaveCount(0);
  await page.getByRole('button', { name: 'Import a pattern', exact: true }).click();
  await page.getByRole('button', { name: 'Explore with the little garden sampler' }).click();
  const success = page.getByRole('status', { name: 'All set' });
  await expect(success).toContainText('Your little garden is ready');
  expect(await success.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  await page.screenshot({ path: '../artifacts/notification-success-mobile.png' });
  await page.clock.install();
  await success.getByRole('button', { name: 'Dismiss notification' }).focus();
  await page.clock.fastForward(7000);
  await expect(success).toBeVisible();
  await page.keyboard.press('Enter');
  await expect(success).toHaveCount(0);
});
