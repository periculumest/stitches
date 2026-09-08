> Historical phase 1 document. For the current PostgreSQL release, see [phase 2 decisions](<phase 2/IMPLEMENTATION-DECISIONS.md>) and [preproduction setup](<phase 2/PREPROD-READINESS.md>). SQLite recovery commands below apply only to the old MVP.

# MVP validation and scope

## Automated checks

Run the full workflow with `./Test.ps1` from PowerShell. This builds the production frontend, runs .NET tests, creates an isolated server and data folder, and runs Playwright in installed Google Chrome. Logs/data/screenshots remain in ignored `artifacts/`; browser failure traces remain in `web/test-results/`.

Backend coverage:

- Durable completion across fresh repository connections; project isolation; rejected stale revisions; invalid stitch IDs; undo/redo and redo invalidation.
- Reversible substitutions and definition changes without source mutation; deleting and restoring a completed stitch.
- Milestone idempotence through undo and reopen.
- Consistent backup round trip with source PDFs, progress, inventory, substitutions, and usable history; automatic retention; non-overwriting restore; corruption rejection.
- Synthetic two-page vector/text PDF: 10×20 logical grid, 200 stitches, DMC 310 key mapping, page relocation with undo, explicit finalization.
- Unsupported empty PDF review, unresolved-thread finalization rejection, inventory validation, and a 1,000×1,000 pattern with more than 200,000 actual stitches.
- Supplied symbol-font PDF: exact 200×300 dimensions, 60,000 unique positions, 78 definitions with preserved outlines, 20 positioned pages, summary-count verification, and symbol replacement/undo. Synthetic tabular legends verify overlap removal and refusal of conflicting repeated stitches.

Browser coverage:

- Library/sample creation; selected-color and bulk completion; undo/redo; substitution; bobbins and location; progress after clearing browser storage and reloading.
- Actual multipart PDF upload, review screen, key correction, finalization, and 200 completed stitches.
- Canvas region drag, exact scoped completion, paint/erase, manual removal and undo, simulated save failure and rollback.
- Manual backup and downloadable archive; large-pattern bounded DOM, zoom, isolate, pan, visible bulk completion, undo, and paint.
- Supplied PDF multipart upload, 78 valid SVG symbol shapes with no browser errors, review/finalization, and exactly 5,119 DMC 310 stitches selected and completed.

- Focus mode on desktop and a 390px window: chart expansion, center/zoom and selection/filter preservation, paint/undo persistence, visible save failure and reload, dialog Escape behavior, exit during a pending save, and navigation restoration. Screenshots: `artifacts/focused-pattern.png` and `artifacts/focused-pattern-narrow.png`.

The suite passes 17 backend cases and 8 browser workflows with the supplied PDF present. The two real-PDF checks are explicitly skipped if that private sample is absent; synthetic format regressions still run. Catalog coverage checks every supplied code, description, and RGB value, correction of stale database metadata without inventory loss, legacy White/Blanc migration and undo references, invalid catalog rejection, and inventory search/swatch rendering. The script additionally stops and restarts its actual server process, compares project progress and inventory, and exercises the offline restore CLI.

The 207,738-stitch synthetic pattern took about 4.1 seconds for creation, load, 10 zoom actions, filter, and pan in the initial browser run on this development machine. An expanded run saved 6,969 visible stitches in 1.17 seconds and a paint stroke in 1.28 seconds (including request/response and verification); the expanded sequence took 7.4 seconds. These are end-to-end smoke measurements, not frame-time guarantees on other hardware. The full-project response and snapshot persistence are the main next performance targets.

## Implementation coverage against the original slices

| Slice | Delivered | Explicit limits / next validation |
|---|---|---|
| 01 Foundation | Local ASP.NET/React app, SQLite, immutable pattern vs independent projects, persistence repository, library, health, backup | Project snapshots use JSON inside SQLite; no LAN/accounts |
| 02 Import | Retained upload, basic grid/key extraction, verified tabular symbol-font chart assembly, source glyph outlines, global coordinates, overlap deduplication, usage-count verification | One commercial format verified; no OCR/standalone drawn-symbol recognition; other formats may use provisional placement |
| 03 Audit | Summary, source PDF, navigable located warnings, page overlays/offsets, key and cell corrections, undo/redo, explicit finalization | No full grid calibration or new-definition authoring; specialty interpretation manual |
| 04 Workspace | Canvas spatial index, pan/zoom, readable symbols/colors, grid coordinates, multi-select, highlight/isolate, filters, areas, paint/erase, scoped completion, persistent undo/redo | Fractional orientation simplified; line hit testing/bulk scope use origin cell |
| 05 Progress | Live overall/remaining counts, definition key progress, effective thread/type/page breakdowns, library summaries, persisted milestone celebrations | Per-page progress uses geometric bounds and may double-count overlapping page regions |
| 06 Inventory | Authoritative 454-color RGB JSON, startup metadata refresh, search, user-added codes, whole bobbins/locations, ownership and missing colors | RGB channels take precedence over inconsistent hex fields; no quantity sufficiency estimate |
| 07 Substitution | Global per-project effective thread, source preserved, removal and undo/redo, live material/rendering changes | No blending, automatic matching, or regional substitutions |
| 08 Reliability | Daily/manual backups, retention, offline validated restore, restart/storage-clear tests, import diagnostics, synthetic large-pattern tests | Long sessions and real commercial patterns still need user acceptance testing |

## Before trusting a real long-running project

Use a representative PDF and compare the full chart/key against the original, particularly page edges and any specialty stitches. Mark a small known region, close/reopen the app, and verify its exact count. Download and restore a backup into a separate data folder. Try a short stitching session with her to check comfortable zoom, contrast, symbols, and marking behavior.

The implementation is a runnable MVP with documented import and catalog boundaries. It does not mark every checkbox in the original specs as complete or claim universal PDF compatibility.
