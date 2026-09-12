# SH-BETA-001 implementation decisions

Implemented 2026-09-12. Scope: [Beta Experience & Feedback](01-beta-experience-feedback.md). These decisions use the discretion granted for this implementation. The pre-existing legal-document and retention changes were preserved.

## Repository mapping and dependency boundary

The repository has an immutable imported `OwnedPattern` snapshot, an independently editable `OwnedProject` with `DataRevision`, a private `PatternSourceAsset`, and one `ImportRecord` for the original import. There is no candidate-revision or asynchronous import-worker model yet. Feedback records the server-derived project ID, `PatternId:DataRevision` revision identifier, latest owned import record, and a bounded status/time/parser-version snapshot. The identifier describes the current model; it is not a promise that an old editable chart can be reconstructed. Older imports have parser version `unknown`; new imports record the parser assembly version.

Safe extraction failures already produce a project with no chart stitches and a retained source. Reporting works from that review screen. Rejected uploads never produce a source and can be reported through general feedback. If a project/source disappears while composing, the user can explicitly remove the association or attachment and submit the text. A pattern consent binds the source ID returned when the form was opened; the server rechecks that exact source under the same per-account content lock used by deletion. Feedback never gives an administrator general project access.

Only the supporting parts of SH-BETA-002/003/004 necessary for this experience are added here. Candidate imports, worker leases, account suspension/restoration, access-request administration, provider malware scanning, and the broader operations dashboard remain owned by those specifications. This implementation does not mark them complete.

## Feedback, diagnostics, and storage

