# Arrange chart pages during import review

After importing a PDF, choose **Arrange pages** beside the review confirmation button. The builder starts with the current saved arrangement. It is available while the project is awaiting review and has extracted chart pages.

1. Compare the colored page previews with the original design. Each tile is labeled with its PDF page number. **Open page in original PDF** opens the source with a page hint; PDF viewers may handle that hint differently.
2. Keep the existing layout or choose a **Top-left page**, select **Pages per row**, and choose **Arrange in rows**. This proposes PDF-number order with the chosen page first. It does not infer missing pages or change which pages were extracted.
3. Drag tiles to arrange them. Edges snap together; hold Alt to disable snapping. Select a tile and use arrow keys for one-stitch adjustments, or Shift + arrow for ten stitches. The selected-page menu, offset inputs, and arrow buttons also work without dragging.
4. For a specific join, select the page you want to move, choose its **Neighbor page**, enter the repeated overlap width in stitches, and choose **Place left/right/above/below**. Placing left or above the origin shifts the draft to make room. **Fit board**, zoom, scrolling, and **Find this page on the board** help with large designs.
5. Review the join checks. Matching repeated stitches are merged on save. Conflicting stitch pairs block saving. Uncovered areas are highlighted in the written checks; they can be intentional margins, so compare them with the source. Rectangle coverage and matching symbols do not prove that the intended design is correct.
6. Choose **Save arrangement** to save every page position together and return to import review. Check the assembled chart and key before choosing **Looks good, start stitching**.

**Undo move** reverses draft adjustments, and **Reset layout** returns to the arrangement from when the builder opened. Cancel/Escape asks before discarding changes. Closing or refreshing the browser warns if the draft is unsaved, but drafts are not stored across browser sessions. A failed save keeps the draft open for retry. If another window changes the project, close the draft and reopen the builder using the latest project version. Saved arrangements can be undone/redone from the workspace; undoing an arrangement is blocked while completion is attached to its stitches.

## Scope and compatibility

- Previews are generated from extracted stitches, not PDF page images. They show thread colors (the first component for blends); use the original PDF to inspect symbols and source details.
- Moving pages changes their positions, not the detected grid or stitch interpretation. Grid calibration, scanned-pattern recognition, page inclusion/exclusion, rotation, and an aligned source overlay remain future work.
- New imports retain source stitches hidden by overlap merging, so separating pages later restores those stitches. Earlier imports with overlapping page footprints lack this information and must be re-imported before using the builder. Earlier non-overlapping imports with source-page stitch IDs can be arranged directly.
- Individual edits without a source page, or edits moved outside their source page, cannot be safely moved by this builder. Arrange pages before those edits, undo them, or work from a fresh import.
- Re-importing creates a separate review project. Existing stitching progress is not migrated or overwritten.

Restart the application with `Start.ps1` after updating the code so the frontend and backend both support the builder. No database migration or new service configuration is needed for this change.
