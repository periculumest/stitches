import { test, expect } from './fixtures';

test('feedback is keyboard accessible, opt-in, and lost-response retries return a receipt', async ({ page, request }) => {
  await page.goto('/?token=do-not-capture#private');
  const trigger = page.getByRole('button', { name: 'Send Feedback', exact: true });
  await trigger.focus(); await page.keyboard.press('Enter');
  const dialog = page.getByRole('dialog', { name: 'Send Feedback', exact: true });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel('Include technical diagnostics')).not.toBeChecked();
  await expect(dialog.getByLabel('Attach screenshots I choose')).not.toBeChecked();
  await dialog.getByLabel('What happened or what would help?').fill('Keyboard feedback with a lost response');
  let lost = true; const bodies: string[] = [];
  await page.route('**/api/beta/feedback', async route => {
    const body = route.request().postData()!; bodies.push(body);
    if (lost) { lost = false; const response = await route.fetch(); expect(response.ok()).toBeTruthy(); await route.abort('failed'); }
    else await route.continue();
  });
  await dialog.getByRole('button', { name: 'Send report', exact: true }).focus(); await page.keyboard.press('Enter');
  await expect(dialog.getByRole('alert')).toContainText('Your draft is still in this tab');
  await expect(dialog.getByLabel('What happened or what would help?')).toHaveValue('Keyboard feedback with a lost response');
  await dialog.getByRole('button', { name: 'Retry submission' }).focus(); await page.keyboard.press('Enter');
  await expect(page.getByRole('dialog', { name: 'Feedback received' })).toContainText('Receipt:');
  expect(bodies).toHaveLength(2);
  for (const body of bodies) { expect(body).toContain('"route":"library"'); expect(body).not.toContain('do-not-capture'); expect(body).not.toContain('#private'); }
  const keys = bodies.map(b => b.match(/"submissionKey":"([^"]+)"/)![1]); expect(keys[0]).toBe(keys[1]);
  await page.getByRole('button', { name: 'Done', exact: true }).focus(); await page.keyboard.press('Enter'); await expect(trigger).toBeFocused();
  const history = await request.get('/api/beta/feedback'); expect(history.status()).toBe(404);
});

