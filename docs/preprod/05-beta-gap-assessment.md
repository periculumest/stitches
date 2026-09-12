# Stitch Helper — Beta Plan Gap Assessment

**Review date:** 2026-09-12  
**Scope:** SH-BETA-000 through SH-BETA-004, including the five final user clarifications  
**Review type:** Requirements and cross-domain consistency review; no source-code audit or runtime tests performed

## Assessment

The four execution domains remain a useful division. The most consequential gaps concerned interactions between otherwise well-described features: deleting data while jobs or backups retain it, restoring an account without product access, and preserving progress when requests or revisions race.

The specifications are now more precise about those interactions. This makes them suitable for implementation planning, not evidence that the application is ready for beta. The implementer must first map them to the actual repository and produce launch-test evidence.

## Findings and refinements

Priority describes impact if the gap were left unresolved. All existing launch requirements remain in force; this review does not silently move product features out of scope.

| ID | Priority | Gap in the reviewed plan | Consequence | Refinement and owner |
| --- | --- | --- | --- | --- |
| GAP-01 | P0 | Immediate deletion omitted backup copies, generated exports, feedback attachments and running jobs | Deleted content could remain accessible or reappear after recovery | Copy inventory, immediate access removal, durable cleanup, job fencing and replay of deletion records; SH-BETA-004 FR-015 |
| GAP-02 | P0 | Save failure did not distinguish failure from an unknown result; multiple tabs were unspecified | Retried toggles could undo progress; stale saves could overwrite newer work | Mutation identity, committed version, stale-write rejection and preservation of the failed batch; SH-BETA-002 FR-013 |
| GAP-03 | P0 | Account state rules were scattered; restoring a locked account lacked a request path | Login could bypass pending deletion or restoration could be unusable | Ordered access matrix, stable provider identity and limited restoration requests; SH-BETA-003 FR-014–015 |
| GAP-04 | P0 | Reprocessing addressed stitch progress but not independent projects, manual mappings or undo history | One acceptance could change another project or silently lose edits | Project-specific activation, explicit mapping-loss review and safe history boundaries; SH-BETA-002 FR-015 |
| GAP-05 | P0 | Import statuses existed without a durable execution/recovery contract or a definition of usable output | Crashed jobs could remain Processing or publish after deletion | Durable intent, leases, bounded recovery and output validation; SH-BETA-002 FR-014 |
| GAP-06 | P0 | Retry assumed a stored source; reporting assumed a working viewer | Users with the most important parser failures could not report or recover | Retain safe extraction failures; remove rejected/blocked files; report from import/error details; SH-BETA-001 FR-013 and SH-BETA-002 FR-014 |
| GAP-07 | P0 | Export completeness and consistency were underspecified; checksums alone were the explicit archive check | Inventory could be omitted or an intact ZIP could hold inconsistent project state | Snapshot semantics, inventory/source coverage, authorized job lifecycle and semantic reconstruction test; SH-BETA-002 FR-016 |
| GAP-08 | P0 | Six-month account retention had no interaction with asset TTLs, deadline races or feedback text purge | A nominally restorable account could have missing files or residual private data after purge | Retention boundaries, calendar deadlines, atomic restore/purge and explicit data coverage; SH-BETA-004 FR-016 |
| GAP-09 | P0 | Screenshot support inherited PDF-centric validation; optional diagnostics had become automatic metadata collection | Unsafe attachments, content leakage or unexpected collection | Separate diagnostic choice, safe route capture, bounded image decode and attachment authorization; SH-BETA-001 FR-002/014 and SH-BETA-004 FR-017 |
| GAP-10 | P1 | Announcement ordering lacked a publication timestamp; dismissal/edit behavior and What's New access were ambiguous | Different implementations could display different banners or expose drafts | Deterministic UTC publication rules, explicit authenticated access and consistent sanitization; SH-BETA-003 FR-016 |
| GAP-11 | P0 | Health lacked freshness and release management lacked schema/recovery behavior | A dead worker could look healthy; code rollback could fail against changed schema | Unknown/Stale states, purge/cleanup visibility, administrator recovery and deploy rehearsal; SH-BETA-003 FR-015/017 |
| GAP-12 | P1 | Feedback retries, keyboard behavior and tour steps without a pattern were unspecified | Duplicate tickets and blocked first-run experiences | Idempotent submission, bounded drafts, focus handling and available-target onboarding; SH-BETA-001 FR-014–015 |

