import { test as base, expect } from '@playwright/test';

export const test = base.extend({
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
