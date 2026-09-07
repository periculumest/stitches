# MVP implementation decisions

Updated: 2026-09-07. These notes supplement the original specification pack and record disagreements, pragmatic scope choices, and limitations. The original product priorities remain intact.

## Preserved decisions

The implementation uses ASP.NET Core, React/TypeScript, SQLite, retained filesystem PDFs, a canonical continuous pattern coordinate system, project-local edits and substitutions, server-authoritative progress, and Canvas rendering. Browser storage is not required. Multiple stitching attempts can share an immutable source pattern with independent state.

## 1. PDF import is an adapter with an explicit compatibility boundary

**Clarification to specs 02–03:** A general importer cannot honestly promise reliable commercial-PDF normalization without examples from the intended user. The user agreed to begin generally and test actual imports later, replacing the importer if necessary.

`IPatternImporter` isolates deterministic PdfPig implementations. The basic adapter finds regular runs of axis-aligned vector grid lines and inline legend mappings. The additional `ChartTableImporter` recognizes the supplied PDF's separate Symbol/Strands/Type/Number/Color tables, joins symbol font identities across PDF font subsets, derives the stitch lattice from symbol positions, and reads printed global coordinates. It retains page bounds, notes, immutable source data, and the original PDF. A failed extraction remains an audit project and cannot be finalized with zero stitches or unmapped threads.