## Confirmed decisions preserved

- Immediate permanent project deletion with strong confirmation and an export reminder; no recycle bin.
- Six-calendar-month retained accounts, immediately blocked product access, administrator-assisted restoration and eventual purge.
- Unread, Read, Completed and Archived feedback; completed tickets stay findable after archive.
- Optional/configurable scheduled backups; mandatory current-data export and tested recovery.
- Candidate revisions and progress migration only when equivalence is proven.
- Global feature flags, one administrator, no email notifications, no user-visible ticket history, no full offline synchronization or SignalR requirement.

The new failure-handling details are engineering refinements to these decisions. They are not new user answers. Provider-specific policies below remain unresolved until deployment selection.

## Remaining choices and evidence

| Owner | Item | What must be recorded | Timing |
| --- | --- | --- | --- |
| Implementer | Save-failure strategy | Queue or mutation blocking; pending-batch behavior; actual refresh/crash limits; conflict/retry tests | Before implementing save behavior |
| Implementer | Existing architecture | Current Pattern/Project/revision ownership, durable job mechanism, export format and schema migration mapping | Before new entity/API work |
| Implementer | Resource envelope | Screenshot count/bytes/pixels, worker time/memory/concurrency, temporary TTLs, flag cache interval | Before hosted validation |
| Operator | Backup-copy deletion | Snapshot/object-version inventory and maximum lifetime; compatibility with the immediate deletion promise | Before invitations |
| Operator | Recovery and retained-data cost | Provider mechanism, restore steps, objectives, rehearsal result and capacity for six months of retained accounts | Before invitations |
| Operator | File scanning | Selected provider/no-op mode and unavailable-provider behavior, accurately shown in health | Before invitations |
| Operator | Support and admin continuity | Actual contact route, original-identity verification process, sole-admin recovery procedure and manual health-check cadence | Before invitations |

The backup-copy issue is the main unresolved product/deployment boundary. First establish what the selected host supports. If immediate physical removal of every retained copy is incompatible with that host, explicitly choose a compatible technical design or approve accurate narrower wording. Do not implement a hidden retention exception. This review does not assess the legal sufficiency of deletion or retention policies.

## Minimum adversarial launch rehearsal

Use controlled test accounts and a clock that can be advanced; record expected and actual results against the owning acceptance criteria.

1. Commit a stitch mutation, drop its HTTP response, retry it, and then submit a stale edit from another tab.
2. Edit progress and manual mappings while a candidate is being reviewed; accept it in one of two independent projects sharing a source.
3. Kill and restart an import worker; separately delete the project while import/export jobs are running.
4. Produce an export during edits, reconstruct its logical data and assets, and verify inventory and source PDFs as well as stitch counts.
5. Delete a project with feedback attachments and existing generated archives; restore an earlier infrastructure snapshot and apply deletion records before enabling access.
6. Delete an account, request restoration through restricted sign-in, test the six-month deadline race, and verify no old sessions reactivate after restoration.
7. Fill the user quota, submit text-only feedback, export current data, and reject oversized/malformed screenshots safely.
8. Upgrade schema and application version, then exercise the documented compatible rollback or forward-recovery path.

## Handoff

Use the five revised specifications as the implementation source of truth. Keep this assessment as the rationale and risk map. Implement the shared contracts first, then their UI/admin consumers, and attach real test evidence to the launch gate. No additional feature expansion is needed to resolve these findings.
