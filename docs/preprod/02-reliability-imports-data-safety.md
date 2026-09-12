# Feature Specification — Reliability, Imports & Data Safety

**Feature ID:** SH-BETA-002  
**Status:** Ready for implementation  
**Depends on:** Existing user-owned persistence, private asset storage, and hosted PostgreSQL architecture

## Purpose

Make progress persistence trustworthy and PDF imports diagnosable, retryable, and safely reprocessable without damaging an active project.

## In scope

- Shared error/correlation contract
- Structured, sanitized logging
- First-class import-run lifecycle and diagnostics
- Successful-with-warnings outcomes
- Retry from the stored private source PDF
- Reprocess with the latest parser into a candidate revision
- Safe progress migration only after proven equivalence
- Explicit save/connectivity state and no silent data loss
- Transactional project operations
- On-demand current data export
- Configurable optional scheduled user backups
- Infrastructure recovery plan and rehearsal

## Out of scope

- True offline-first synchronization
- Conflict-rich offline reconciliation as a mandatory beta capability
- User-visible backup history beyond already available enabled backups
- Self-service restore/import from an export
- Formal admin data-integrity scanner
- Automatic parser correction without user review

## User stories

- As a user, I can work confidently because the app distinguishes confirmed saves from pending or failed writes.
- As a user, I can open a usable pattern even if import warnings require later review.
- As a user, I can retry an import without locating and uploading the PDF again.
- As a user, I can test a newer parser without risking my current project or progress.
- As the operator, I can identify the stage and reason for a failed import.
- As a user, I can download my complete current dataset regardless of scheduled-backup configuration.

## Functional requirements

### FR-001 — Error contract

Every significant server failure produces:

- a stable sanitized error code;
- a unique correlation/occurrence ID;
- a safe user message;
- structured server diagnostics.

Correlation must support tracing user, request, project, import run, feedback submission, and relevant background job without exposing private content.

### FR-002 — Import run

Each parsing attempt is a separate immutable-in-identity `ImportRun` associated with the user, project/pattern, source asset, and parser version.

Lifecycle states are:

```text
Pending -> Processing -> Succeeded
                      -> SucceededWithWarnings
                      -> Failed
                      -> Cancelled (only when cancellation is supported safely)
```

Persist stage transitions, timestamps, warning codes, failure code/stage, and bounded diagnostic metadata.

### FR-003 — User-visible import outcome

- `Succeeded`: allow normal use.
- `SucceededWithWarnings`: allow access when the produced chart is usable; clearly present warnings and a review action.
- `Failed`: do not create or replace the active pattern revision; show safe failure details and retry options.

Warnings may never be hidden merely to present a successful state.

### FR-004 — Retry stored source

An authorized user may retry a failed or warning import using the original stored private PDF. The new attempt creates a new import run and does not overwrite prior diagnostic history.

If the source asset no longer exists or fails integrity validation, require re-upload and explain why.

### FR-005 — Reprocessing and candidate revision

**Reprocess with latest parser** creates a candidate normalized pattern revision. The currently active revision and projects remain untouched while reprocessing or review is incomplete.

The user can:

- inspect import warnings and structural differences;
- accept the candidate;
- reject/discard the candidate without changing the active revision.

Acceptance is transactional. Failure leaves the prior active revision intact.

This candidate-revision approach is confirmed for the beta trial. At acceptance, revalidate the source revision and read the latest confirmed progress; if either changed during review, recompute the assessment or require a refreshed review. Never apply a stale progress snapshot over newer work.

### FR-006 — Progress migration proof

Progress may transfer automatically only when the system can prove that every migrated stitch maps deterministically to the same logical cell and stitch identity.

At minimum, compare normalized dimensions, coordinates, stitch types, symbol/thread mapping identities, and any other field that affects progress semantics. Use a deterministic structural fingerprint/versioned equivalence algorithm.

If equivalence cannot be proven:

