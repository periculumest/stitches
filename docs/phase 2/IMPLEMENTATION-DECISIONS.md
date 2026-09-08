# Phase 2 implementation decisions

Date: 2026-09-07. Implements SH-IDENTITY-002. This ledger supplements `00-decisions-and-scope.md`; the explicit changes below supersede corresponding phase 1 behavior.

## Identity and ownership

- Google supplies sign-in identity only. PostgreSQL and private object storage hold product data; users do not grant access to Drive, Gmail, or other Google services.
- `ApplicationUser : IdentityUser<Guid>` is the owner. Google mappings use Identity's external-login table keyed by provider subject. Email is a mutable profile snapshot and never links accounts.
- Concurrent first callbacks acquire a PostgreSQL transaction advisory lock on the provider/subject before resolving or creating the account.
- Application cookies have a fixed 30-day sliding policy. `Authentication__SessionDays` is deliberately not configurable: changing that requires an explicit product decision. Production uses a Secure, HttpOnly, SameSite=Lax `__Host-` cookie. CSRF uses ASP.NET antiforgery with a centralized client header, including upload, export, and logout.
- Every repository is request-scoped and obtains its owner from `ICurrentUserContext`. Composite foreign keys prevent a project referencing a foreign pattern and a pattern referencing a foreign source asset. Missing/foreign resources return 404.
- There is no development guest login. The separate `tests/TestSupport` executable generates cookie fixtures against databases named `stitch_test_*`; it is excluded from the application image. No test-authentication endpoint or header bypass exists in the server.

## PostgreSQL model and migration

- EF Core/Npgsql replaces SQLite entirely. There is no SQLite import, ownership claim, or legacy restore path. Existing `.data` files are left untouched on disk.
- Versioned relational rows contain owners, relationships, revisions, timestamps, assets, imports, catalog, inventory, preferences, retained backups, job receipts, and pending object deletions. Pattern charts and project metadata/history use explicit application JSON in PostgreSQL `jsonb` columns.
- This keeps the proven importer/command model while avoiding tens of thousands of EF-tracked stitch-definition rows. Thread components are nested in normalized chart JSON, have stable IDs, and are validated by the command layer.
- An immutable pattern can back multiple independently corrected projects. Project-specific chart corrections and their bounded undo history remain in the project; original normalized source data remains in the pattern. Exports include both.
- Progress is separate: `(ProjectId, StitchId)` rows exist only for completed stitches. Inventory uses `(UserId, Code)`. Cleared inventory entries retain a versioned zero-value row so stale editors cannot resurrect old stock.
- The current catalog is DMC-only, so the existing canonical DMC code remains its physical-thread key. Supporting another brand requires a reviewed catalog-key migration; it must not overload DMC codes.
- The catalog is shared reference data and is updated only by the release/migration command. The former user-facing “Add a DMC color” action was removed to prevent one account changing everyone else's reference data.
- `--migrate` applies EF migrations and seeds/updates the catalog under an advisory lock. Normal web startup never creates or migrates schema. The initial down migration is developer tooling, not a production rollback strategy.

## Progress and concurrency

- Progress requests contain a client-generated request UUID and explicit final stitch states. The server serializes transactions for each owned project with a PostgreSQL row lock. Different projects remain independent.
- Narrow SQL upserts/deletes affect only changed stitches. Progress does not rewrite the chart or a serialized complete-progress document. A small metadata/history document records undo and milestones.
- The latest transaction to acquire the project lock and commit determines same-stitch state. Disjoint stitch updates both survive. A committed request UUID is recorded with a payload hash; retries neither repeat history nor overwrite a later operation. Reusing a UUID with another payload returns 409.
- Aggregate commands, including undo/redo, require the current revision. Progress increments that revision but accepts no stale whole-project body. `DataRevision` separately identifies chart changes. Undo of progress also leaves chart JSON untouched.
- React buffers strokes for 300 ms, assigns final states, and serializes batches. It can collect additional painting during a save. Transient failures get one automatic retry with the same request ID; persistent failures retain the pending batch and offer explicit retry. Unsaved changes trigger the browser's leave-page warning. This is an in-memory buffer, not an offline queue.
- Progress saves have their own saving state and do not set the application-wide busy flag. Painting, inspection, tool selection, and filters remain available without disabling/fading the page. Navigation, sign-out, and revision-based edits first await the shared progress-save promise; failed saves keep the workspace and pending changes available for retry. Edits use the confirmed revision after the save finishes.
- The save-status area reserves a fixed 13rem width, including in focus mode, so switching between saving, saved, and attention messages does not move the progress tracker or surrounding header controls.
- Opening a project fetches its complete state. Focus and a 30-second active timer call the lightweight state endpoint; unchanged revisions return 204. That endpoint does not load or transfer chart JSON. Changed chart revisions trigger a full fetch. Responses that race with a local mutation are discarded.
- Undo history is shared within the same user's project and capped at 100 operations. A stale undo conflicts, requiring a refresh before the user can undo the latest recorded operation.

