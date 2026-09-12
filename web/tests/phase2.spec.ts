import { test, expect } from './fixtures';

test('two browsers merge disjoint progress and refresh on focus and the active timer', async ({ page, browser, request }) => {
  await page.clock.install();
  const p = await (await request.post('/api/projects/sample?size=20')).json();
  await request.post(`/api/projects/${p.id}/commands`, { data: { revision: 0, kind: 'rename', name: 'Two devices' } });
  const second = await browser.newContext({ storageState: await page.context().storageState() });
  const other = await second.newPage();
  try {
    for (const tab of [page, other]) { await tab.goto('/'); await tab.getByRole('button', { name: 'Open Two devices', exact: true }).click(); }
    const ids = p.data.stitches.slice(0, 2).map((s: { id: string }) => s.id);
    const write = (tab: typeof page, stitchId: string) => tab.evaluate(async ({ id, stitchId }) => {
      const { token } = await (await fetch('/api/antiforgery')).json();
      const response = await fetch(`/api/projects/${id}/progress`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token }, body: JSON.stringify({ requestId: crypto.randomUUID(), changes: [{ stitchId, complete: true }] }) });
      if (!response.ok) throw new Error(await response.text());
    }, { id: p.id, stitchId });
    await Promise.all([write(page, ids[0]), write(other, ids[1])]);
    expect((await (await request.get(`/api/projects/${p.id}`)).json()).completed).toHaveLength(2);
    for (const tab of [page, other]) { await tab.evaluate(() => window.dispatchEvent(new Event('focus'))); await expect(tab.locator('.workspace-progress')).toContainText('2 /'); }
    await page.bringToFront();
    await request.post(`/api/projects/${p.id}/progress`, { data: { requestId: crypto.randomUUID(), changes: [{ stitchId: ids[0], complete: false }] } });
    await page.clock.fastForward(31000);
    await expect(page.locator('.workspace-progress')).toContainText('1 /');
  } finally { await second.close(); }
});

test('blend editor, striped legend, component inventory and substitution work together', async ({ page, request }) => {
  const p = await (await request.post('/api/projects/sample?size=20')).json();
  await request.post(`/api/projects/${p.id}/commands`, { data: { revision: 0, kind: 'rename', name: 'Blend browser test' } });
  await page.goto('/'); await page.getByRole('button', { name: 'Open Blend browser test', exact: true }).click();
  await page.locator('.key-edit').first().click();
  await page.getByRole('button', { name: 'Add blend component' }).click();
  await page.getByRole('button', { name: 'Add blend component' }).click();
  const editor = page.getByRole('dialog');
  await editor.getByRole('combobox', { name: 'Thread 1', exact: true }).selectOption('310');
  await editor.getByRole('combobox', { name: 'Thread 2', exact: true }).selectOption('321');
  await editor.getByRole('combobox', { name: 'Thread 3', exact: true }).selectOption('500');
  await editor.getByLabel('Strands', { exact: true }).nth(0).fill('1');
  await editor.getByLabel('Strands', { exact: true }).nth(2).fill('2');
  await page.getByRole('button', { name: 'Save definition' }).click();
  await expect(page.locator('.save-status')).toContainText('Saved');
  const key = page.locator('.key-entry').first();
  await expect(key).toContainText('310 (1 strands) + 321 + 500 (2 strands)');
  expect(await key.locator('.symbol-swatch').evaluate(el => getComputedStyle(el).backgroundImage)).toContain('linear-gradient');
  await page.getByRole('button', { name: 'Threads', exact: true }).click();
  const label = page.getByLabel(new RegExp(`Replacement for symbol .* component 321`)).first();
  await label.selectOption('White'); await expect(page.locator('.save-status')).toContainText('Saved');
  const saved = await (await request.get(`/api/projects/${p.id}`)).json();
  const definition = saved.data.definitions[0];
  expect(definition.components).toHaveLength(3); expect(definition.components[1].strandCount).toBeNull();
  expect(saved.substitutions[definition.components[1].id]).toBe('White');
  expect(saved.substitutions[definition.components[0].id]).toBeUndefined();
  await expect(page.locator('.material-row').filter({ hasText: 'DMC White' })).toBeVisible();
  await label.selectOption(''); await expect(page.locator('.save-status')).toContainText('Saved');
  await page.screenshot({ path: '../artifacts/phase2-blend.png', fullPage: true });
});

test('sign-in gate and logout clear the private workspace', async ({ page, browser }) => {
  const anonymous = await browser.newContext({ storageState: { cookies: [], origins: [] } }); const tab = await anonymous.newPage();
  try {
    await tab.goto('/'); await expect(tab.getByRole('link', { name: /Continue with Google/ })).toBeVisible();
    await expect(tab.getByRole('alert')).toHaveCount(0);
    await expect(tab.getByRole('heading', { name: 'My projects' })).toHaveCount(0);
    await tab.screenshot({ path: '../artifacts/phase2-signin.png', fullPage: true });
  } finally { await anonymous.close(); }
  await page.goto('/'); await page.getByRole('button', { name: /^Account menu for / }).click(); await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('link', { name: /Continue with Google/ })).toBeVisible();
  expect(await page.evaluate(async () => (await fetch('/api/projects')).status)).toBe(401);
});
