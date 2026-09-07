# Supplied chart format assessment

Assessed 2026-09-07 using the locally supplied `docs/sample-patterns/Spider-ManBWChart.pdf`. The PDF was inspected and rendered locally; it was not uploaded to an external service. Diagnostic images and extracted inspection data remain under ignored `artifacts/`.

## Why the original importer failed

The 27-page document contains a cover, instructions, layout preview, two legend pages, two usage-summary pages, and 20 chart pages. The original importer expected uninterrupted regular grid lines and a simple symbol/code row. This document instead uses:

- Separate Symbol / Strands / Type / Number / Color columns. The strand count `2` is not a thread number, and symbol baselines differ slightly from the text row.
- Embedded `CrossStitch2` font subsets. Extracted characters such as `é` encode chart symbols; displaying those characters in a system font produces the wrong shapes.
- Grid fragments, filled rectangular borders, and shaded overlap strips that interfere with a single uniform-vector-line detector.
- Global numbered axes, including rotated row labels and partially clipped edge numbers. Chart pages form four columns by five rows, rather than a vertical strip.

## Verified result

| Check | Result |
|---|---|
| Design dimensions | 200 columns × 300 rows |
| Unique stitches | 60,000 |
| DMC definitions | 78, all mapped to the supplied catalog |
| Preserved source symbol outlines | 78 |
| Chart pages | PDF pages 8–27, assembled into 20 tiles |
| Column offsets | 0, 50, 100, 150 |
| Row offsets | 0, 69, 138, 207, 276 |
| Repeated overlap instances removed | 5,208 |
| Count validation | All 78 full-stitch totals agree with the PDF usage summary |
| Example totals | DMC 310: 5,119; White: 1,826; DMC 947: 2,756 |

Normal tiles contain 53×72 printed cells; the right edge has 50 columns and the bottom row has 24 rows. Three-column/three-row overlap strips account for the repeated instances. Source page bounds retain these printed extents for overlays, but each logical stitch is stored only once.

## Implementation

`ChartTableImporter` detects table structure rather than a filename or fixed project size. It tolerates small glyph-placement drift, reads coordinate labels by position, and requires agreement among labels. Embedded font glyphs are converted to portable path data; the original PDFs remain preserved. Existing simple PDF support stays available as a fallback.

Ambiguous coordinates, inconsistent dimensions, conflicting overlaps, and disagreement with the usage summary stop this adapter instead of creating an apparently complete but incorrect chart. Review is still required before stitching. The adapter imports full-cross definitions; scans, sparse or unnumbered chart layouts, standalone drawn symbols, mixed specialty geometry, and other font encodings still need further adapters.

Regression coverage includes this actual PDF when present, a small independently generated two-page tabular chart, a deliberately conflicting overlap, source-symbol edit/undo, browser outline rendering, and selected-color completion. Existing imports are snapshots and are not silently rewritten when parser code changes; import the source again to obtain a newly reviewed result.