## Blends and imports

- Every stitch definition has one or more physical thread components. One component is `Single`; two or more is `Blend`. The editor supports up to 12 components; three-component blends are tested. Components have unique IDs, distinct thread codes, and nullable positive strand counts.
- The legacy `threadCode` field remains a compatibility projection of the first component for importer/display code. Components are authoritative for rendering, inventory, validation, and substitution.
- Striped colors use actual component catalog colors, with the original symbol rendered over them. No synthetic mixed RGB catalog entry is generated.
- Substitutions are keyed by component ID, including single-thread usages. Substituting one occurrence does not replace other occurrences of the same physical thread in other definitions.
- Explicit `310 + 321` compositions are supported in inline legends and chart-table number columns. Parenthesized counts or corresponding tabular strand counts are preserved. Unknown counts remain null. Ambiguous repeated symbols and unsupported continuation-row layouts are retained for review rather than inferred as blends. Broad publisher compatibility is still outside this release.
- Table usage-count validation counts every physical component used by a stitch.

## Cross Stitch Professional import support

- Recognize the exporter identification and repeated `Sym / No. / Colour Name` key columns. Match each symbol by its font and encoded character, including numeric characters, and preserve source outlines in a shared font frame. Read the explicit DMC palette, design dimensions, and cross-stitch strand count from the instructions; unknown strand counts remain unspecified.
- Use crossing vector grid lines to locate cells. Symbol bearings vary within this format, so their baselines are mapped into grid cells instead of treating every distinct glyph position as a grid column.
- Assemble tiles using one-based printed row/column numbers, independently of PDF page order. Validate page footprints against the declared dimensions; empty fabric is allowed, missing page coverage is rejected. Matching overlap stitches are deduplicated; conflicting overlaps, duplicate cell symbols, missing key mappings, and unverified placement stop import with an explanation.
- The supplied `Iron Man (1).pdf` has 48 chart pages plus its key: 450 × 450, 202,500 stitches, 63 DMC definitions, and two strands per cross. All 63 source symbols are retained. No per-color usage summary is printed, so validation uses geometry, source symbol tallies, and known key mappings rather than claiming a usage-summary comparison.
- This adapter imports full crosses and keeps the existing review step. Existing imports are not rewritten; re-upload the PDF to create a correctly assembled project. The generic vector-grid and earlier chart-table adapters remain available for their existing formats.

## Visual import page builder

- Entry point: **Arrange pages** in the import review banner. A modal board keeps navigation and confirmation outside the draft. Tiles depict extracted stitches and retain source PDF page numbers; no additional PDF renderer dependency is introduced.
- Retain the current arrangement initially. Optional row layout uses an explicitly selected top-left page and PDF-number order. Provide pointer snapping, keyboard movement, precise offsets, neighbor placement, overlap adjustment, zoom/fit, local undo, reset, and discard confirmation.
- Save all positions in one revision-checked `layout` command. The backend validates the complete page set, source ownership, bounds, and conflicting stitch positions before changing the project. One history entry supports undo/redo and increments the chart revision for other sessions. Arrangement history cannot be undone while completion remains attached to stitches.
- Preserve only repeated source stitch instances in `PageOverlapStitches`, alongside the visible chart, and mark new imports `PageSourcesComplete`. All three existing adapters populate this representation. Rearranging uses each instance's source-page ID; matching overlaps merge again while hidden instances remain recoverable. Review-time edits to shared stitches update those instances together.
- Older overlapping imports are rejected with a re-import explanation because their discarded source instances cannot be reconstructed reliably. Non-overlapping legacy imports remain usable when source ownership is known. No schema migration is required: these fields live in the existing pattern JSON.
- Client checks describe conflicting stitch pairs, matching overlaps, and uncovered cells within the layout rectangle. Conflicts block saving; gaps require visual review because blank margins can be intentional. Server checks remain authoritative. Earlier automatic verification notes are explicitly qualified after a user changes placement.
- Drafts remain local until save, survive failed requests while open, and cannot silently adopt a background revision. Original PDFs, source patterns, and existing projects are preserved. The builder is limited to import review; calibration, page extraction selection, scanned charts, and direct source overlays remain separate work.
- User instructions and compatibility details: [PAGE-BUILDER.md](PAGE-BUILDER.md).