- do not guess;
- warn that progress cannot be safely transferred;
- require acceptance with a clearly stated consequence, such as beginning progress on the new revision without migrated completion state;
- retain the existing revision/project until acceptance completes.

### FR-007 — Save acknowledgement

The client may show `Saved` only after the server confirms persistence. Mutations should be explicit and idempotent where practical. Acknowledged progress must survive refresh, login from another device, and application restart.

### FR-008 — Connectivity/save failure behavior

Preferred implementation: retain a bounded local queue of unsaved mutations, visibly mark them unsaved, and reconcile safely when connectivity returns.

Acceptable beta fallback: upon confirmed save/connectivity failure, display a blocking failure state and disable further mutations until connectivity/persistence is restored.

The implementation may choose either approach, but must never silently discard, silently overwrite, or falsely acknowledge changes.

If a local queue is implemented, it is not an offline-first database and must include ordering, idempotency keys, conflict handling, bounded storage, and clear discard/retry UX.

### FR-009 — Atomicity and integrity

- Import activation, revision acceptance, bulk mapping changes, reset, and deletion use transactions or compensating operations.
- A failure cannot leave a partially active candidate revision.
- Temporary processing artifacts are cleaned up safely.
- Persistent asset/database references remain consistent across failed operations.

### FR-010 — Current user export

An authenticated user can generate and download a current, versioned, application-level archive containing all owned product data and required private assets. It excludes other users' data and authentication/session secrets.

Archive generation verifies its manifest and checksums before offering the file. Export remains mandatory regardless of scheduled-backup settings.

### FR-011 — Scheduled user backups

Scheduled daily/weekly user archives are optional and configurable:

```text
Backups.Enabled
Backups.DailyEnabled
Backups.WeeklyEnabled
Backups.RetentionDays
```

When disabled, the UI must not imply that scheduled user backups exist. Existing good artifacts follow the configured retention/deletion policy. When enabled, prior safety rules remain: a failed replacement never destroys the last successful artifact.

Scheduled user backups are not a beta launch blocker.

This explicitly supersedes the earlier mandatory daily/weekly scheduling requirement. Existing user download/export capabilities remain in scope; no new backup-history experience is required. Disabling scheduling does not satisfy or waive the infrastructure recovery requirement below.

### FR-012 — Infrastructure recovery

Before beta, document and rehearse recovery for authoritative PostgreSQL data and private object storage. The selected mechanism may be hosting-provider snapshots/backups or another operational design.

Record:

- services/data covered;
- backup mechanism and cadence;
- retention;
- restoration steps and required access;
- expected recovery point/time objectives, even if provisional;
- date and result of the latest rehearsal.

This is separate from user-downloadable exports.

### FR-013 — Unknown save outcomes and concurrent clients

Define a mutation ID scoped to user/project and an expected project/revision version. A successful response returns the committed version and saved timestamp. Retrying the same mutation after the response is lost must return its existing result without toggling stitches again. Reusing a mutation ID with a different payload is rejected. Prefer commands expressing the intended completion state over unqualified toggles.

An old tab must not overwrite a newer acknowledged edit. Reject stale writes with a stable conflict code and preserve pending input for review; automatic merging is optional only when demonstrably safe. No SignalR or live collaboration is required.

Distinguish pending, confirmed, failed, and unknown-outcome writes. In the mutation-blocking fallback, preserve the already-entered failed batch in the current session and offer retry or explicit discard; merely disabling future input is insufficient. Resolve outstanding outcomes before candidate acceptance, reset, or deletion. Scope any local queue to the original user, project and revision; another signed-in user must never inherit or replay it. Local durable recovery across refresh/crash is optional, and the UI must state its limits.

### FR-014 — Durable import execution and source retention

Persist job intent with import creation using the existing durable job mechanism or an equivalent transactional handoff. Workers claim bounded leases, record heartbeat/progress, and make terminal transitions idempotent. Recover expired leases after restart; cap attempts and expose final failure rather than leaving Processing forever. Polling is sufficient for beta.