Text extraction and vector inspection use [PdfPig’s documented page, letter, and path APIs](https://github.com/UglyToad/PdfPig). PDF rendering for comparison uses the browser’s native PDF viewer in a separate tab; there is no OCR or server rasterization dependency.

Numbered symbol-font charts now assemble automatically from column labels and rotated row labels. Offset inference requires multiple agreeing labels; clipped edge labels do not override that consensus. Matching repeated cells are collapsed to one stable stitch ID; conflicting repeats stop the import. Extracted dimensions and per-thread full-stitch counts are compared with printed metadata when available. Simple charts without the recognized structure retain provisional vertical stacking and manual page placement. See [PDF-IMPORT-ASSESSMENT.md](PDF-IMPORT-ASSESSMENT.md) for the verified sample and remaining limits.

Source font shapes are stored as portable SVG path geometry on stitch definitions and rendered in the key, selected-stitch details, materials list, and Canvas via cached Path2D objects. Font subsets are matched by base font name plus decoded symbol value. The current outline adapter handles 8-bit font character maps; other font mappings may fall back to a text symbol with an explicit warning. Quadratic and cubic curves are serialized separately with invariant numeric formatting. A shared per-font frame preserves relative symbol sizing (including small dots). Thread/type edits preserve source outlines; replacing the text symbol removes the outline, and undo restores it. No fonts need installation in the browser.

The audit editor changes any existing definition, adds/removes/reassigns stitches, and moves source pages; each mutation has undo/redo. It does not implement a general grid-boundary reconstruction wizard or new-definition authoring. Unsupported source types remain prominent warnings. This is a practical import MVP, not proof of publisher coverage.

## 2. Catalog: supplied RGB data is authoritative

**Updated following the user's catalog addition:** `data/thread-catalog/rgb-dmc.json` replaces the original 39-color starter palette and is the source of truth for all 454 supplied thread codes. The obsolete `dmc.json` asset has been removed. `floss` maps to the thread code, `description` to its name, and the integer `r`, `g`, `b` channels to its display color. The `row` field describes the supplied chart order; it is not a user's inventory storage location.

The supplied `hex` column has 15 entries that disagree with RGB or contain truncated/spreadsheet-converted values. Display hex is therefore calculated from the RGB channels, without modifying the source file or guessing corrections. Startup validates the entire catalog (unique codes, names, integer channels in 0–255) before updating SQLite in one transaction. Rebuilding copies the asset into the runtime/publish output; restarting refreshes the catalog descriptions and colors, including existing rows. Inventory counts and locations are independent and preserved. User-added codes outside the supplied set remain available; supplied codes always take precedence.

The supplied code `White` is canonical. Legacy `Blanc` inventory is migrated to `White`; if both existed, bobbin counts are added and distinct location notes retained. Existing project, source-pattern views, substitutions, and undo/redo references resolve `Blanc` to `White`. Immutable stored source snapshots and PDFs are not rewritten. The PDF key parser accepts both spellings. This replaces the earlier starter-catalog compromise; no claim about official endorsement or exact physical color matching is made.

Whole bobbins and free-form locations persist server-side. Zero bobbins plus an empty location removes an inventory entry. Material checks indicate ownership of a required effective color, **not a guarantee of sufficient thread quantity**. Additional DMC codes and the Variations family are supported; blend recipes remain out of scope.

## 3. Snapshot persistence with operation deltas

**Simplification of the recommended relational model:** SQLite has separate `patterns`, `projects`, `catalog`, `inventory`, and `metadata` tables. Project data is a versioned JSON snapshot; commands update it atomically using an expected revision. Repository boundaries keep SQL and filesystem paths out of domain logic.

The source pattern stays immutable. Each project stores its editable normalized data, completion IDs, substitutions, latest area, milestones, and undo/redo history. Normal completion history records affected IDs and inverse values rather than copying the entire pattern per stroke. Key, stitch, and substitution changes store inverse operations; page placement stores an inverse pattern snapshot because it changes multiple coordinates.

The most recent 100 undo actions persist across restart. A new edit clears redo. Every completion mutation, definition edit, stitch edit, substitution, and page move is reversible. Working-area and name changes are persistent settings and are not added to undo history. Milestone celebrations are once per project, including after undoing below and recrossing a threshold.

This keeps migration and Android API semantics straightforward. Large JSON loads and writes are a known ceiling: rendering is spatially indexed, but the API currently sends the complete project on mutations and the library reads full stored snapshots for summaries. Chunked data transfer and relational completion storage are future improvements if real project sizes demand them.

## 4. Save semantics favor visible confirmation

Completion updates optimistically, with a visible “Saving…” state. The UI accepts one mutation at a time while the local server commits it. Failed requests roll the screen back to the last confirmed state, display the failure, and offer reloading the saved project. The client does not automatically replay an uncertain request, which could otherwise duplicate an operation after an ambiguous network failure.

The server checks revisions to prevent silent lost updates between windows. SQLite uses WAL and `synchronous=FULL`. A completed response means a database commit; closing while “Saving…” remains visible is not a guarantee. Browser unload prompts while a mutation is pending.

## 5. Geometry and rendering

The domain represents FullCross, HalfCross, QuarterCross, ThreeQuarterCross, Backstitch, FrenchKnot, Bead, and Other as separate definition types. Stitches support floating-point origins and optional line endpoints. Each instance has its own stable completion ID. The editor can set backstitch endpoints; other specialty types use a consistent simplified display convention. **Fractional orientation and specialty geometry recognition are not fully implemented.** Manual cell editing currently uses integer origins; imported future geometry may use fractional coordinates.

Canvas bins stitches in 32×32 regions and only draws bins intersecting the viewport, with additional stitch bounds clipping. Line bins include their full extent. Symbols render at cell sizes of at least 12 pixels; overview zoom uses colored cells and the symbol-bearing key. Canvas hit testing currently selects line/point stitches by their origin cell, rather than arbitrary positions along a line. Bulk scope uses the instance origin.

Painting respects selected definitions, completion/type filters, and the current working area. Each continuous drag is one undo operation and interpolates crossed cells. A scoped bulk button displays its affected count before use; normal bulk actions need no modal confirmation. Manual edits are explicit tools.

## 6. Recovery is a complete, verified set

Backups use [Microsoft.Data.Sqlite’s online backup API](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup), followed by retained source files and SHA-256 checksums in a manifest. A shared mutation lock keeps the database and assets consistent. The archive is published through a temporary filename only after completion.

The daily worker runs at startup and checks every 30 minutes. It creates at most one automatic backup per UTC date and retains the seven newest; missed days while the app is closed are not fabricated. Manual backups are retained until the user removes them from disk. Project deletion makes a manual backup first.

Restore is an offline CLI action into a new/empty data directory. It rejects unsafe archive paths, unsupported schema, checksum failures, SQLite corruption, and missing referenced PDFs. It never overwrites a nonempty existing directory. The automated round trip verifies usable project state and undo history after restoration. See [RECOVERY.md](RECOVERY.md).

## 7. Hosting and future Android

The default URL is `http://127.0.0.1:5057`, because port 5050 was already used by another app on the development computer. The UI and APIs run on the same origin in production. Local host/origin checks help prevent arbitrary browser pages from writing to the local service. Authentication and LAN exposure are not part of this release.

The implementation targets .NET 8 using the installed SDK/runtime. A future supported-runtime upgrade should be treated as routine maintenance. Android remains a future client: the plain JSON API, stable IDs, normalized geometry, and server-owned commands do not depend on React or browser storage.

## Next import iteration

Try several of her actual PDFs and record whether grid dimensions, symbol count, mappings, page placement, overlap strips, and specialty stitches match. Build publisher-specific fixtures only with permitted sample sources, and keep personal/commercial PDFs out of version control. Improve the adapter based on those results rather than silently increasing its claimed confidence.

## Focused pattern view

Focus mode uses the full browser viewport and hides navigation, the pattern key, and the details panel. It keeps stitching tools, undo/redo, zoom/fit, progress, save state, and any save failure visible. A compact row identifies active thread/type/status filters, isolation, and working-area restrictions. Exit focus, Escape, or Change thread or filters returns to the workspace without clearing state. Edit stitches returns to the workspace so its editing panel is accessible. Import review remains available in focus, with completion still disabled until confirmation in the normal workspace.

This is an optional session view, not a saved project setting or a native Fullscreen API request. Browser controls remain available; browser fullscreen can be used separately. The same canvas stays mounted. Resizing preserves its center stitch and zoom; initial load and explicit Fit pattern calculate a new fit. This avoids losing the current stitching position when changing layouts.
