import { test, expect } from './fixtures';
import path from 'node:path';
import { existsSync } from 'node:fs';

test('Iron Man imports its complete chart, original symbols, and two-strand palette', async ({ page, request }) => {
  test.slow();
  const source = path.resolve('../docs/sample-patterns/Iron Man (1).pdf');
  test.skip(!existsSync(source), 'User-provided PDF is absent; synthetic importer regressions still run.');
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await page.getByRole('button', { name: 'Import a pattern', exact: true }).click();
  // Parsing and saving this 48-page chart can exceed the default assertion timeout on CI.
  const [importResponse] = await Promise.all([
    page.waitForResponse(response => response.url().endsWith('/api/imports') && response.request().method() === 'POST', { timeout: 90_000 }),
    page.locator('input[type=file]').setInputFiles(source),
  ]);
  expect(importResponse.ok(), 'Iron Man PDF import succeeded').toBeTruthy();
  await importResponse.finished();
  await expect(page.getByRole('heading', { name: 'Iron Man (1)', exact: true })).toBeVisible();
  await expect(page.locator('.workspace-title')).toContainText('450 × 450');
  await expect(page.locator('.workspace-title')).toContainText('63 colors');
  await expect(page.locator('.workspace-title')).toContainText('48 chart pages');
  await expect(page.locator('.workspace-progress')).toContainText('202,500');
  await expect(page.locator('.audit-notes')).toContainText('verified coverage');
  await expect(page.locator('.key-choice .source-symbol')).toHaveCount(63);
  const glyphBounds = await page.locator('.key-choice .source-symbol path').evaluateAll(paths => paths.map(path => {
    const bounds = (path as SVGGraphicsElement).getBBox(); return [bounds.width, bounds.height];
  }));
  expect(glyphBounds.every(([width, height]) => width > 0 && height > 0)).toBe(true);
  await page.screenshot({ path: '../artifacts/iron-man-import.png', fullPage: true });
  await page.getByRole('button', { name: 'Arrange pages', exact: true }).click();
  await expect(page.locator('.builder-tile')).toHaveCount(48);
  await expect(page.locator('.builder-checks')).toContainText('No conflicting stitches');
  await page.screenshot({ path: '../artifacts/page-builder-iron-man.png' });
  await page.getByRole('button', { name: 'Close page builder', exact: true }).click();
  await page.getByRole('button', { name: 'Looks good, start stitching' }).click();
  await expect(page.getByText('Does this look like your pattern?')).not.toBeVisible();
  await page.getByLabel('Apply completion to').selectOption('pattern');
  await page.getByRole('textbox', { name: 'Search pattern key' }).fill('826');
  await expect(page.locator('.key-choice')).toContainText('DMC 826 (2 strands)');
  await page.locator('.key-choice').click();
  await expect(page.locator('.scope-summary')).toContainText('4 matching stitches');
  await page.getByRole('button', { name: 'Mark matching complete', exact: true }).click();
  await expect(page.locator('.save-status')).toContainText('Saved');
  const projects = await (await request.get('/api/projects')).json();
  const saved = projects.find((project: { name: string }) => project.name === 'Iron Man (1)');
  expect(saved.completed).toBe(4);
  await page.reload();
  await page.getByRole('button', { name: 'Open Iron Man (1)', exact: true }).click();
  await expect(page.locator('.workspace-progress')).toContainText('4 / 202,500');
  expect(errors).toEqual([]);
});