Recheck project existence, account eligibility, and source authorization before publishing output. A worker finishing after deletion cannot recreate a project or asset reference. Validate worker output before activation: a usable chart has coherent dimensions, valid coordinates/types and an inspectable representation; unresolved mappings can remain warnings, but invalid geometry cannot be declared usable.

A PDF admitted by safety/validation but failing chart extraction remains privately retained, counted toward quota, and available for download/retry/explicit feedback attachment. A rejected or blocked file is not an ordinary retained source; remove it after bounded diagnostic handling. Distinguish these cases in user messaging. Rejected candidates release their exclusively owned assets and quota; retained diagnostic records do not retain hidden content copies.

### FR-015 — Revision ownership, edits, and acceptance

Map Pattern, Project, and PatternRevision to the actual repository before implementation. Two independent projects based on one PDF must remain independent: accepting a candidate changes only the selected project's active revision, even if immutable source bytes are shared within the same account.

Candidate review summarizes dimensions, stitch counts/types, unresolved mappings, progress-transfer result, and the fate of user color/symbol edits. Preserve edits only through deterministic correspondence; otherwise disclose the specific reset/loss and require explicit acceptance. Never silently discard manual mappings while transferring progress. Undo history must not apply commands against incompatible revisions; mark acceptance as a clear history boundary if history cannot be carried safely. Discarding a candidate changes neither project.

### FR-016 — Coherent exports and archive recovery verification

Produce the archive from a consistent logical snapshot. Include a schema version, export ID, UTC snapshot time, inventory/bobbin counts and locations, projects, progress, supported edit/history data, normalized patterns, and retained source assets including safe failed imports. Document every included/excluded category; omit internal admin notes, security records, secrets, and operational logs. Snapshot metadata alone is insufficient if referenced asset bytes can change mid-export: pin immutable versions or restart safely.

Use ExportJob states Queued, Running, Succeeded, Failed, Expired with owner, snapshot version/time, manifest references and expiry. Authorize job polling and every download. Failed/partial archives are never advertised as complete; interrupted generation cleans up temporary bytes. Export must work even at quota, subject to separate temporary storage/concurrency limits. Deletion invalidates affected generated archives and prevents in-flight exports publishing stale deleted content.

In addition to infrastructure recovery rehearsal, use an internal test harness to reconstruct representative project progress, mappings, inventory and source assets from an exported archive. Check semantic values and references, not just ZIP/checksum validity. This does not add self-service restore UI.

When scheduled user backups are enabled, preserve the previously selected single latest daily and single latest weekly artifact, with failed replacement preserving the last good artifact and the existing 30-day expiry policy. Define that expiry as a maximum age for stale artifacts, not a request to retain 30 daily copies. Account retention (six months) is separate from backup-file expiry. Show Disabled, Never succeeded, Running, Succeeded, Failed or Stale accurately.

### FR-017 — Cross-layer error contract

Publish one error catalog with code, safe message, retryability, HTTP category and recovery action. Cover validation, quota, unavailable source, expired session, forbidden access, concurrency conflict, worker timeout and unexpected failure. Keep codes distinct from occurrence IDs and use bounded field errors. Client network failures must not falsely claim server persistence or server-side diagnostic availability. Client and worker events carry app/parser versions independently.

## Domain model

These are conceptual contracts to map onto existing entities. If immutable revisions are shared, the active-revision pointer/state belongs to each project; a global PatternRevision.State must not activate a candidate in every project. Extend the existing job/version metadata to carry mutation IDs, expected versions, leases and export snapshots required above.