test('draft dismissal warns, screenshot choices can be removed, and session expiry preserves text', async ({ page }) => {
  await page.goto('/'); await page.getByRole('button', { name: 'Send Feedback', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Send Feedback', exact: true });
  await dialog.getByLabel('What happened or what would help?').fill('Keep this feedback draft');
  page.once('dialog', d => d.dismiss()); await page.keyboard.press('Escape'); await expect(dialog).toBeVisible();
  await dialog.getByLabel('Attach screenshots I choose').check();
  await dialog.locator('input[type=file]').setInputFiles({ name: 'selected.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhX0AAAAASUVORK5CYII=', 'base64') });
  await expect(dialog.getByRole('button', { name: 'Remove selected.png' })).toBeVisible();
  await dialog.getByRole('button', { name: 'Remove selected.png' }).click(); await dialog.getByLabel('Attach screenshots I choose').uncheck();
  await page.evaluate(() => window.dispatchEvent(new Event('session-expired')));
  const session = page.getByRole('dialog', { name: 'Your session expired' }); await expect(session).toBeVisible();
  await session.getByRole('button', { name: 'I signed in — resume this tab' }).click(); await expect(session).not.toBeVisible();
  await expect(dialog.getByLabel('What happened or what would help?')).toHaveValue('Keep this feedback draft');
  page.once('dialog', d => d.accept()); await dialog.getByRole('button', { name: 'Cancel', exact: true }).click(); await expect(dialog).not.toBeVisible();
});

test('render failure has independent feedback and safe client reference details', async ({ page }) => {
  await page.route('**/api/projects', route => route.fulfill({ status: 200, json: { invalid: 'simulated response shape' } }));
  await page.goto('/');
  await expect(page.locator('.fatal')).toContainText('CLIENT_RENDER_FAILURE');
  await expect(page.locator('.fatal')).toContainText('does not identify a server log');
  await page.getByRole('button', { name: 'Send Feedback', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Send Feedback', exact: true });
  await dialog.getByLabel('What happened or what would help?').fill('The main screen failed but this report still works');
  await dialog.getByLabel('Include technical diagnostics').check();
  await dialog.getByRole('button', { name: 'Send report', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Feedback received' })).toContainText('Receipt:');
});

test('failed import can report its retained source without chart stitches', async ({ page, request }) => {
  const imported = await request.post('/api/imports', { multipart: { file: { name: 'unsupported.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\nnot a usable chart') } } });
  expect(imported.ok()).toBeTruthy(); const p = await imported.json(); expect(p.data.stitches).toHaveLength(0);
  await page.goto('/'); await page.getByRole('button', { name: `Open ${p.name}`, exact: true }).click();
  await page.getByRole('button', { name: 'Report a problem with this pattern', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Report a problem with this pattern', exact: true });
  const consent = dialog.getByLabel('Attach my pattern to this report so the administrator can inspect it.');
  await expect(consent).toBeEnabled(); await expect(consent).not.toBeChecked(); await consent.check();
  await dialog.getByLabel('What happened or what would help?').fill('This import failed and I consent to share the retained source');
  await dialog.getByRole('button', { name: 'Send report', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Feedback received' })).toContainText('Receipt:');
});

test('admin completes and archives reports; published safe release notes appear', async ({ page, request }) => {
  test.skip(process.env.STITCH_TEST_BETA_ADMIN !== 'true', 'Requires an isolated beta administrator fixture.');
  const submission = await request.post('/api/beta/feedback', { multipart: { input: JSON.stringify({ submissionKey: crypto.randomUUID(), category: 'Other', message: 'Admin completion browser test', route: 'library', diagnosticsIncluded: false, diagnosticsDisclosureVersion: 1 }) } });
  const receipt = await submission.json();
  await page.goto('/admin/beta'); await page.getByRole('button', { name: `Open report ${receipt.id.slice(0, 8)}` }).click();
  const detail = page.getByRole('dialog', { name: 'Feedback details' }); await detail.getByRole('button', { name: 'Mark completed', exact: true }).click();
  await expect(detail).toContainText('Completed'); await detail.getByRole('button', { name: 'Mark archived', exact: true }).click(); await expect(detail).toContainText('Archived');
  await detail.getByRole('button', { name: 'Close dialog' }).click();
  await expect(page.getByRole('combobox', { name: 'Status', exact: true })).toBeFocused();
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('Completed');
  await page.getByLabel('Include archived completed reports').check(); await expect(page.getByRole('button', { name: `Open report ${receipt.id.slice(0, 8)}` })).toBeVisible();
  await request.post('/api/admin/beta/releases', { data: { version: 'browser-test', releaseDate: '2026-09-12', title: 'Safe published notes', bodyMarkdown: '# Changes\n\n- Feedback added\n\n<script>window.betaScriptRan=true</script>\n\n[unsafe](javascript:alert(1))', publish: true } });
  await page.goto('/whats-new'); await expect(page.getByRole('heading', { name: 'Safe published notes' })).toBeVisible();
  await expect(page.locator('.release-markdown')).toContainText('<script>'); expect(await page.evaluate(() => 'betaScriptRan' in window)).toBe(false); await expect(page.locator('.release-markdown a')).toHaveCount(0);
});

test('feedback details prioritize the message, keep attachments explicit, and fit mobile', async ({ page, request }) => {
  test.skip(process.env.STITCH_TEST_BETA_ADMIN !== 'true', 'Requires an isolated beta administrator fixture.');
  const message = 'My pattern looks different after uploading\n\nThe blue symbols in the top corner seem to be missing. I tried uploading the PDF again, but the same thing happened.\n\nIt would help to see a preview before I start stitching. Thank you!';
  const submission = await request.post('/api/beta/feedback', { multipart: { input: JSON.stringify({ submissionKey: crypto.randomUUID(), category: 'Bug', message, route: 'workspace', diagnosticsIncluded: false, diagnosticsDisclosureVersion: 1 }) } });
  expect(submission.ok()).toBeTruthy();
  const receipt = await submission.json();
  const original = await (await request.get(`/api/admin/beta/feedback/${receipt.id}`)).json();
  const longReference = 'reference-'.repeat(30);
  await page.route(`**/api/admin/beta/feedback/${receipt.id}`, route => route.fulfill({ json: {
    ...original,
    report: { ...original.report, diagnosticsIncluded: true, appVersion: '0.1.0-beta', errorCode: 'IMPORT_SYMBOLS_UNRESOLVED', correlationId: longReference, browserMetadata: 'Chrome · Windows', screenMetadata: '1440 × 1000', importMetadata: '{invalid legacy metadata' },
    attachments: [{ id: 'screenshot-fixture', kind: 'Screenshot', available: true }, { id: 'removed-pattern-fixture', kind: 'Pattern', available: false }],
  } }));
  const attachmentRequests: string[] = [];
  page.on('request', req => { if (req.url().includes(`/feedback/${receipt.id}/attachments/`)) attachmentRequests.push(req.url()); });
  await page.goto('/admin/beta');
  const trigger = page.getByRole('button', { name: `Open report ${receipt.id.slice(0, 8)}` });
  await trigger.click();
  const detail = page.getByRole('dialog', { name: 'Feedback details' });
  await expect(detail.locator('.feedback-message')).toHaveText(message);
  await expect(detail.locator('.feedback-message')).toHaveCSS('white-space', 'pre-wrap');
  await expect(detail.getByText('IMPORT_SYMBOLS_UNRESOLVED', { exact: true })).not.toBeVisible();
  await expect(detail.getByRole('link', { name: 'Open screenshot 1' })).toHaveAttribute('href', `/api/admin/beta/feedback/${receipt.id}/attachments/screenshot-fixture`);
  await expect(detail.getByText('Source removed · no longer available')).toBeVisible();
  await expect(detail.getByRole('link')).toHaveCount(1);
  await page.screenshot({ path: '../artifacts/feedback-details-desktop.png' });
  await detail.locator('summary').click();
  await expect(detail.getByText(longReference, { exact: true })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await detail.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  expect(await detail.locator('.feedback-body').evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  await detail.locator('summary').click();
  await detail.locator('.feedback-body').evaluate(el => { el.scrollTop = 0; });
  await page.screenshot({ path: '../artifacts/feedback-details-mobile.png' });
  const complete = detail.getByRole('button', { name: 'Mark completed', exact: true });
  const box = (await complete.boundingBox())!;
  expect(box.y + box.height).toBeLessThan(844);
  await page.route(`**/api/admin/beta/feedback/${receipt.id}/status`, route => route.fulfill({ status: 503, json: { message: 'Please try again.', code: 'UNAVAILABLE' } }));
  await complete.click();
  await expect(detail.getByRole('alert')).toBeVisible();
  await expect(detail.locator('.feedback-status')).toHaveText('Unread');
  await expect(complete).toBeEnabled();
  expect(attachmentRequests).toHaveLength(0);
  await page.keyboard.press('Escape');
  await expect(detail).not.toBeVisible();
  await expect(trigger).toBeFocused();
});
