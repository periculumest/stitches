# Feature Specification — Security, Privacy & Launch Guardrails

**Feature ID:** SH-BETA-004  
**Status:** Ready for implementation  
**Depends on:** Existing Google identity, server-side ownership, private asset storage, PostgreSQL, and hosted deployment architecture

## Purpose

Protect private pattern content and user data, bound resource consumption, establish clear deletion/retention behavior, and integrate the legal/privacy surfaces required for beta.

## In scope

- Server-side authorization for every user-owned resource
- PDF type/signature, size, page count, malformed/encrypted-file handling
- Configurable upload, quota, rate, and processing limits
- File-safety scanning abstraction
- Sensitive logging rules
- Session failure and CSRF protections
- Security headers and production-safe secrets/configuration
- Versioned Privacy Policy/Terms acceptance
- Immediate permanent project deletion with strong confirmation
- Account deletion with immediate lockout, six-month recoverable retention, and permanent purge
- Audit events for destructive and sensitive actions

## Out of scope

- Legal advice or final legal review
- Seven-day or other project recycle bin
- Self-service deleted-account restoration
- Public/shared patterns or projects
- Sophisticated external malware-scanner provider as a required beta dependency
- Commercial billing quotas

## Locked limits

Default beta values:

```text
Maximum PDF size: 50 MB
Maximum PDF pages: 250
Maximum counted storage per user: 1 GB
Deleted-account retention: 6 months
```

All limits must be server-side and configurable without source changes. Lower deployment-specific processing ceilings may be used only when clearly surfaced to users and documented.

## Functional requirements

### FR-001 — Ownership authorization

Every read, download, mutation, association, export, feedback attachment, and deletion involving user-owned data derives ownership from authenticated server context.

Use ownership-scoped queries. For inaccessible private IDs, prefer not-found behavior that does not reveal another user's resource.

### FR-002 — Upload validation

Before processing, validate:

- configured byte-size ceiling;
- allowed extension as a hint, not proof;
- declared MIME type;
- PDF file signature/content identification;
- page-count ceiling using a bounded parser;
- encrypted/password-protected status;
- malformed/truncated structure;
- storage quota;
- processing/rate eligibility.

Reject unsupported files with a stable error code, unique reference ID, and actionable safe message. Validation and parsing must not trust the original filename.

### FR-003 — Processing containment

Configure limits for processing time, memory/worker resources where supported, concurrent imports per user/system, retries, and temporary-file lifetime. A pathological PDF must not monopolize the web process or create unbounded retries.

Long-running import work should use the application's background-processing boundary rather than hold an interactive HTTP request indefinitely.

### FR-004 — Storage quota

Count:

- original PDFs;
- persisted user/project assets;
- user-uploaded screenshots and feedback files.

Do not count:

- temporary generated downloads after cleanup;
- operational logs;
- infrastructure/system backups;
- a feedback reference to an already counted pattern asset.

Quota calculation must be authoritative server-side. Failed/abandoned uploads and temporary artifacts are cleaned up and do not permanently consume quota.

### FR-005 — Rate limits

Apply configurable rate/concurrency limits to authentication-sensitive and expensive endpoints, especially upload, import/retry/reprocess, export, feedback attachment, and destructive operations.

Responses identify the category safely and indicate when retry is reasonable. Limits apply server-side even if the UI disables a button.

### FR-006 — File-safety scanning abstraction

Define a provider-neutral file scanning interface and explicit results such as `Clean`, `Blocked`, `Unavailable`, and `NotConfigured`.

The initial provider may be minimal/no-op only when configuration explicitly selects that mode and startup/health makes the reduced protection visible to the administrator. The application must not claim a file was scanned when it was not.

Blocked files are not parsed or made available. Unavailable-scanner behavior is configurable fail-closed/fail-open for beta and must be documented.

### FR-007 — Logging and diagnostics

Permitted when useful:

- original filename;
- opaque internal IDs;
- file size/page count;
- parser version/stage;
- error/warning codes;
- correlation IDs.

Prohibited:

- PDF bytes or rendered pages;
- extracted pattern/chart contents;
- screenshot contents;
- OAuth/access/refresh tokens;
- cookies, antiforgery tokens, secrets, or connection strings;
- raw authorization headers.

