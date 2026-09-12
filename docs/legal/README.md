# Versioned legal documents

Implemented September 9, 2026. This directory records the design, publishing workflow, acceptance behavior, and verification. See [Publishing and editor access](publishing.md) and [Data model, API, and lifecycle](architecture.md).

## Agreed scope and design

- A database-backed document registry, initially with unpublished Terms of Service and Privacy Policy drafts. Additional slugs support subscription, cancellation, and other documents without schema changes.
- Editors can create and revise drafts, preview, and publish through an authenticated management interface. Publishing requires an explicitly assigned LegalEditor role; ordinary accounts cannot grant themselves access.
- Published versions are immutable snapshots of title, plain-text body, change summary, and acceptance wording, with a sequential version, server publication time, and SHA-256 digest. Public URLs expose current and historical published versions; drafts remain private.
- A document can require acceptance for app use or be informational. Publishing a required update normally requires reacceptance. An editorial update can keep the previous acceptance requirement while still creating a new content version. A user's record always identifies the version actually accepted; it is never silently advanced.
- Acceptance uses unchecked, document-specific confirmations. The server records the authenticated user, exact published version, digest, and server timestamp. It rejects stale versions if another update was published while a person was reviewing them.
- Required acceptance is checked on the server as well as in the UI. Reading legal documents, signing out, exporting current data, and deleting an account remain available without accepting updates.
- Users can see their own acceptance history; editors can look up the last version accepted for each document by account ID or exact email. Evidence is scoped to the authenticated user or authorized editor.
- Account deletion also deletes that user's acceptance records under the current deletion policy. Shared published legal documents remain. Exports include the user's acceptance records and the exact accepted document snapshots.

## Content and initial rollout

No legal prose is invented or published automatically. The initial drafts need approved text before publication. Plain text is rendered without interpreting HTML, scripts, or Markdown. Acceptance wording is part of each version, allowing agreement, acknowledgment, or other counsel-approved wording.

Subscription eligibility, payment cancellation flows, scheduled effective dates, and marketing-consent preferences are outside this first release. Documents can be added now, but required documents apply to all app users until a future audience-specific acceptance flow is implemented.

## Verification

The backend suite passes 82 tests, including five new PostgreSQL/API scenarios covering draft privacy, editor authorization, stale saves/publications, immutable snapshots and receipts, editorial versus required updates, re-enabling a requirement, atomic batch rejection, concurrent acceptance retries, owner isolation, CSRF, export evidence, and account deletion. These tests run against actual migrations, not an in-memory database.

Seven browser tests pass across legal publishing/acceptance, the public landing page, and deletion flows. The legal scenario covers editor publication, escaped plain text, public historical URLs, unchecked confirmation, required acceptance, editorial updates, rejection of publication races, reset checkboxes, preservation of the mounted workspace, and acceptance history on a narrow screen.

The application and production frontend build successfully. The migration is applied locally and the tested app is running at `http://127.0.0.1:5057`; readiness succeeds and the public document list is empty, as expected before publication. No approved legal text or editor account is provisioned automatically.

### Repeating the tests

Set `STITCH_TEST_POSTGRES` to the local test cluster's administrative connection and run `dotnet test tests/StitchHelper.Tests.csproj -c Test`. The backend tests create and remove their own isolated databases.

For the legal browser scenario, create a **fresh** database whose name starts with `stitch_test_`. Set `STITCH_BROWSER_POSTGRES` to that database, `STITCH_TEST_LEGAL_EDITOR=true`, and `STITCH_TEST_URL` to the test host. Run `dotnet run --project tests/TestSupport -c Test -- artifacts/legal-browser-auth.json` to migrate it and create a test-only editor cookie. Run the application in `Testing` with `ConnectionStrings__StitchHelper` pointing to that database and `Storage__LocalRoot` pointing to an isolated artifact directory. Set `STITCH_TEST_AUTH=../artifacts/legal-browser-auth.json`, keep `STITCH_TEST_LEGAL_EDITOR=true`, and run `npm test --prefix web -- legal.spec.ts retention.spec.ts landing.spec.ts`. The legal scenario publishes test text, so it must never run against a real account database. It is skipped unless the explicit editor-fixture flag is set. It also verifies retrying a temporary document-loading failure.
