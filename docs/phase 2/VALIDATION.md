# Phase 2 validation

Date: 2026-09-07. Application implementation and local verification are complete; real Google/hosted-infrastructure release gates remain open until the owner provisions the environment.

## Executed successfully

| Check | Evidence |
| --- | --- |
| TypeScript and React production build | `npm --prefix web run build` passed |
| Backend tests with real PostgreSQL | 50 passed, 0 failed, 0 skipped |
| Chrome browser suite | 21 passed, 0 failed, including progress-save UI, Iron Man import, and page builder regression checks |
| Visual page builder | Drag/snap, keyboard movement, row layouts, top-left choice, overlap placement, local undo/reset, cancellation, and narrow-screen use passed |
| Arrangement persistence and recovery | One revision/chart-revision update, persisted reload, workspace undo/redo, confirmation, failed-save retry, and stale-draft rejection passed |
| Source overlap preservation | Joining and separating pages retained every source stitch across serialization; matching overlaps merged, conflicting overlaps were rejected atomically, and edits/undo preserved repeated instances |
| Builder compatibility and input checks | Incomplete/duplicate/unknown/invalid page placements, active/stale projects, unsafe legacy overlaps, and undo with attached progress were rejected |
| Cross Stitch Professional import | Supplied Iron Man PDF imported all 202,500 stitches, 63 original symbols, two-strand DMC assignments, and 48 correctly placed chart pages; browser confirmation, completion, save, and reload passed |
| Import ambiguity handling | Synthetic fixtures verified matching-overlap deduplication and blank fabric; rejected conflicting overlaps, missing pages/coordinates/key entries, incorrect dimensions, unknown symbols, and duplicate cell symbols |
| Progress saves without page-wide flashing | Delayed-save test observed no disabled-attribute transitions on initially enabled navigation/tool/key-edit buttons; controls retained full opacity and accepted further strokes |
| Pending-save sequencing and recovery | Navigation waited for all strokes, undo used the confirmed revision, and failed saves retained the workspace and changes until retry succeeded |
| Canvas completion appearance | Sampler cell pixels faded on painting and remained faded after saving/reopening; erasing restored contrast, and the inspected-cell completion button also faded the cell. Passed in the full suite against an isolated database; the reported project-specific symptom has not yet been reproduced. |
| Complete local script | `Test.ps1 -SkipInstall` passed, including the job/restart checks below |
| Daily and weekly job commands | Both `--backup daily` and `--backup weekly` succeeded |
| App process replacement | Existing cookie, project/progress state, and inventory still worked after stop/start |
| Retained archive downloads after restart | Daily and weekly ZIPs downloaded successfully for the expanded browser fixture dataset |
| EF schema/model consistency | `migrations has-pending-model-changes` reported no changes |
| Container build | `docker build --build-arg APP_VERSION=0.2.0 -t stitch-helper:phase2-validation .` passed |
| Container release command | Initial migration against a clean, separate PostgreSQL container passed; repeat migration passed |
| Container runtime | Non-root UID 1654; static app returned 200; liveness/readiness succeeded; anonymous private API returned 401 |
| Missing production configuration | Image startup rejected a missing HTTPS `PublicBaseUrl` with an actionable diagnostic |
| Visual inspection | Sign-in page, three-component blend workspace, assembled Iron Man import, desktop page builder, 48-page Iron Man board, and narrow-screen builder screenshots inspected |

The complete successful Windows script run retained its logs, private test objects, signed-cookie fixture, and downloaded daily/weekly archives under:

```text
artifacts/test-run-6134810a751f482e9ae7df8e83b5f49f/
```

The latest script used isolated databases on the local PostgreSQL instance at loopback port 5433, not SQLite or an in-memory EF provider. The initial validation used PostgreSQL **17.11** at port 55432. Test code creates randomly named test databases and drops its backend-test databases afterward; browser-test databases remain for inspection. Container and EF tooling results are retained from the earlier phase 2 validation; they were not rerun for the page builder change.

The container smoke test used a separate Linux PostgreSQL 17.11 container and ran the app image in Development mode for local filesystem adapters. This proves packaging, migration, non-root serving, and health behavior; it does **not** prove a real hosted object-storage or Google OAuth configuration.

## Coverage exercised