### FR-008 — Web/session protections

- HTTPS is required in hosted environments.
- Cookie-authenticated mutations use CSRF protection.
- Session expiration and failed Google callbacks show safe recovery UX.
- Apply appropriate secure cookie settings and standard security headers, including content-type protections, frame restrictions, referrer policy, and a deployment-appropriate Content Security Policy.
- Uploaded private files are never placed beneath the public web root.

### FR-009 — Terms and Privacy Policy

Privacy Policy and Terms of Service must be published at stable site URLs and linked from the application footer/account/authentication-related surfaces as applicable.

Record acceptance with:

```text
UserId
DocumentType
DocumentVersion
AcceptedAt
```

Material policy updates may require re-acceptance before continued product use. Reading public policy pages and completing required auth/account-deletion flows must remain possible when acceptance is pending.

The product UX must explain, consistently with the final policies, that uploaded patterns are stored privately, processed to create an interactive representation, and may be shared with the administrator only through explicit feedback consent.

### FR-010 — Permanent project deletion

Project deletion is immediate and permanent after deliberate confirmation. No project recycle bin or grace period is provided.

This final decision supersedes the earlier tentative seven-day recovery proposal. Restoring a deleted account must not resurrect projects permanently deleted before account deletion. Failed physical cleanup is an operational retry, never a user recovery window; keep the deleted project inaccessible throughout.

The UI must:

- identify the exact project;
- state that deletion cannot be undone;
- encourage downloading current data/pattern materials first;
- require a deliberate check such as typing `DELETE` or the project name;
- prevent accidental double submission.

The server reauthenticates ownership and performs deletion transactionally/with safe asset cleanup. Delete normalized/project data, progress, project-specific assets, revisions, and private references that are not legitimately shared by another owned aggregate. Preserve only legally/operationally required minimal audit records with no pattern content.

Where independent projects reference the same immutable source within one account, remove the deleted project's association and retain only bytes still owned by a surviving project. The confirmation must explain this distinction; deleting one project must neither erase another project nor claim that its still-used shared source vanished. Feedback references alone must not keep a deleted project's otherwise unowned source alive.

### FR-011 — Account deletion lifecycle

When a user confirms account deletion:

1. Strongly warn and encourage current-data export.
2. Require deliberate confirmation and recent authentication where practical.
3. Immediately disable application access and invalidate/reject active sessions.
4. Mark the account pending permanent deletion with `PurgeAfter = DeletedAt + 6 months`.
5. Retain the user's data and private assets during the six-month recovery window, inaccessible to the user through normal login.
6. Allow administrator-assisted restoration during that window after identity verification/support process.
7. Automatically and permanently purge user-owned records/assets after the deadline.

The retention period is configurable for automated testing but defaults to six calendar months in production.

### FR-012 — Account restoration

Restoration is administrator-only for beta. It:

- requires the account to be inside its retention window;
- clears pending-deletion state transactionally;
- restores access only if allow-list and suspension rules also permit it;
- records an audit event;
- does not silently change policy acceptance requirements.

### FR-013 — Permanent account purge

Purge removes user-owned product records, projects, progress, imports, feedback attachments, source assets, stored exports/backups, and account-linked private data. It also anonymizes or minimizes retained audit data according to operational/legal requirements.

The purge job must be idempotent, resumable, observable, and safe under multiple application instances. A partial failure remains retryable and keeps the account inaccessible.

### FR-014 — Configuration and secrets

Security/resource settings come from validated environment/platform configuration. Secrets are not committed, logged, returned to clients, or baked into images. Production startup fails clearly if mandatory secure configuration is missing; it must not silently fall back to public or ephemeral storage.

### FR-015 — Deletion across copies and concurrent work

Distinguish immediate irreversible product deletion from completion of physical cleanup. At confirmation commit, remove normal access, fence writes/jobs, record a durable deletion event and enqueue idempotent cleanup. Do not report physical erasure as finished while retries remain. The deletion event contains opaque resource identifiers and deletion time, never pattern contents.

