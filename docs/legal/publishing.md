# Publishing and editor access

## Initial setup

1. Deploy the server and frontend together. Apply `VersionedLegalDocuments` through the usual `--migrate` command in the configured application environment. It creates two blank **unpublished** drafts: `terms-of-service` and `privacy-policy`. Existing accounts continue working until a required document is published. Do not mix old servers that lack acceptance enforcement with the new release.
2. Have the intended editor sign in normally so their account exists. From an operator shell using the application's database configuration, explicitly assign editor access:

   ```powershell
   dotnet run --project server -c Release --no-build -- --legal-editor "editor@example.com"
   ```

   An account GUID can replace the email address. Email lookup must identify exactly one existing account; use the GUID when accounts share an email. The CLI never creates an account. Development configuration requires `ASPNETCORE_ENVIRONMENT=Development`; production uses its normal configured environment. No editor is assigned on migration, sign-in, or first visit.

   To revoke access:

   ```powershell
   dotnet run --project server -c Release --no-build -- --remove-legal-editor "ACCOUNT-GUID"
   ```

   Role membership is read from the database on every management request, so users do not need to sign in again after a change. This role allows document management and lookup of account acceptance evidence; it does not grant access to other users' patterns.
3. Visit `/admin/legal`, or **Account & data → My legal acceptances & documents → Manage legal documents**. Fill each draft with approved content and acceptance wording. The editor remains accessible even when the editor has outstanding acceptance requirements.

## Draft, review, and publication

Choose an existing document or **New document**. New identifiers use lowercase letters, digits, and hyphens, start with a letter, and have at most 80 characters. Identifiers are permanent public URL components. Titles can change in subsequent versions.

Document content is plain text, up to 250,000 characters. Paragraph breaks are preserved; HTML, scripts, and Markdown are displayed literally. Enter a publication summary and the exact wording for the user's checkbox, such as agreement or acknowledgment wording approved for that document. Required documents must have a nonblank acceptance statement.

Choose whether the document requires acceptance from **all signed-in users**. For required documents, **Require existing users to accept again** is enabled by default. Uncheck it only when an editorial update is intended to preserve existing acceptance. Review that setting every time: it is saved with the draft. Turning a requirement off publishes an informational version; turning it back on always requires a fresh acceptance.

Save the draft, review the saved preview and acceptance settings, check the publication confirmation, and select **Publish new version**. Publication is immediate. There is no scheduled effective date or grace period in this release. Editing a draft does not change the public document. Another editor's saved changes produce a conflict; reload and reconcile before saving or publishing.

Published versions cannot be overwritten or deleted through the app or normal database updates. Correct mistakes by publishing another version. To revert wording, copy the desired historical text into the draft and publish a new numbered version; history remains intact.

## Public links and future documents

| URL | Purpose |
| --- | --- |
| `/legal` | All current published documents |
| `/legal/terms-of-service` | Current published Terms of Service |
| `/legal/privacy-policy` | Current published Privacy Policy |
| `/legal/{identifier}/{version}` | Exact historical published text |
| `/account/legal` | Signed-in user's last acceptance per document and full history |
| `/admin/legal` | Editor publishing and acceptance lookup |

Public legal pages work without a session. Before publication, the individual URL explains that no document has been published. The public landing page links to Terms, Privacy, and the document index. Once approved documents are published, configure the corresponding public URLs in the OAuth consent screen through the normal provider administration process.

Add `subscription-terms`, `cancellation-policy`, or other documents using **New document**. This requires no schema change or deployment. This release has no subscription eligibility or purchase flow: marking such a document required will affect everyone. Keep documents informational until that behavior is appropriate, or implement an audience-specific acceptance flow alongside subscriptions.

## Looking up acceptance

Use **Find an account's acceptances** with an account GUID or exact email. The result shows each document's current version, last version actually accepted, timestamp, and whether acceptance is pending. Expand history to inspect the exact text, acceptance statement, and SHA-256 digest. Email lookup shows up to 20 matching accounts with IDs; use an ID to disambiguate.

For example, if someone accepts version 1 and version 2 is editorial, their row shows **Current 2 / Last accepted 1 / Up to date**. Publishing version 3 with reacceptance enabled changes it to **Current 3 / Last accepted 1 / Acceptance required**. Only their explicit acceptance of version 3 changes the recorded last accepted version.

Records cover acceptance collected after this feature is enabled. Existing accounts are never backfilled as having accepted historical terms.
