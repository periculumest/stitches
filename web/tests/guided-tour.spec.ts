import { test, expect } from './fixtures';
import type { Page } from '@playwright/test';

const guide = (page: Page) => page.getByRole('complementary', { name: 'Guided tour', exact: true });
const target = (page: Page, name: string) => page.locator(`[data-tour="${name}"].guided-tour-target`);
async function start(page: Page) {
  await page.goto('/'); await page.getByRole('button', { name: 'Guided tour', exact: true }).click();
  await expect(target(page, 'import')).toBeVisible();
}
async function sampler(page: Page) {
  await start(page); await target(page, 'import').click();
  await expect(guide(page)).toHaveAttribute('data-tour-stage', 'sampler');
  await expect(target(page, 'sampler')).toBeVisible();
  await target(page, 'sampler').click(); await expect(guide(page)).toHaveAttribute('data-tour-stage', 'inspect');
}

test('guided sampler walkthrough follows clicks and waits for a confirmed save', async ({ page, request }) => {
  const before = (await (await request.get('/api/projects')).json()).length;
  await sampler(page);
  expect((await (await request.get('/api/projects')).json()).length).toBe(before + 1);
  await expect(page.locator('.workspace-title')).toContainText('The little garden');
  await target(page, 'thread').click(); await expect(guide(page)).toHaveAttribute('data-tour-stage', 'complete');
  await expect(page.locator('.key-choice[aria-pressed=true]')).toHaveCount(1);
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/projects/*/progress', async route => { await pending; await route.continue(); });
  try {
    await target(page, 'complete').click();
    await expect(guide(page)).toHaveAttribute('data-tour-stage', 'saving');
    await expect(page.locator('.save-status')).toContainText('Saving');
    await expect(guide(page).getByRole('button', { name: 'Continue', exact: true })).toHaveCount(0);
    release(); await expect(guide(page)).toHaveAttribute('data-tour-stage', 'progress');
    await expect(page.locator('.save-status')).toContainText('Saved');
    await expect(target(page, 'progress')).toBeVisible();
    await guide(page).getByRole('button', { name: 'Continue', exact: true }).click();
    let submissions = 0; page.on('request', req => { if (req.url().endsWith('/api/beta/feedback') && req.method() === 'POST') submissions++; });
    await target(page, 'feedback').click();
    await expect(page.getByRole('dialog', { name: 'Send Feedback', exact: true })).toBeVisible();
    await expect(guide(page)).toHaveCount(0); expect(submissions).toBe(0);
    const state = await (await request.get('/api/beta/state')).json();
    await expect.poll(async () => (await (await request.get('/api/beta/state')).json()).completedOnboardingVersion).toBe(state.onboardingVersion);
    await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
    await page.reload(); await expect(guide(page)).toHaveCount(0);
  } finally { release(); }
});

test('sampler guidance works inside the native dialog on mobile and with a keyboard', async ({ page, request }) => {
  await page.setViewportSize({ width: 390, height: 844 }); await start(page);
  await guide(page).getByRole('button', { name: 'Focus highlighted control' }).click();
  await expect(target(page, 'import')).toBeFocused(); await page.keyboard.press('Enter');
  await expect(guide(page)).toHaveAttribute('data-tour-stage', 'sampler');
  expect(await guide(page).evaluate(el => el.closest('dialog')?.open)).toBe(true);
  await expect(target(page, 'sampler')).toBeInViewport();
  const ring = (await target(page, 'sampler').boundingBox())!, card = (await guide(page).boundingBox())!;
  expect(ring.y + ring.height <= card.y || card.y + card.height <= ring.y || ring.x + ring.width <= card.x || card.x + card.width <= ring.x).toBe(true);
  expect(await guide(page).evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  await page.screenshot({ path: '../artifacts/guided-tour-sampler-mobile.png' });
  await guide(page).getByRole('button', { name: 'Focus highlighted control' }).click();
  await expect(target(page, 'sampler')).toBeFocused(); await page.keyboard.press('Enter');
  await expect(guide(page)).toHaveAttribute('data-tour-stage', 'inspect');
  await guide(page).getByRole('button', { name: 'Focus highlighted control' }).click();
  await page.keyboard.press('Enter'); await expect(guide(page)).toHaveAttribute('data-tour-stage', 'complete');
  await guide(page).getByRole('button', { name: 'Skip marking stitches' }).click();
  await expect(guide(page)).toHaveAttribute('data-tour-stage', 'feedback');
  await guide(page).getByRole('button', { name: 'End tour' }).click();
  await expect(page.getByRole('button', { name: 'Guided tour', exact: true })).toBeFocused();
  const state = await (await request.get('/api/beta/state')).json();
  await expect.poll(async () => (await (await request.get('/api/beta/state')).json()).completedOnboardingVersion).toBe(state.onboardingVersion);
});

test('failed sampler creation stays on the sampler and closing the picker removes its highlight', async ({ page }) => {
  await start(page); await target(page, 'import').click();
  await page.route('**/api/projects/sample', route => route.fulfill({ status: 503, json: { error: 'Sampler temporarily unavailable' } }));
  await target(page, 'sampler').click(); await expect(page.getByRole('dialog')).toContainText('Sampler temporarily unavailable');
  await expect(guide(page)).toHaveAttribute('data-tour-stage', 'sampler');
  await page.getByRole('button', { name: 'Close dialog', exact: true }).click();
  await expect(guide(page)).toHaveAttribute('data-tour-stage', 'choose');
  await expect(target(page, 'import')).toBeVisible(); await expect(target(page, 'sampler')).toHaveCount(0);
  await guide(page).getByRole('button', { name: 'End tour' }).click(); await expect(page.locator('.guided-tour-target')).toHaveCount(0);
});

test('the updated tour appears for a previous tour version and pauses for unrelated dialogs', async ({ page, request }) => {
  const actual = await (await request.get('/api/beta/state')).json();
  await page.route('**/api/beta/state', route => route.fulfill({ json: { ...actual, completedOnboardingVersion: actual.onboardingVersion - 1 } }));
  await page.goto('/'); await expect(target(page, 'import')).toBeVisible();
  await page.getByRole('button', { name: 'Send Feedback', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Send Feedback', exact: true })).toBeVisible(); await expect(guide(page)).toHaveCount(0);
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(target(page, 'import')).toBeVisible();
  await guide(page).getByRole('button', { name: 'End tour' }).click(); await page.unroute('**/api/beta/state');
  await expect.poll(async () => (await (await request.get('/api/beta/state')).json()).completedOnboardingVersion).toBe(actual.onboardingVersion);
  await page.reload(); await expect(guide(page)).toHaveCount(0);
});