Cover database rows, source/revision assets, thumbnails/previews, screenshot attachments linked to the deleted project, generated exports, latest daily/weekly user archives, temporary uploads, application caches and feedback attachment grants. Invalidate an archive containing a deleted project as a whole and regenerate it on demand from current data; a completed feedback status does not preserve content access. Textual feedback and safe diagnostics can remain until account purge; avoid retaining deleted filenames/content in new deletion audit events.

Prevent in-flight import/save/export jobs from recreating deleted data. Restoring infrastructure from an older snapshot must replay deletion and eligibility events before serving traffic. Keep a minimal independently recoverable deletion record for at least the maximum restorable snapshot lifetime and outstanding cleanup period; then remove/minimize it under the documented retention policy.

**Unresolved deployment dependency:** provider snapshots, object versions and immutable backups may have their own retention behavior. Before invitations, inventory these copies and document the actual maximum physical lifetime. Do not claim immediate erasure from every backup without evidence. If the selected provider cannot satisfy the intended deletion promise, the operator must choose a compatible storage/erasure design or explicitly approve narrower, accurately disclosed wording. This specification does not silently authorize a retained-project recovery period.

Copies users or admins already downloaded to their own devices are outside server deletion control. Do not promise their revocation.

### FR-016 — Retention boundaries and account purge completeness

Six calendar months applies to the retained account dataset, not just a deletion marker. The source assets needed for restoration cannot expire through unrelated stale-upload or project cleanup jobs. Stop normal product jobs while pending deletion; restoration does not restart cancelled imports/exports automatically. Old sessions remain invalid after restore and the user signs in again.

Compute and persist the UTC deadline using calendar-month addition (clamp an invalid resulting day to month end). Before the deadline, restore may win an atomic state transition; at or after it, restoration is denied even if the purge worker is late. Purge starts on the next bounded scheduled run; show overdue/failed work in admin health.

Purge covers feedback message bodies and attachments, access/restoration requests, linked emails, diagnostics and stored user exports as well as project data. Inventory shared references before deleting assets. Retained audit records must not retain unnecessary direct identifiers. The deployment record must set actual TTLs for operational logs, temporary uploads, export downloads and cleanup retries; temporary content cannot have indefinite retention by omission.

### FR-017 — Attachment safety and limits

All file paths are server-generated; encode original filenames safely when displayed or used as download labels. PNG/JPEG screenshots require signature verification, bounded decode, metadata stripping and safe re-encoding; reject active formats such as SVG/HTML. Apply quota reservation and scanning policy to screenshots as well as PDFs. Admin attachment viewing still requires report-specific consent and a live authorization check; general Admin membership alone is not consent to inspect arbitrary patterns.

Serve private content through authorization checks that enforce current account/project state; use safe download headers and prevent shared-cache storage. Raw PDFs must not run active content in the app's origin. Bound attachment count, compressed size, decoded pixel count and temporary-storage usage before allocation.

For unambiguous enforcement, existing 50 MB and 1 GB defaults mean 50,000,000 and 1,000,000,000 bytes; document any later switch to MiB/GiB in both UI and configuration. The 250-page limit is inclusive. Screenshot limits and worker limits are selected and recorded by the implementer from representative inputs before deployment; no limit may be absent/unbounded merely because this plan leaves its number configurable.

### FR-018 — Policy version integrity and retention messaging

Published policy versions are immutable and retain the exact text or content hash alongside type/version/date. An acceptance request must match the currently required version; a policy publication race triggers a refreshed review rather than silently accepting different text. Record explicit Terms acceptance and Privacy acknowledgment distinctly if the final wording requires that distinction.

The account deletion confirmation clearly says that access ends now, data is retained for six months, restoration requires admin help, and purge follows the deadline. Do not describe account deletion as immediate erasure. Project deletion copy separately reflects FR-010 and the deployment's verified backup-copy handling. These are consistency requirements; policy wording and its legal sufficiency are outside this document review.

## Domain model

