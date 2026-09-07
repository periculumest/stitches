# Stitch Helper — Architecture and Domain Model

**Version:** 0.1  
**Status:** Implementation guidance

---

## 1. Recommended Initial Stack

The initial implementation should use:

- **Backend:** ASP.NET Core
- **Frontend:** React + TypeScript
- **Persistence:** SQLite
- **Pattern/asset storage:** Local filesystem
- **Rendering:** Canvas-based or equivalent viewport-aware rendering

This stack is recommended because:

- the application is single-user and locally hosted,
- SQLite provides durable server-side persistence without database infrastructure,
- ASP.NET Core fits the expected implementation environment,
- React provides a flexible desktop web UI,
- Canvas avoids the cost of mounting tens or hundreds of thousands of stitch DOM nodes.

---

## 2. Architectural Boundaries

Recommended high-level layers:

```text
Frontend
  ↓
Application/API Layer
  ↓
Domain Layer
  ↓
Repositories / Persistence
  ↓
SQLite + Filesystem
```

Browser state may cache data for responsiveness, but must not be authoritative.

The domain should remain independent of React and browser APIs where practical so a future Android application can reuse API contracts and domain semantics.

---

## 3. Persistence Strategy

### SQLite

SQLite should persist:

- Patterns
- Projects
- Pattern definitions
- Stitch definitions
- Project completion state
- Pattern edits
- Project substitutions
- Inventory
- Inventory locations
- Application settings
- Import audit metadata where useful

### Filesystem

Filesystem storage should persist:

- Original uploaded PDFs
- Generated previews/thumbnails
- Import debug artifacts if retained
- Future image sources

Store stable references/paths in SQLite rather than large PDFs as database blobs unless implementation discovery proves otherwise.

---

## 4. Pattern vs Project

These are separate concepts.

### Pattern

A reusable imported source design.

Contains:

- source metadata,
- normalized stitch grid,
- original symbols,
- original thread mappings,
- page/source boundaries,
- import audit information.

### Project

One stitching attempt using a pattern.

Contains:

- completion state,
- user edits,
- substitutions,
- project-specific metadata,
- activity/progress state.

Importing the same PDF twice must allow creation of two independent projects.

A project should not share mutable completion state with another project.

---

## 5. Canonical Coordinate System

Multi-page PDFs must be normalized into one continuous logical grid.

Example:

```text
(0,0) ---------------------- (width-1,0)
  |
  |
  |
(0,height-1)
```

Source pages are mapped into this coordinate system.

Page boundaries are retained as metadata and may be shown as overlays.

The workspace should not force the user to navigate page-by-page.

---

## 6. Core Domain Concepts

Suggested conceptual model:

```text
Pattern
 ├── PatternGrid
 ├── StitchDefinitions
 ├── SourcePages
 ├── SourceKey
 └── ImportAudit

Project
 ├── PatternId
 ├── CompletionState
 ├── PatternEdits
 ├── ThreadSubstitutions
 ├── WorkingState
 └── Progress

ThreadCatalog
 └── ThreadDefinition

ThreadInventory
 └── InventoryEntry
```

---

## 7. Stitch Modeling

Do not model a stitch as only:

```text
x + y + color
```

A stitch position should reference a stitch definition.

Conceptual example:

```json
{
  "x": 140,
  "y": 82,
  "stitchDefinitionId": "sd-13"
}
```

A stitch definition:

```json
{
  "id": "sd-13",
  "symbol": "●",
  "thread": {
    "brand": "DMC",
    "code": "310"
  },
  "stitchType": "FullCross"
}
```

Another stitch definition may use the same thread:

```json
{
  "id": "sd-14",
  "symbol": "/",
  "thread": {
    "brand": "DMC",
    "code": "310"
  },
  "stitchType": "HalfCross"
}
```

These must remain distinct.

---

## 8. Stitch Types

The domain should use an extensible stitch-type model.

Initial known values should include:

```text
FullCross
HalfCross
QuarterCross
ThreeQuarterCross
Backstitch
FrenchKnot
Bead
Other
```

Some stitch types do not map naturally to one square grid cell.

Therefore, the model should leave room for geometry.

Conceptually:

```text
Point stitch:
  x, y

Cell stitch:
  x, y

Line stitch:
  startX, startY
  endX, endY
```

MVP rendering may initially prioritize the most common types, but the normalized model should not make later line/point stitches impossible.

---

## 9. Thread Model

Initial thread identity:

```text
Brand
Family
Code
Name
DisplayColor
```

