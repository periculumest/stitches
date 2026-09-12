import { test, expect } from './fixtures';
import type { Page } from '@playwright/test';

async function pointFor(page: Page, stitch: { x: number; y: number }) {
  const bounds = (await page.locator('.canvas-panel canvas').boundingBox())!;
  const cell = Math.min(28, (bounds.width - 90) / 20, (bounds.height - 90) / 20);
  return { x: bounds.x + (bounds.width - 20 * cell) / 2 + (stitch.x + .5) * cell,
    y: bounds.y + (bounds.height - 20 * cell) / 2 + (stitch.y + .5) * cell };
}

test('completed cells visibly fade on painting, after saving, and after reload', async ({ page, request }) => {
  const p = await (await request.post('/api/projects/sample?size=20')).json();
  await request.post(`/api/projects/${p.id}/commands`, { data: { revision: 0, kind: 'rename', name: 'Visible completion' } });
  const stitch = p.data.stitches[0];
  const open = async () => { await page.goto('/'); await page.getByRole('button', { name: 'Open Visible completion', exact: true }).click(); };
  await open();
  const contrast = async () => {
    const point = await pointFor(page, stitch);
    return page.locator('.canvas-panel canvas').evaluate((el, point) => {
      const canvas = el as HTMLCanvasElement, bounds = canvas.getBoundingClientRect();
      const cell = Math.min(28, (bounds.width - 90) / 20, (bounds.height - 90) / 20);
      const scale = canvas.width / bounds.width, side = Math.max(1, Math.floor((cell - 4) * scale));
      const pixels = canvas.getContext('2d')!.getImageData(Math.floor((point.x - bounds.x - cell / 2 + 2) * scale), Math.floor((point.y - bounds.y - cell / 2 + 2) * scale), side, side).data;
      let difference = 0;
      for (let i = 0; i < pixels.length; i += 4) difference += Math.abs(pixels[i] - 255) + Math.abs(pixels[i + 1] - 252) + Math.abs(pixels[i + 2] - 245);
      return difference / (pixels.length / 4);
    }, point);
  };
  const original = await contrast();
  expect(original).toBeGreaterThan(10);
  await page.getByRole('button', { name: 'Paint complete', exact: true }).click();
  let point = await pointFor(page, stitch);
  await page.mouse.click(point.x, point.y); await page.mouse.move(0, 0);
  await expect(page.locator('.workspace-progress')).toContainText('1 /');
  await expect.poll(contrast).toBeLessThan(original * .7);
  await expect(page.locator('.save-status')).toContainText('Saved');
  expect((await (await request.get(`/api/projects/${p.id}`)).json()).completed).toContain(stitch.id);
  await open();
  await expect.poll(contrast).toBeLessThan(original * .7);
  await page.getByRole('button', { name: 'Paint incomplete', exact: true }).click();
  point = await pointFor(page, stitch);
  await page.mouse.click(point.x, point.y); await page.mouse.move(0, 0);
  await expect.poll(contrast).toBeGreaterThan(original * .9);
  await expect(page.locator('.save-status')).toContainText('Saved');
  await page.getByRole('button', { name: 'Inspect', exact: true }).click();
  await page.mouse.click(point.x, point.y);
  await page.getByRole('button', { name: 'Mark stitch complete', exact: true }).click();
  await page.mouse.move(0, 0);
  await expect.poll(contrast).toBeLessThan(original * .7);
  await expect(page.locator('.save-status')).toContainText('Saved');
});