```text
PolicyDocument
  Type
  Version
  PublishedAt
  IsMaterialChange

PolicyAcceptance
  UserId
  DocumentType
  DocumentVersion
  AcceptedAt

AccountDeletion
  UserId
  Status (PendingPurge | Restored | Purging | Purged | PurgeFailed)
  RequestedAt
  PurgeAfter
  RestoredAt?
  PurgedAt?
  CorrelationId

FileSafetyAssessment
  AssetId
  Provider
  ProviderVersion?
  Result
  AssessedAt
  ErrorCode?
```

## Required configuration

At minimum:

```text
Uploads.MaxPdfBytes
Uploads.MaxPdfPages
Storage.MaxUserBytes
Imports.MaxConcurrentPerUser
Imports.Timeout
RateLimits.*
FileScanning.Provider
FileScanning.UnavailableBehavior
Deletion.AccountRetentionMonths
```

Validate non-negative, internally consistent values at startup. Admin health displays effective non-secret values relevant to operations.

## Failure scenarios

- Extension says PDF but signature does not: reject before parsing.
- Page-count extraction is unsafe/too slow: abort within bounds and return a safe validation failure.
- User races two uploads near quota: authoritative reservation/transaction prevents exceeding quota.
- Project deletion partly fails in object storage: project remains inaccessible; cleanup is retried and observable without resurrecting it.
- Account purge partly fails: account stays locked and job resumes idempotently.
- Restore races purge deadline: one transaction/lock determines the winning state; never restore a partially purged account.
- Scanner is unavailable: follow configured behavior and report the effective state honestly.
- Policy version changes: require acceptance only when marked material and do not trap the user away from policy/deletion/logout paths.

## Acceptance criteria

- [ ] Cross-user authorization tests cover patterns, projects, progress, imports, exports, feedback attachments, and private assets.
- [ ] Files over 50 MB and PDFs over 250 pages are rejected by default with safe error/reference IDs.
- [ ] Spoofed extensions/MIME types do not bypass content validation.
- [ ] The 1 GB default quota is enforced under concurrent upload tests.
- [ ] Import/export/upload rate and concurrency limits are enforced server-side.
- [ ] File scanning has an explicit configured provider/mode and never falsely reports scanning.
- [ ] Logs contain no document contents, credentials, tokens, or cookies in automated redaction tests.
- [ ] Privacy and Terms links are reachable and acceptance version/timestamp is recorded.
- [ ] A material policy version can require re-acceptance.
- [ ] Project deletion requires deliberate confirmation and permanently removes the project with no recovery path.
- [ ] The project-deletion screen encourages export/download before confirmation.
- [ ] Account deletion immediately blocks existing and new sessions.
- [ ] Deleted-account data remains restorable by admin before six months.
- [ ] Restoration also respects allow-list and suspension state.
- [ ] Restoring a retained account does not restore an earlier permanently deleted project.
- [ ] The purge path permanently removes representative database records and private assets after the deadline.
- [ ] Purge retry is idempotent after an injected partial failure.
- [ ] Mandatory production secrets/configuration are validated at startup.
- [ ] Deletion during import/export/save blocks stale publication; feedback grants and generated archives cannot expose a deleted project's content.
- [ ] Deleting one project preserves an independent project sharing its source, without leaving feedback-only orphan content.
- [ ] A recovery rehearsal using a pre-deletion snapshot reapplies deletion events before product access is enabled.
- [ ] Retained-account assets survive the full recovery window; purge includes feedback text/attachments and account-linked requests.
- [ ] Calendar-month boundary and restore-versus-purge race tests use a controllable clock.
- [ ] Oversized/invalid screenshot decoding and unsafe filenames cannot bypass safety/quota rules or execute in admin UI.
- [ ] A policy change during acceptance cannot record acceptance of unseen text.

## Deferred work

- External enterprise-grade malware scanning provider if not selected for beta
- Self-service account restoration
- Project soft delete/recycle bin
- Advanced threat detection and security operations alerts
- Billing-driven quota tiers

## Open decisions

Before deployment, record the selected scanner-unavailable behavior, screenshot/worker/temporary-file limits, support route and concrete hosting-provider recovery/retention configuration. Resolve the backup-copy deletion compatibility in FR-015 before invitations; this is a launch dependency even though implementation of the deletion/restore boundaries can proceed now. Do not present this document review as legal approval of the six-month retention policy.