```text
ImportRun
  Id
  UserId
  PatternId?
  ProjectId?
  SourceAssetId
  ParserVersion
  Status
  CurrentStage
  StartedAt
  CompletedAt?
  WarningCodes[]
  FailureCode?
  CorrelationId
  DiagnosticData (bounded/sanitized)
  OutputPatternRevisionId?

PatternRevision
  Id
  PatternId
  SourceImportRunId
  ParserVersion
  State (Candidate | Active | Rejected)
  StructuralFingerprint
  CreatedAt
  ActivatedAt?

ProgressMigrationAssessment
  Id
  FromRevisionId
  ToRevisionId
  EquivalenceAlgorithmVersion
  IsEquivalent
  Reasons[]
  AssessedAt
```

## Observability requirements

Structured events should include codes rather than free-form content for import stage, outcome, parser version, duration, file size/page count, detected symbol/color counts, warning count, and failure category.

Do not log PDF bytes, extracted charts, pattern cells, screenshots, OAuth tokens, cookies, connection strings, or secrets. Filenames are permitted but should not be used as correlation keys.

## Failure scenarios

- Parser process crashes: import run becomes failed through timeout/worker recovery; active revision is unchanged.
- Retry submitted twice: idempotency prevents duplicate activation; distinct intentional attempts remain traceable.
- Candidate acceptance fails: rollback and retain old active revision.
- Migration equivalence is uncertain: no progress is transferred.
- Network drops during progress batching: pending state remains visible; use selected queue or mutation-blocking behavior.
- Export generation fails: no incomplete archive is offered; error code and reference ID are returned.
- Scheduled backup is disabled: current export continues to work.

## Acceptance criteria

- [ ] Each parse attempt creates a separately traceable import run.
- [ ] Import statuses include SucceededWithWarnings and Failed.
- [ ] A usable warning result can be opened and reviewed.
- [ ] Retrying uses the authorized stored source and preserves earlier import history.
- [ ] Reprocessing never mutates the active revision before acceptance.
- [ ] Rejecting a candidate leaves the current project unchanged.
- [ ] Progress saved while a candidate is under review is preserved or triggers renewed review; accepting a candidate cannot silently replace it with stale progress.
- [ ] Progress transfers only after versioned structural equivalence passes.
- [ ] A dimension, coordinate, stitch-type, or mapping difference that prevents proof blocks automatic migration.
- [ ] The UI never displays Saved before persistence confirmation.
- [ ] Save failure is visible and either safely queued or blocks further mutation.
- [ ] Acknowledged progress survives refresh, session renewal, and backend restart.
- [ ] Current export works when all scheduled-backup flags are disabled.
- [ ] Export validation detects a deliberately corrupted test archive.
- [ ] Cross-user export and source-asset access tests fail safely.
- [ ] A documented recovery rehearsal restores representative database records and private source assets.
- [ ] Dropping a successful save response and retrying produces exactly one intended mutation; an old tab cannot overwrite newer progress.
- [ ] The blocking save fallback preserves the failed batch for retry/discard and never transfers it to another account.
- [ ] Killing a worker mid-import and restarting reaches a bounded terminal outcome; deletion during processing cannot resurrect data.
- [ ] A safe extraction failure retains the source for download/reporting; a blocked file is not downloadable as an accepted source.
- [ ] Reprocessing one of two projects sharing a source leaves the other project's revision, edits and progress unchanged.
- [ ] Candidate acceptance explicitly handles manual mapping edits and incompatible undo history.
- [ ] Export during editing is coherent, includes inventory and failed-import sources, and passes semantic reconstruction in a test harness.
- [ ] Full user quota does not block current-data export; deletion during export prevents publication of deleted content.

## Deferred work

- Full offline editing and multi-device reconciliation
- Self-service export restore/import
- Automated data-integrity health scanner
- Parser-version batch reprocessing
- Detailed user-facing revision diff visualization beyond what beta review requires

## Open decisions

The implementer must record whether the beta uses a bounded local unsaved-mutation queue or mutation blocking. Either is compliant only if all FR-008 guarantees pass.
