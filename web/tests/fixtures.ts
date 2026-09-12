import { test as base, expect } from '@playwright/test';

export const test = base.extend({
  page: async ({ page, request }, use, testInfo) => {
    // Other workflows use an account that has already dismissed onboarding. Tour tests exercise it explicitly.
    if (!testInfo.file.endsWith('guided-tour.spec.ts')) {
      const state = await (await request.get('/api/beta/state')).json();
      expect((await request.post('/api/beta/onboarding', { data: { version: state.onboardingVersion } })).ok()).toBeTruthy();
    }
    await use(page);
  },
  request: async ({ playwright, baseURL, storageState }, use) => {
    const bootstrap = await playwright.request.newContext({ baseURL, storageState });
    const response = await bootstrap.get('/api/antiforgery');
    expect(response.ok(), 'Authenticated test cookie and CSRF bootstrap').toBeTruthy();
    const { token } = await response.json();
    const request = await playwright.request.newContext({ baseURL, storageState: await bootstrap.storageState(), extraHTTPHeaders: { 'X-CSRF-TOKEN': token } });
    await bootstrap.dispose(); await use(request); await request.dispose();
  },
});
export { expect };