- Stable Google subject mapping, changed email snapshots, and simultaneous first callbacks resolving one internal account, using the real Identity database/resolver.
- Cookie lifetime/sliding/HttpOnly configuration; anonymous API rejection; CSRF rejection for missing tokens and success for valid cookie/token pairs.
- Two-user isolation for projects, commands, progress, patterns, source downloads, duplicate/project creation, deletion, retained backups, inventory, and export. Attempted payload ownership does not grant access. Database foreign keys reject cross-owner references.
- Concurrent disjoint progress writes; same-stitch last commit; idempotent retry receipts; conflicting request-ID reuse; atomic rejection of invalid batches; revision conflicts; durable sparse rows; progress undo without chart replacement.
- Inventory revision checks, including cleared entries retaining concurrency protection.
- Single-, two-, and three-component thread usages; nullable counts; zero/negative/empty/duplicate/unknown component validation; explicit blend PDF extraction; per-component substitutions and undo.
- PDF imports, audit confirmation, chart edits, page placement, symbol-font outlines, overlap deduplication, expected usage counts, and retained private source files.
- Versioned ZIP manifests, section/asset checksums, catalog portability, secret exclusion, foreign-user exclusion, asset inclusion, corrupted archive detection, and storage-key traversal rejection.
- Daily/weekly successful replacement, upload failure preserving the old artifact, latest-one retention, and competing workers producing one successful artifact per window.
- Browser progress painting, erasing, bulk operations, undo/redo, work areas, source audit, substitutions, inventory, exports, persisted reload, focus/narrow-screen behavior, visible failed saves, and successful retry.
- Two authenticated browser contexts seeing merged progress after focus and a controlled 30-second timer; browser blend editor/stripes/materials/substitution; sign-in gate and logout.

## Scale and import observations

- The supplied symbol-font sample passed both backend and browser checks: 200 × 300, 60,000 stitches, 78 colors, 20 chart pages, and 5,208 deduplicated overlap stitches.
- The supplied `Iron Man (1).pdf` passed both backend and browser checks: 450 × 450, 202,500 stitches, 63 colors, and 48 chart pages. All source symbol outlines and the explicit two-strand setting were retained. Selected per-thread counts and page boundaries were checked against the source.
- The synthetic large workspace contained **207,738 stitches**. The latest complete script observed a 6,969-stitch viewport mutation saved in about **1.5 seconds**, and a paint stroke in about **1.6 seconds**. The complete create/load/zoom/filter/pan sequence was about **9.9 seconds** on this machine.
- These observations validate the existing canvas behavior in this environment; they are not production capacity or latency guarantees.

Screenshots are in ignored `artifacts/`, including `phase2-signin.png`, `phase2-blend.png`, `workspace.png`, `focused-pattern.png`, `supplied-pdf-symbols.png`, and `iron-man-import.png`.

Page builder screenshots: `page-builder.png`, `page-builder-iron-man.png`, and `page-builder-mobile.png`. The builder corrects placement of extracted pages; page selection/extraction, grid calibration, and image-only chart recognition remain outside this release.

## Open hosted gates

The owner has not yet selected/provisioned a provider/hostname/OAuth client. Use [PREPROD-READINESS.md](PREPROD-READINESS.md) to complete these steps.

- [ ] Real Google OAuth redirect/callback, returning identity, and logout on the final HTTPS origin.
- [ ] Actual Google Cloud Storage credentials, bucket privacy enforcement, upload/read/delete permissions, and authorized downloads.
- [ ] Production Data Protection certificate and shared encrypted key ring across deployed instances.
- [ ] Platform migration job, Cloud SQL connectivity, runtime database role privileges, and secret mappings.
- [ ] Platform daily/weekly schedules, retry behavior, and failure alerts.
- [ ] Full hosted container replacement with real source objects and retained archives surviving outside the container.
- [ ] Concurrent deployed app instances, including cookie continuity and scheduled-job contention.
- [ ] Representative largest import/export under the selected ingress, memory, timeout, and concurrency settings.
- [ ] Infrastructure database/object-storage restore rehearsal and operator recovery policy.
- [ ] CI execution in the repository host. The workflow is supplied; no remote CI run was triggered here.

The new-visitor “session expired” false warning found during screenshot review was corrected after the full-suite run; a targeted browser check against the rebuilt container verifies that first visits show a clean sign-in screen and private APIs still reject anonymous access.
