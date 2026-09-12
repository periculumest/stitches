import { test, expect } from './fixtures';

test('retention policy is public and explains deletion and backup expiry', async ({ page }) => {
  await page.context().clearCookies();
  await page.goto('/data-retention');
  await expect(page.getByRole('heading', { name: 'Data retention & deletion' })).toBeVisible();
  await expect(page.getByText(/latest daily archive for up to 7 days/)).toBeVisible();
  await expect(page.getByText(/retry failed cleanup every 5 minutes/)).toBeVisible();
});

test('account deletion requires an explicit typed confirmation', async ({ page }) => {
  await page.goto('/');
  const accountMenu = page.getByRole('button', { name: /^Account menu for / });
  await accountMenu.click();
  await page.keyboard.press('Escape');
  await expect(accountMenu).toBeFocused();
  await expect(accountMenu).toHaveAttribute('aria-expanded', 'false');
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(accountMenu).toBeVisible();
  await accountMenu.click();
  await expect(page.getByRole('group', { name: 'Account actions' })).toBeInViewport();
  await page.getByRole('button', { name: 'Account & data', exact: true }).click();
  const dialog = page.getByRole('dialog');
  const remove = dialog.getByRole('button', { name: 'Delete my account' });
  await expect(remove).toBeDisabled();
  await dialog.getByLabel('Type DELETE to confirm').fill('delete');
  await expect(remove).toBeDisabled();
  await dialog.getByLabel('Type DELETE to confirm').fill('DELETE');
  await expect(remove).toBeEnabled();
  await expect(dialog.getByRole('link', { name: /retention and deletion policy/ })).toHaveAttribute('href', '/data-retention');
  await dialog.getByRole('button', { name: 'Close dialog' }).click();
  await expect(dialog).not.toBeVisible();
});

test('project deletion explains the consequences and removes its last pattern', async ({ page, request }) => {
  const response = await request.post('/api/projects/sample');
  expect(response.ok()).toBeTruthy();
  const project = await response.json();
  const name = `Deletion check ${project.id}`;
  const renamed = await request.post(`/api/projects/${project.id}/commands`, { data: { revision: project.revision, kind: 'rename', name } });
  expect(renamed.ok()).toBeTruthy();
  await page.goto('/');
  page.once('dialog', async dialog => {
    expect(dialog.message()).toContain('retained backup archives');
    expect(dialog.message()).toContain('last project using the pattern');
    await dialog.accept();
  });
  await page.getByRole('button', { name: `Delete ${name}`, exact: true }).click();
  await expect(page.getByRole('button', { name: `Delete ${name}`, exact: true })).toHaveCount(0);
  expect((await request.get(`/api/projects/${project.id}`)).status()).toBe(404);
  expect((await request.get(`/api/patterns/${project.patternId}`)).status()).toBe(404);
});