- Defaults: messages 5–5,000 characters; one PNG/JPEG screenshot, maximum 5 MiB before and after normalization and 12 million decoded pixels; 500 MiB combined retained-source and screenshot quota for admitting new screenshots. Text-only submissions bypass the storage quota. Existing-source references do not consume the quota again. The broader source-upload quota policy belongs to SH-BETA-004.
- These values are configurable under `Beta:MessageMinLength`, `MessageMaxLength`, `ScreenshotCount`, `ScreenshotMaxBytes`, `ScreenshotMaxPixels`, and `StorageQuotaBytes`. Startup rejects unsafe ranges. Maximum configured count is three, each at most 10 MiB/24 million pixels. Screenshot decoding has a process-wide limit of two concurrent decoders; overload asks the user to retry.
- Screenshots are bounded PostgreSQL `bytea` values in `FeedbackAttachment`. This is a deliberate beta tradeoff: the text, consent, and screenshots commit in one database transaction, with no separately uploaded permanent object or orphan-cleanup protocol. It increases database/backup size and can be replaced with private object staging when volume warrants it. Multipart request buffers are request-scoped framework temporary data, not permanent feedback uploads.
- Images are identified before full decode, restricted to PNG/JPEG, decoded to one RGBA frame, then copied into a fresh image and re-encoded as PNG. This removes original EXIF/IPTC/XMP/ICC and frame metadata and filenames. The decoder is SixLabors.ImageSharp 3.1.12; its package license is the Six Labors Split License. See the [decoder options](https://docs.sixlabors.com/api/ImageSharp/SixLabors.ImageSharp.Formats.DecoderOptions.html) and [package details](https://www.nuget.org/packages/SixLabors.ImageSharp/3.1.12).
- The server generates screenshot/attachment IDs; there is no API accepting arbitrary uploaded attachment IDs or filesystem paths. Opening an attachment requires the current database-backed BetaAdmin role, a matching report attachment, and an audit record. Original PDFs remain referenced once, in the existing private store. Admin lists never fetch screenshot or PDF bytes.
- Account deletion cascades feedback, screenshot bytes, onboarding, and announcement dismissals. Existing source deletion sets the feedback attachment's source ID to null, retaining the report text and diagnostic snapshot. Feedback is retained while the account exists; archiving is not deletion. User portable archives continue to exclude administrative feedback/diagnostics and screenshots; this should be considered explicitly when the broader SH-BETA-002 export contract is implemented. Infrastructure backup-copy removal remains subject to the existing deployment retention work.
- Diagnostics, screenshots, and pattern consent each start unchecked. Required record metadata is account, server time, server version, allow-listed page name, and authorized report associations. Optional diagnostics add a maximum 512-character user agent, bounded screen dimensions, import lifecycle metadata, and an error association. Disclosure version 1 is stored even on opt-out. No URL/query/fragment is submitted; unknown route strings become `unknown`.
- Server error associations use a 24-hour Data Protection token binding the internal user, code, and occurrence ID. A forged, expired, or different-account token cannot associate a report with another user's server diagnostics. Client occurrences use a `client-` UUID and are explicitly described as not identifying a server log. Neither kind of association exposes a log-fetch endpoint.

## Submission and save recovery

Each opened feedback draft has a UUID idempotency key. The per-user content lock and unique `(UserId, SubmissionKey)` index serialize duplicates; a payload hash rejects reuse for different content. Receipt timestamps use PostgreSQL microsecond precision, so a committed response lost in transit can be retried for the identical receipt. Screenshots and text either all commit or all fail. No selected attachment is silently dropped.

An ambiguous response freezes the attempted form and selected files for unchanged retry. Explicit validation/quota/source rejection unlocks the form so the user can remove an attachment or submit a general report. Closing a dirty draft asks for confirmation; successful submission displays a receipt and clears the draft. Native modal dialogs provide keyboard containment/Escape handling and restore the opening control's focus.

Selected save strategy: **block further mutations after an unconfirmed failure**, retaining the existing bounded in-memory progress batching and request IDs while saves are in flight. Strokes can continue during an ordinary save, but after failure the command handler refuses new edits until retry or explicit discard/reload succeeds. Progress retries reuse the original mutation ID; navigation waits for confirmation. Aggregate edits require reload after uncertainty, rather than blindly replaying a non-idempotent command. Saved text uses the timestamp of confirmed server state. Save failure remains visible even if another action changes the general error banner.

Progress and feedback drafts are **tab-memory only**, not durable offline queues. Refresh, crash, tab closure, or renderer failure can lose unconfirmed content; browser unload prompts are best effort. Session expiry keeps the mounted workspace/draft and offers same-account sign-in in another tab. Mutations carry an expected account header, checked server-side, so retrying an old tab after switching accounts cannot submit its work to the new account. The user can discard unconfirmed progress and reload the server's actual state, which may include a request whose response was lost.

## Onboarding and release communication

The page builder retains an exact layout draft and expected project revision. Retrying that unchanged command is allowed after a failure: an already-committed first attempt increments the revision, so replay is rejected rather than applied twice. A changed draft remains blocked until reload. This preserves the existing arrangement recovery workflow while enforcing the failure boundary.

Tour version 2 replaces the original text guide with the requested guided walkthrough. It highlights real controls and positions a nonmodal instruction card beside the current target. The four main steps remain: choose a pattern, highlight a symbol, mark stitches and review confirmed progress, and open Send Feedback. Individual clicks within those steps do not add extra numbered steps.

Discretionary decisions for the guided-tour update:

- The provided starter is the existing **little garden sampler**, not a copyrighted sample PDF from the repository. The first step highlights Import a pattern and then the sampler button inside the picker. A practice project is created only when the user clicks that button; reopening the tour does not automatically create projects. Choosing an existing project or uploading a PDF remains possible. A PDF must have usable stitches and pass explicit import confirmation before the viewer guidance proceeds.
- Starting the tour while already in a project uses that project. The tour explains that **Mark matching complete** changes the selected thread's matching stitches in the displayed scope and offers **Skip marking stitches**. This provides a keyboard-operable path without automatically changing progress or marking an entire pattern on the user's behalf. Undo retains its existing behavior.
- Guide advancement follows actual button activation and confirmed project state. The completion step retains the pre-click count and waits for persistence confirmation; loading, rejected creation, or failed saves cannot falsely advance it. Opening Send Feedback finishes the tour but does not submit any feedback.
- The highlighted control remains a normal clickable/focusable control. No full-screen dimmer intercepts other actions. **Focus highlighted control** lets keyboard users reach the target without guessing the tab order. The guide adds and restores `aria-describedby`, tracks scrolling/resizing and DOM changes, and pauses for unrelated native dialogs. Inside the pattern picker it renders within that dialog so the guide and sampler remain in the same accessible top layer. Ending the guide restores an appropriate focus target without closing the user's picker.
- The onboarding version advances from 1 to 2 because the interaction changed materially. Accounts that dismissed version 1 can see the new walkthrough once; completion or dismissal stores version 2. No database migration is needed. **Guided tour** in the footer voluntarily restarts it. Dismissal-write failure remains visible and retryable. In-progress tour position is session-only and resumes from the applicable starting step after a reload, unless dismissal/completion was persisted.

Announcements use server UTC and a half-open `[StartsAt, EndsAt)` window. Only the latest eligible published active entry is considered, with ID as a deterministic tie-breaker. If that entry was dismissed, an older entry is not resurrected. Dismissals are per account and announcement. The UI refreshes eligibility every minute. Message content is immutable after creation; admins may activate/deactivate or publish a draft. A changed message is a new announcement ID and can be shown again. Links are HTTPS without embedded credentials or exactly `/whats-new`; external links use `noopener noreferrer`.

Release entries are authored as drafts or published entries through `/admin/beta`. Published entries are immutable and appear at the authenticated, beta-authorized `/whats-new` page. The intentionally limited Markdown renderer creates only escaped text, headings, paragraphs, and bullet lists. Raw HTML, image markup, and Markdown links remain inert text. This sanitizes by construction and avoids a raw-HTML rendering path. Privacy/Terms and the retention page remain publicly accessible.

## Access and operator setup

`BetaAdmin` is separate from `LegalEditor`. Authorization checks persisted role membership on every request, so role changes do not rely on reissuing a cookie. Provision an existing account by internal GUID:

```powershell
dotnet run --project server -- --beta-admin <internal-user-guid>
dotnet run --project server -- --remove-beta-admin <internal-user-guid>
```

Production beta admission uses `Beta:AllowedEmails` from trusted server configuration (for example `Beta__AllowedEmails__0=tester@example.com`). Email is checked against the signed-in user's server profile, never supplied by a feedback form. A BetaAdmin has beta access. An empty list admits all signed-in accounts only in Development/Testing; production defaults to denied access. This minimal dependency gate will be replaced by the persisted allow-list/suspension model in SH-BETA-003. Export/account-deletion and legal endpoints retain their existing access exceptions.

Set `Beta:SupportContact` to the actual operator contact before invitations; the development fallback says to contact the person who invited the user. No messages are sent automatically. `Beta:AppVersion` defaults to the server assembly's informational version, including the Docker `APP_VERSION` build argument, and may be explicitly configured with the deployed build identifier.

Apply migration `20260912062607_BetaExperienceFeedback` using the existing `--migrate` release command before running this build. The migration builds on the existing uncommitted versioned-legal-documents migration; both belong in the release. It adds the beta tables and an `unknown` parser-version default for historical imports. Rollback drops beta feedback and attachments, so preserve the database before any rollback.

## Error catalog

All API failures have a safe message, HTTP category, stable code, unique reference, and deployed version. General logs contain code/reference/version, not feedback text, attachments, raw request bodies, or exception dumps. The UI offers retry/reload and safe-detail copying. A render error fallback mounts its own feedback control independently of the failed main application and always offers the configured support contact when available.

| HTTP | Code | Recovery |
| --- | --- | --- |
| 400 | VALIDATION_FAILED | Correct the input; refresh expired CSRF verification if needed. |
| 401 | SESSION_EXPIRED | Sign in to the same account; keep this tab and retry. |
| 403 | ACCESS_REQUIRED | Contact the operator for beta/admin access. |
| 404 | NOT_FOUND | Return to projects or remove the deleted report association. |
| 409 | CONFLICT | Reload confirmed state or explicitly update rejected feedback choices. |
| 413 | LIMIT_EXCEEDED | Remove/shrink the attachment or free storage; text-only feedback remains available. |
| 428 | LEGAL_ACCEPTANCE_REQUIRED | Review and accept the current legal documents. |
| 503 | UNAVAILABLE | Keep the draft/tab open and retry. |
| Other server failure | UNEXPECTED_FAILURE | Keep pending work; retry or report the reference. |
| Client network/render failure | NETWORK_FAILURE / CLIENT_RENDER_FAILURE | Check connection/reload as appropriate; client references do not claim server-log availability. |

## Validation

`tests/BetaExperienceTests.cs` covers authenticated/CSRF/admin boundaries, ownership, explicit consent, safe diagnostic association, concurrent idempotency, exact receipts, image metadata removal, invalid attachments, pixel/quota limits, text-only submissions at quota, account/project deletion, completion/archive/reopen transitions, announcement ordering/dismissal, onboarding persistence, and release publication filtering.

`web/tests/beta.spec.ts` covers keyboard feedback/focus restoration, a committed response dropped before reaching the client, no query/fragment capture, draft-discard confirmation, screenshot removal, same-tab draft survival through session expiry, unavailable tour targets, persisted dismissal, failed-import source consent, administrator completion/archive filtering, and inert malicious release Markdown. The administrator browser case uses `STITCH_TEST_BETA_ADMIN=true` with the separate TestSupport fixture executable.

Final verification on 2026-09-12:

```powershell
$env:STITCH_TEST_BETA_ADMIN='true'
$env:STITCH_TEST_LEGAL_EDITOR='true'
./Test.ps1 -SkipInstall
```

- Production frontend build passed.
- 92 backend tests passed; none skipped.
- All 34 Chrome browser tests passed, including the conditional legal-editor workflow; none skipped.
- Daily and weekly archive generation, server restart, shared-cookie continuity, persisted project/inventory comparison, and both archive downloads passed.
- Desktop and 390-pixel-wide mobile feedback form screenshots were inspected; no horizontal dialog overflow. Screenshots: `artifacts/beta-feedback-desktop.png` and `artifacts/beta-feedback-mobile.png`.
- Full-run logs and retained isolated test state: `artifacts/test-run-5606cb74160e4c538fd755faa965a523`.
- Existing xUnit analyzer warnings in `PostgresTests.cs` remain; no build/test failures.

The final public-support exemption also passed all 15 targeted beta/legal backend regressions: required legal acceptance still blocks release access while the public support/version endpoint remains available to the error fallback.

The production deployment has not been migrated or configured by this task. Before invitations, apply the migration and configure the administrator, beta email list, actual support contact, and deployed version using the setup above. Live Google/cloud-storage and infrastructure recovery/retention gates from the other preproduction specifications still require their own deployment evidence.

### Guided-tour update validation

The version 2 walkthrough passed the same complete validation command with both administrator fixtures enabled: 92 backend tests and all 37 Chrome browser tests, with none skipped. The four dedicated cases in `web/tests/guided-tour.spec.ts` cover the full sampler flow, delayed-save confirmation, keyboard navigation inside the native dialog, mobile target/card separation, failed sampler creation, highlight cleanup, previous-version redisplay, unrelated-dialog pause/resume, and persisted dismissal. Other browser tests explicitly use an account with onboarding dismissed, so they test their own workflows independently of onboarding state.

The mobile screenshot `artifacts/guided-tour-sampler-mobile.png` was visually inspected: the guidance sits above the highlighted sampler button without covering that button or causing horizontal overflow. Full logs and isolated test state are under `artifacts/test-run-68a02dc133a045a29db3b15a9e24d128`. This update changes the tour version constant and frontend behavior, so rebuilding/restarting through `Start.ps1` is sufficient; it adds no database migration.

## Feedback details redesign

The administrator's “Feedback details” dialog now uses a dedicated `FeedbackDetails` component and scoped styles. Discretionary choices for this design:

- Keep the app's cream and forest-green palette, serif heading, and restrained icons. Widen the desktop dialog to 920px so the message and report context have separate columns; stack them on phones.
- Give the sender's message the most space, preserve paragraph breaks, and wrap long references. Show the category and current status as labeled badges, with a short report reference above the message.
- Put received time, originating screen, app version, and the exact sender account ID in a compact context panel. The existing API supplies an account ID, so the UI does not invent a name or email.
- Replace the automatic database-property dump with explicitly labeled fields. Technical diagnostics and full references are collapsed until requested; absent optional values are omitted. Malformed legacy import metadata does not prevent reading the report.
- Present shared files as attachment cards with explicit Open links and a clear removed-source state. Opening the dialog never fetches an attachment. Existing authorization and access recording remain in effect.
- Keep status actions visible while the report body scrolls. Completion is the primary action; read/unread and archive are secondary. A read report offers “Mark unread”; other states offer “Mark read” to reopen or triage it. Current completion/archive actions are disabled. Status changes remain internal and send no email.
- Retain native dialog keyboard behavior and Escape dismissal, prevent duplicate actions while saving, and show failed updates inside the dialog without changing its confirmed status. Capture the opening button before loading disables it; restore focus there on close, or to the status filter when the report has left the current list.

This is a frontend change with no database migration. Built assets are regenerated by `Start.ps1` as usual.

The browser regression in `web/tests/beta.spec.ts` checks paragraph preservation, collapsed diagnostics, malformed legacy metadata, long references on a 390px viewport, available/removed attachments, no unsolicited attachment requests, visible mobile actions, failed status saves, and focus restoration. The existing completion/archive test also checks the filter focus fallback. Desktop and phone screenshots are saved to `artifacts/feedback-details-desktop.png` and `artifacts/feedback-details-mobile.png`.

Validation on 2026-09-12: production build and all 92 backend tests passed. The final full browser run passed 37 of 38 cases, including every feedback case; the unrelated large-pattern reload assertion exceeded its five-second wait (`artifacts/test-run-25d0af7f3ae846648f8fbddfecf2d5dd`). A focused rerun of all six feedback cases plus that large-pattern case passed all seven without changing the import implementation or its test. Daily/weekly backups, restart continuity, and archive downloads also passed in that rerun (`artifacts/test-run-6e53e9a01d9a4ef28a649f5b5fd298fe`). Desktop and mobile screenshots were visually inspected.
