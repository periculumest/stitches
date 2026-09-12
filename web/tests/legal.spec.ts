import { test, expect } from './fixtures';

// Run against a fresh isolated database with STITCH_TEST_LEGAL_EDITOR=true in TestSupport.
test.skip(process.env.STITCH_TEST_LEGAL_EDITOR !== 'true', 'Requires an editor fixture in a fresh isolated legal test database.');
test('editor publishes, users accept exact versions, and updates prompt again without replacing the workspace', async ({ page, request, browser }) => {
  await page.goto('/admin/legal');
  await page.getByRole('button', { name: 'Terms of Service', exact: true }).click();
  await page.getByLabel('Document text (plain text)', { exact: true }).fill('Terms for browser testing.\n<script>window.legalScriptRan = true</script>');
  await page.getByLabel('Publication summary', { exact: true }).fill('Initial test terms');
  await page.getByLabel('Acceptance statement (the exact checkbox wording)', { exact: true }).fill('I agree to these test terms');
  await page.getByRole('button', { name: 'Save draft', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Draft saved');
  await page.getByLabel('I reviewed this saved draft', { exact: false }).check();
  await page.getByRole('button', { name: 'Publish new version', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('version 1 published');
  const publicContext = await browser.newContext(); const publicPage = await publicContext.newPage();
  await publicPage.goto(new URL('/legal/terms-of-service/1', page.url()).href);
  await expect(publicPage.locator('.legal-body')).toContainText('<script>');
  expect(await publicPage.evaluate(() => 'legalScriptRan' in window)).toBe(false);
  await publicContext.close();
  await page.goto('/');
  const dialog = page.getByRole('dialog', { name: 'Review legal documents' });
  await expect(dialog).toBeVisible();
  const accept = dialog.getByRole('button', { name: 'Accept selected documents' });
  await expect(accept).toBeDisabled();
  await page.keyboard.press('Escape'); await expect(dialog).toBeVisible();
  await dialog.getByLabel('I agree to these test terms (version 1)', { exact: true }).check();
  await accept.click(); await expect(dialog).not.toBeVisible();
  await expect(page.getByRole('heading', { name: 'My projects', exact: true })).toBeVisible();
  const publishUpdate = async (reaccept: boolean) => {
    const drafts = await (await request.get('/api/admin/legal/drafts')).json();
    const draft = drafts.find((d: { slug: string }) => d.slug === 'terms-of-service');
    const saved = await request.put('/api/admin/legal/drafts/terms-of-service', { data: { ...draft, revision: draft.draftRevision, body: draft.body + '\nUpdated.', changeSummary: reaccept ? 'New obligations' : 'Editorial correction', requireReacceptance: reaccept } }); expect(saved.ok()).toBeTruthy();
    const response = await request.post('/api/admin/legal/drafts/terms-of-service/publish', { data: { revision: (await saved.json()).draftRevision } }); expect(response.ok()).toBeTruthy(); return response.json();
  };
  await publishUpdate(false);
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(dialog).not.toBeVisible();
  let state = await (await request.get('/api/legal/status')).json();
  expect(state.documents[0].lastAcceptance.version).toBe(1); expect(state.documents[0].current.version).toBe(2);
  // Tag the actual application element; a reacceptance update must preserve it.
  await page.getByRole('heading', { name: 'My projects', exact: true }).evaluate(el => el.setAttribute('data-preserved', 'yes'));
  await publishUpdate(true);
  await page.evaluate(() => window.dispatchEvent(new Event('legal-acceptance-required')));
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('I agree to these test terms (version 3)', { exact: true }).check();
  await publishUpdate(true); // Publishing during review must reject stale acceptance.
  await accept.click();
  await expect(dialog.getByRole('alert')).toContainText('changed while you were reviewing');
  const currentChoice = dialog.getByLabel('I agree to these test terms (version 4)', { exact: true });
  await expect(currentChoice).not.toBeChecked(); await expect(accept).toBeDisabled();
  await currentChoice.check(); await accept.click(); await expect(dialog).not.toBeVisible();
  await expect(page.getByRole('heading', { name: 'My projects', exact: true })).toHaveAttribute('data-preserved', 'yes');
  await page.goto('/account/legal');
  await expect(page.locator('tbody')).toContainText('Version 4');
  await expect(page.locator('summary')).toHaveCount(2);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  await page.screenshot({ path: '../artifacts/legal-acceptances-mobile.png', fullPage: true });
  state = await (await request.get('/api/legal/status')).json();
  expect(state.documents[0].needsAcceptance).toBe(false);
  let failOnce = true;
  await page.route('**/api/legal/documents/terms-of-service/versions/5', async route => {
    if (failOnce) { failOnce = false; await route.fulfill({ status: 503, contentType: 'application/json', body: '{"error":"Temporary document failure"}' }); }
    else await route.continue();
  });
  await publishUpdate(true);
  await page.goto('/');
  await expect(dialog.getByRole('alert')).toContainText('Temporary document failure');
  await dialog.getByRole('button', { name: 'Check again' }).click();
  await dialog.getByLabel('I agree to these test terms (version 5)', { exact: true }).check();
  await accept.click(); await expect(dialog).not.toBeVisible();
});