Example:

```json
{
  "brand": "DMC",
  "family": "StrandedCotton",
  "code": "310",
  "name": "Black",
  "displayColor": "#000000"
}
```

Do not assume display RGB is exact physical thread color. It is a visual approximation.

Blended-thread recipes are not required in MVP.

---

## 10. Symbols

Every stitch definition must have a symbol.

Symbol sources:

1. Preserve source PDF symbol where recognized.
2. Allow manual correction.
3. Generate a replacement symbol if missing/unreliable.
4. Support generated symbols for future image-to-pattern conversion.

The rendering system should always show enough information that color-blindness or similar colors do not make stitches indistinguishable.

---

## 11. Effective Thread Resolution

A project may substitute a thread.

Conceptually:

```text
Pattern Thread
   ↓
Project Substitution?
   ↓
Effective Thread
```

Example:

```text
Pattern: DMC 310
Project substitution: DMC 3799
Effective thread: DMC 3799
```

The original pattern mapping must remain unchanged.

All downstream calculations should use effective thread where appropriate.

---

## 12. Pattern Editing Model

Manual editing is always allowed.

Edits should be recorded as project/pattern edit operations rather than destructively rewriting the original imported representation when practical.

Examples:

- Change stitch definition.
- Change symbol.
- Change thread mapping.
- Change stitch type.
- Add stitch.
- Remove stitch.
- Correct page/grid mapping.

A reversible operation model is preferred.

---

## 13. Undo / Redo

Undo/redo is mandatory.

It should cover at least:

- completion painting,
- region completion,
- bulk visible completion,
- thread substitution,
- pattern edits.

A command-based operation model is recommended.

Example:

```text
ProjectOperation
 ├── type
 ├── payload
 ├── inversePayload
 ├── timestamp
 └── projectId
```

This does not require a fully event-sourced system.

Use the simplest implementation that provides reliable reversible operations.

---

## 14. Rendering Architecture

Large patterns are expected.

Avoid one React component per stitch.

Recommended approach:

- Canvas-based grid rendering.
- Viewport-aware drawing.
- Spatial chunking/tiling.
- Only render cells intersecting the viewport.
- Maintain efficient indexes for stitch-definition lookup.
- Maintain indexes for completion state.
- Render page boundaries as optional overlays.
- Render symbols and approximate floss colors together.

Filtering/highlighting may operate primarily over currently rendered cells for interactive speed.

---

## 15. Working Area

The workspace should support a rectangular working area.

A working area contains:

```text
minX
minY
maxX
maxY
```

Filtering and bulk actions may be scoped to:

- full pattern,
- current viewport,
- selected working area.

UI must make the current scope obvious before destructive/bulk actions are applied.

---

## 16. Progress Calculation

At minimum:

```text
totalCount
completedCount
remainingCount
completionPercentage
```

Progress may also be indexed/grouped by:

- stitch definition,
- thread,
- stitch type,
- region/page.

Use derived values where inexpensive rather than storing redundant counters that may drift.

---

## 17. Thread Inventory

Conceptual structure:

```json
{
  "threadId": "dmc-stranded-310",
  "bobbinCount": 3,
  "location": "Box 1 / Row 3"
}
```

The user does not require fractional bobbin quantities in MVP.

The model should support additional entries/locations later if needed, but the initial UI may keep one simple location string per inventory entry.

---

## 18. DMC Catalog

Ship a versioned repository asset, for example:

```text
/data/thread-catalog/rgb-dmc.json
```

The catalog should be validated on startup/build.

The exact source and redistribution rights for official DMC data must be verified before shipping.

Do not scrape and silently redistribute uncertain catalog data.

---

## 19. Backup Architecture

Backup is an MVP reliability requirement.

Recommended design:

- Automatic daily SQLite backup.
- Retain at least the most recent 7 automatic backups.
- Manual "Back up now".
- Manual export of database/project data.
- Include uploaded source PDFs and required asset references in backup strategy.
- Restore process must be documented and testable.

A backup is not considered valid until restore has been exercised successfully.

---

## 20. Future Android Considerations

Android is not part of MVP implementation.

However:

- API/domain DTOs should not assume browser-only types.
- Authoritative state must remain server/domain owned.
- IDs should be stable.
- File references should use application-level abstractions.
- Project model should be portable.
- UI-specific state should not leak into persisted domain state unnecessarily.

The future Android application may initially communicate with the same local server or later evolve toward its own synchronized/local persistence model.
