import { test, expect } from './fixtures';
import path from 'node:path';
import { readFileSync } from 'node:fs';
import type { APIRequestContext, Page } from '@playwright/test';

async function importChart(page: Page, request: APIRequestContext, name: string) {
  const result = await request.post('/api/imports', { multipart: { file: { name: `${name}.pdf`, mimeType: 'application/pdf', buffer: readFileSync(path.resolve('../artifacts/structured-sampler.pdf')) } } });
  expect(result.ok()).toBeTruthy(); const p = await result.json();
  await page.goto('/'); await page.getByRole('button', { name: `Open ${name}`, exact: true }).click();
  await page.getByRole('button', { name: 'Arrange pages', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Arrange chart pages' })).toBeVisible();
  return p;
}

test('page builder arranges a draft, saves atomically, persists, and supports workspace undo', async ({ page, request }) => {
  const errors: string[] = []; page.on('pageerror', e => errors.push(e.message));
  const p = await importChart(page, request, 'Builder rows');
  await page.getByLabel('Top-left page').selectOption('2');
  await page.getByLabel('Pages per row').selectOption('2');
  await page.getByRole('button', { name: 'Arrange in rows' }).click();
  await expect(page.getByRole('button', { name: 'Page 2, column 1, row 1', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Page 1, column 11, row 1', exact: true })).toBeVisible();
  expect((await (await request.get(`/api/projects/${p.id}`)).json()).revision).toBe(p.revision);
  await page.screenshot({ path: '../artifacts/page-builder.png' });
  await page.getByRole('button', { name: 'Save arrangement', exact: true }).click();
  await expect(page.getByRole('dialog')).not.toBeVisible();
  await expect(page.locator('.workspace-title')).toContainText('20 × 10');
  let saved = await (await request.get(`/api/projects/${p.id}`)).json();
  expect(saved.revision).toBe(p.revision + 1); expect(saved.dataRevision).toBe(p.dataRevision + 1);
  expect(saved.data.stitches).toHaveLength(200); expect(saved.status).toBe('audit');
  await page.reload(); await page.getByRole('button', { name: 'Open Builder rows', exact: true }).click();
  await expect(page.locator('.workspace-title')).toContainText('20 × 10');
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(page.locator('.workspace-title')).toContainText('10 × 20');
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await expect(page.locator('.workspace-title')).toContainText('20 × 10');
  await page.getByRole('button', { name: 'Looks good, start stitching' }).click();
  await expect(page.getByRole('button', { name: 'Arrange pages', exact: true })).not.toBeVisible();
  saved = await (await request.get(`/api/projects/${p.id}`)).json(); expect(saved.status).toBe('active');
  expect(errors).toEqual([]);
});

test('dragging snaps pages, keyboard moves are reversible, and cancelled drafts leave the project untouched', async ({ page, request }) => {
  const p = await importChart(page, request, 'Builder drag');
  const first = await page.getByRole('button', { name: 'Page 1, column 1, row 1', exact: true }).boundingBox();
  const second = await page.getByRole('button', { name: 'Page 2, column 1, row 11', exact: true }).boundingBox();
  await page.mouse.move(second!.x + second!.width / 2, second!.y + second!.height / 2);
  await page.mouse.down(); await page.mouse.move(first!.x + first!.width * 1.5, first!.y + first!.height / 2, { steps: 12 }); await page.mouse.up();
  const tile = page.getByRole('button', { name: 'Page 2, column 11, row 1', exact: true }); await expect(tile).toBeVisible();
  await tile.focus(); await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('button', { name: 'Page 2, column 12, row 1', exact: true })).toBeVisible();
  await expect(page.locator('.builder-checks')).toContainText('outside the page coverage');
  await page.getByRole('button', { name: 'Undo move', exact: true }).click(); await expect(tile).toBeVisible();
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.getByText('Discard your unsaved arrangement?')).toBeVisible();
  await page.getByRole('button', { name: 'Keep arranging' }).click(); await expect(tile).toBeVisible();
  await page.keyboard.press('Escape'); await page.getByRole('button', { name: 'Discard changes', exact: true }).click();
  expect((await (await request.get(`/api/projects/${p.id}`)).json()).revision).toBe(p.revision);
});

test('matching overlap can be saved, then separated without losing source stitches', async ({ page, request }) => {
  const p = await importChart(page, request, 'Builder joins');
  await page.getByRole('combobox', { name: 'Selected page', exact: true }).selectOption('2');
  await page.getByLabel('Neighbor page').selectOption('1');
  await page.getByLabel('Overlap at this join').fill('2');
  await page.getByRole('button', { name: 'Place right', exact: true }).click();
  await expect(page.locator('.builder-checks')).toContainText('20 matching stitch pairs');
  await page.getByRole('button', { name: 'Save arrangement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  let saved = await (await request.get(`/api/projects/${p.id}`)).json(); expect(saved.data.stitches).toHaveLength(180); expect(saved.data.pageOverlapStitches).toHaveLength(20);
  await page.getByRole('button', { name: 'Arrange pages', exact: true }).click();
  await page.getByRole('combobox', { name: 'Selected page', exact: true }).selectOption('2');
  await page.getByLabel('Column offset').fill('10');
  await page.getByRole('button', { name: 'Save arrangement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  saved = await (await request.get(`/api/projects/${p.id}`)).json(); expect(saved.data.stitches).toHaveLength(200); expect(saved.data.pageOverlapStitches).toHaveLength(0);
});

test('failed saves retain the draft for retry and stale drafts cannot overwrite another window', async ({ page, request }) => {
  const p = await importChart(page, request, 'Builder recovery');
  await page.getByLabel('Pages per row').selectOption('2'); await page.getByRole('button', { name: 'Arrange in rows' }).click();
  await page.route(`**/api/projects/${p.id}/commands`, route => route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ error: 'Temporary save failure' }) }));
  await page.getByRole('button', { name: 'Save arrangement', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('Temporary save failure');
  await expect(page.getByLabel('Column offset')).toHaveValue('0');
  await page.unroute(`**/api/projects/${p.id}/commands`);
  await page.getByRole('button', { name: 'Save arrangement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByRole('button', { name: 'Arrange pages', exact: true }).click();
  await page.getByRole('button', { name: 'Move page down one stitch' }).click();
  const saved = await (await request.get(`/api/projects/${p.id}`)).json();
  expect((await request.post(`/api/projects/${p.id}/commands`, { data: { kind: 'rename', revision: saved.revision, name: 'Builder changed elsewhere' } })).ok()).toBeTruthy();
  await page.getByRole('button', { name: 'Save arrangement', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('changed in another window');
  const latest = await (await request.get(`/api/projects/${p.id}`)).json(); expect(latest.data.pages).toEqual(saved.data.pages);
});

test('page builder remains usable on a narrow screen', async ({ page, request }) => {
  await page.setViewportSize({ width: 390, height: 844 }); await importChart(page, request, 'Builder mobile');
  await page.getByRole('combobox', { name: 'Selected page', exact: true }).selectOption('2');
  await page.getByRole('button', { name: 'Place right', exact: true }).click();
  await page.getByRole('button', { name: 'Save arrangement', exact: true }).scrollIntoViewIfNeeded();
  expect(await page.getByRole('dialog').evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  await page.screenshot({ path: '../artifacts/page-builder-mobile.png' });
  await page.getByRole('button', { name: 'Save arrangement', exact: true }).click(); await expect(page.getByRole('dialog')).not.toBeVisible();
});
