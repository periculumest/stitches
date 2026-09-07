# Stitch Helper — Open Questions and Future Roadmap

**Version:** 0.1

---

## Resolved Discovery Decisions

The following are considered resolved for MVP planning:

- Desktop/web first.
- Native Android later.
- Single user.
- Local hosting.
- SQLite-backed server persistence.
- Commercial PDF focus.
- Audit before trusting import.
- Manual pattern editing always available.
- All common stitch types must be representable.
- No blended-thread requirement for MVP.
- Seamless multi-page grid.
- Preserve source symbols where possible.
- Generated symbols supported.
- Symbol + approximate floss color displayed together.
- Highlight + isolate + multi-select.
- Viewport/working-area filtering.
- Efficient completion painting.
- Bulk visible completion allowed if reversible.
- Undo/redo mandatory.
- Progress is a primary feature.
- Progress milestones at 10/25/50/75/90/100%.
- DMC catalog shipped as repository data.
- Whole bobbin inventory.
- Simple inventory locations.
- Multiple simultaneous projects.
- Same PDF may create independent projects.
- Automatic backups are required.

---

## Remaining Implementation-Level Questions

These should not block initial scaffolding.

### PDF parsing technology

Evaluate the best combination of:

- direct PDF text extraction,
- vector line extraction,
- page rasterization,
- targeted OCR only when necessary.

Prefer deterministic PDF structure before OCR.

### Rare stitch rendering

Determine exact rendering convention for:

- quarter stitches,
- three-quarter stitches,
- backstitch lines,
- French knots,
- beads.

### Working-area persistence

Decide whether working areas are:

- temporary session state,
- named reusable regions,
- persisted per project.

Recommended initial behavior: persist the most recent working area only if useful.

### Milestone replay behavior

Recommended:

- celebrate each threshold once per project,
- 100% may celebrate again if project falls below 100% and is later recompleted only if explicitly desired.

### Inventory locations

Initial free-form location is sufficient.

Future possibility:

```text
Container
  └── Row
       └── Slot
```

Do not build structured storage hierarchy until the simple approach proves limiting.

---

# Future Milestones

## Native Android

A future iteration should provide a native Android experience.

Potential approaches include:

- Android client against the same local/server API,
- packaged local backend,
- later synchronized mobile persistence.

Android implementation must be revisited after the desktop workflow stabilizes.

## Image-to-Pattern Conversion

Future user journey:

```text
Upload Image
    ↓
Choose Dimensions / Fabric Count / Palette
    ↓
Quantize Colors
    ↓
Map Colors to DMC
    ↓
Generate Symbols
    ↓
Generate Stitch Pattern
    ↓
Create Project
```

This future capability is one reason the normalized pattern model and generated-symbol support are required now.

## Additional Inventory Features

Potential future work:

- multiple locations per thread,
- partial bobbins,
- consumption estimates,
- shopping list,
- low-stock indicators,
- project reservation of thread,
- additional brands.

## Additional Workspace Features

Potential future work:

- bookmarks,
- mini-map,
- named working regions,
- recent-position resume,
- keyboard shortcuts,
- advanced search,
- session analytics,
- configurable completion appearance.

## Advanced Pattern Support

Potential future work:

- scanned PDFs,
- handwritten annotations,
- user-created patterns,
- blend recipes,
- complex specialty stitches,
- arbitrary line/point embellishments.