test('slow progress saves keep controls steady, accept more strokes, and finish before navigation', async ({ page, request }) => {
  const p = await (await request.post('/api/projects/sample?size=20')).json();
  await request.post(`/api/projects/${p.id}/commands`, { data: { revision: 0, kind: 'rename', name: 'Responsive saving' } });
  await page.goto('/'); await page.getByRole('button', { name: 'Open Responsive saving', exact: true }).click();
  const first = await pointFor(page, p.data.stitches[0]);
  const second = await pointFor(page, p.data.stitches[1]);
  await page.getByRole('button', { name: 'Paint complete', exact: true }).click();

  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route(`**/api/projects/${p.id}/progress`, async route => { await gate; await route.continue(); });
  // Observe actual attribute transitions, including brief flashes between assertions.
  await page.evaluate(() => {
    const targets = [...document.querySelectorAll('.sidebar button, .canvas-toolbar .tool, .key-edit')];
    const initiallyEnabled = new Set(targets.filter(el => !el.hasAttribute('disabled')));
    const flashes: string[] = [];
    const observer = new MutationObserver(records => {
      for (const record of records) if (initiallyEnabled.has(record.target as Element)) flashes.push((record.target as Element).getAttribute('aria-label') || 'navigation');
    });
    observer.observe(document.body, { subtree: true, attributes: true, attributeFilter: ['disabled'] });
    Object.assign(window, { progressFlashes: flashes, progressObserver: observer });
  });
  try {
    await Promise.all([page.waitForRequest(`**/api/projects/${p.id}/progress`), page.mouse.click(first.x, first.y)]);
    await expect(page.locator('.save-status')).toContainText('Saving');
    for (const label of ['Inspect', 'Pan', 'Paint complete', 'Paint incomplete', 'Working area', 'Edit stitches', 'Back to projects', 'Sign out']) {
      if (label === 'Sign out') await page.getByRole('button', { name: /Account menu for/ }).click();
      const button = page.getByRole('button', { name: label, exact: true });
      await expect(button).toBeEnabled(); await expect(button).toHaveCSS('opacity', '1');
      if (label === 'Sign out') await page.keyboard.press('Escape');
    }
    await page.getByRole('button', { name: 'Pan', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Pan', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await page.getByRole('button', { name: 'Paint incomplete', exact: true }).click();
    await page.mouse.click(first.x, first.y);
    await page.getByRole('button', { name: 'Paint complete', exact: true }).click();
    await page.mouse.click(second.x, second.y);
    expect(await page.evaluate(() => {
      const state = window as unknown as { progressFlashes: string[]; progressObserver: MutationObserver };
      state.progressObserver.disconnect(); return state.progressFlashes;
    })).toEqual([]);
    await page.getByRole('button', { name: 'Back to projects', exact: true }).click();
    await expect(page.locator('.workspace')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'My projects', exact: true })).toHaveCount(0);
    release();
    await expect(page.getByRole('heading', { name: 'My projects', exact: true })).toBeVisible();
    const saved = await (await request.get(`/api/projects/${p.id}`)).json();
    expect(saved.completed).toEqual([p.data.stitches[1].id]);
  } finally { release(); }
});

test('undo waits for pending progress and uses the confirmed revision', async ({ page, request }) => {
  const p = await (await request.post('/api/projects/sample?size=20')).json();
  await request.post(`/api/projects/${p.id}/commands`, { data: { revision: 0, kind: 'rename', name: 'Undo after saving' } });
  await request.post(`/api/projects/${p.id}/progress`, { data: { requestId: crypto.randomUUID(), changes: [{ stitchId: p.data.stitches[1].id, complete: true }] } });
  await page.goto('/'); await page.getByRole('button', { name: 'Open Undo after saving', exact: true }).click();
  const point = await pointFor(page, p.data.stitches[0]);
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  await page.route(`**/api/projects/${p.id}/progress`, async route => { await gate; await route.continue(); });
  const edits: { revision: number; kind: string }[] = [];
  page.on('request', req => { if (req.url().endsWith(`/projects/${p.id}/commands`)) edits.push(req.postDataJSON()); });
  try {
    await page.getByRole('button', { name: 'Paint complete', exact: true }).click();
    await Promise.all([page.waitForRequest(`**/api/projects/${p.id}/progress`), page.mouse.click(point.x, point.y)]);
    await page.getByRole('button', { name: 'Undo', exact: true }).click();
    expect(edits).toEqual([]);
    release();
    await expect.poll(() => edits.length).toBe(1);
    await expect(page.locator('.save-status')).toContainText('Saved');
    expect(edits[0]).toMatchObject({ kind: 'undo', revision: 3 });
    const saved = await (await request.get(`/api/projects/${p.id}`)).json();
    expect(saved.completed).toEqual([p.data.stitches[1].id]); expect(saved.name).toBe('Undo after saving');
    await expect(page.getByRole('alert')).toHaveCount(0);
  } finally { release(); }
});

test('failed progress prevents navigation and retains changes for retry without disabling tools', async ({ page, request }) => {
  const p = await (await request.post('/api/projects/sample?size=20')).json();
  await request.post(`/api/projects/${p.id}/commands`, { data: { revision: 0, kind: 'rename', name: 'Retry progress' } });
  await page.goto('/'); await page.getByRole('button', { name: 'Open Retry progress', exact: true }).click();
  const point = await pointFor(page, p.data.stitches[0]);
  await page.route(`**/api/projects/${p.id}/progress`, route => route.fulfill({ status: 503, json: { error: 'Save unavailable' } }));
  await page.getByRole('button', { name: 'Paint complete', exact: true }).click();
  await page.mouse.click(point.x, point.y);
  await expect(page.getByRole('alert')).toContainText('not yet confirmed');
  await page.getByRole('button', { name: 'Back to projects', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('not yet confirmed');
  await expect(page.locator('.workspace')).toBeVisible();
  await expect(page.locator('.workspace-progress')).toContainText('1 /');
  await expect(page.getByRole('button', { name: 'Pan', exact: true })).toBeEnabled();
  expect((await (await request.get(`/api/projects/${p.id}`)).json()).completed).toEqual([]);
  await page.unroute(`**/api/projects/${p.id}/progress`);
  await page.getByRole('button', { name: 'Retry saving', exact: true }).click();
  await expect(page.locator('.save-status')).toContainText('Saved');
  expect((await (await request.get(`/api/projects/${p.id}`)).json()).completed).toEqual([p.data.stitches[0].id]);
});