## Private storage and backups

- First hosted adapter: Google Cloud Storage using Application Default Credentials/workload identity. `IPatternAssetStore` and `IBackupArtifactStore` isolate vendor code. Local filesystem stores are permitted only in Development/Testing and must be outside `wwwroot`.
- Production startup checks both buckets for enforced public access prevention and uniform bucket-level access. Objects use opaque generated keys; browser downloads go through authenticated application endpoints. Storage keys are never accepted from the client.
- Original names, media types, sizes, and SHA-256 hashes live in PostgreSQL. Import parsing uses disposable temporary files. A failed parser still retains the source and review warning.
- ZIP format v1 uses an explicit manifest, owner-scoped JSON sections, relevant catalog entries (including history references), and `assets/<id>/original.pdf`. Definitions/components are nested in pattern and project sections. No Identity tables, cookies, credentials, configuration, or provider tokens are serialized.
- Export data is captured in a PostgreSQL repeatable-read transaction. Immutable source objects are copied afterward. Section and asset hashes are checked before return. Archive construction uses disposable files and a 2 GB uncompressed limit; larger datasets currently require an operator-assisted export.
- The same image runs `--backup daily` or `--backup weekly` as a scheduled job. There is no job endpoint, shared scheduler secret, or per-web-instance timer. The hosting scheduler controls timing and retries.
- UTC daily windows and Monday-starting UTC weekly windows have durable unique `(UserId, Kind, ScheduleDate)` receipts. A per-user/kind session advisory lock prevents concurrent generation. The latest-one retention rule applies independently to daily and weekly.
- Replacement uploads an immutable candidate, reads it back, verifies its checksum and contents, then atomically swaps metadata and records the successful window. Old bytes are removed only afterward. Durable deletion records allow retries when old-object cleanup fails. Failed generation/upload preserves the previous retained backup.
- A process crash after upload but before recording the candidate can leave an unreferenced object. Operators should reconcile object keys against database references during maintenance; there is no unsafe blanket age-based bucket deletion. Job receipts and mutation receipts are retained for idempotency in this first release.
- Current exports are not retained or nested in other exports. Deleting a project no longer creates unlimited manual backups: the UI explicitly explains deletion and directs users to export first. The source pattern remains available to the owner and in their export.
- Archive HTTP responses stream without Content-Length to accommodate common ingress response-size limits. The browser currently stages a downloaded export as a Blob; very large datasets need browser memory as well as server staging capacity.

## Hosting and release boundaries

- Multi-stage Dockerfile builds React into ASP.NET static assets, runs as the image's non-root user, and stores all durable production data externally. Configuration/secrets come from the platform.
- `PublicBaseUrl` is the canonical origin used for redirects and scheme/host reconstruction behind HTTPS ingress. Forwarded headers are not trusted. The platform must enforce external HTTPS; the container's plain HTTP port remains behind ingress.
- Data Protection keys are shared in PostgreSQL and encrypted in production with a supplied PFX certificate. All replicas/jobs need the same certificate and application name. Production startup never generates a substitute key-encryption secret.
- `/health/live` reports the running process; `/health/ready` verifies PostgreSQL, applied migrations, and the catalog. Bucket configuration/credentials are checked once at startup, not on every health probe.
- The preproduction guide uses Cloud Run + Cloud SQL + Cloud Storage as one concrete deployment path. The application does not require Cloud Run.
- `Import__MaxMegabytes` defaults to 45 and is constrained to 1–45. Use 25 for the documented Cloud Run HTTP/1 deployment; `/api/capabilities` supplies the actual limit to React.
- The existing .NET 8 application/runtime is retained for this phase. Track its support lifecycle and upgrade the runtime before deploying beyond its supported window.
- No hosting account, domain, OAuth client, cloud resources, or production secrets were available during implementation. Their creation, the live OAuth round trip, private hosted storage, and container replacement rehearsal remain explicit preproduction gates, detailed in `PREPROD-READINESS.md` and `VALIDATION.md`.

## References checked

- [ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-8.0)
- [Data Protection key persistence](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-8.0)
- [Google Cloud Storage authentication](https://docs.cloud.google.com/storage/docs/authentication)
- [Cloud Run request and response limits](https://docs.cloud.google.com/run/quotas)
- [Cloud Run scheduled jobs](https://docs.cloud.google.com/run/docs/execute/jobs-on-schedule)
