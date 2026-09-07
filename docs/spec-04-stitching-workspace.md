# Spec 04 — Interactive Stitching Workspace

**Status:** Ready for implementation  
**Priority:** P0

---

## In Scope

- Large-pattern rendering.
- Seamless grid.
- Optional source-page overlays.
- Zoom.
- Grid numbering.
- Symbol + approximate floss color rendering.
- Stitch selection.
- Key selection.
- Highlight.
- Isolate.
- Multi-select.
- Filter by color/thread.
- Filter by symbol/stitch definition.
- Filter by stitch type.
- Filter by completion state.
- Working-area selection.
- Visible-cell/viewport scoped filtering.
- Completion marking.
- Drag/paint completion.
- Rectangle completion.
- Bulk visible selected-stitch completion.
- Undo/redo.

---

## Out of Scope

- Inventory management.
- Progress celebrations.
- Android/touch optimization.
- Cloud sync.
- Pattern authoring.

---

## Default Layout

Initial desktop layout:

```text
┌──────────────────────────────────────────────────────┐
│ Project / Progress / Main Toolbar                    │
├──────────────┬───────────────────────┬───────────────┤
│ Pattern Key  │                       │ Selected      │
│ Symbols      │     Pattern Canvas    │ Stitch/Tools  │
│ Threads      │                       │               │
├──────────────┴───────────────────────┴───────────────┤
│ Status / Coordinates / Scope                         │
└──────────────────────────────────────────────────────┘
```

This is a starting point, not a permanent constraint.

---

## Rendering Requirements

Large patterns must not use one permanently mounted DOM node per stitch.

Use Canvas or equivalent viewport-aware rendering.

Only visible/near-visible cells should need full render work.

---

## Functional Requirements

### FR-01 — Seamless grid

All imported pages must appear as one continuous pattern.

### FR-02 — Page overlay

Source page boundaries may be toggled on/off.

### FR-03 — Zoom

The user must be able to zoom smoothly while preserving readable alignment.

### FR-04 — Grid numbering

Coordinates/grid numbering must be visible at appropriate zoom levels.

### FR-05 — Dual visual identity

A stitch must be visually represented using:

- its symbol,
- approximate thread/floss color.

Color alone is insufficient.

### FR-06 — Stitch selection

Clicking a stitch must reveal:

- thread/color,
- DMC code where applicable,
- thread name,
- symbol,
- stitch type,
- completion state.

### FR-07 — Key selection

Selecting a key entry must behave consistently with selecting a stitch using that definition.

### FR-08 — Highlight

Matching stitches remain prominent while unrelated rendered stitches are de-emphasized.

### FR-09 — Isolate

The user may show only matching/selected work.

### FR-10 — Multi-select

The user may select multiple stitch definitions/colors/types.

### FR-11 — Viewport-aware filtering

The UI must support applying highlighting/filter behavior primarily to rendered/visible cells.

This is both a performance strategy and a useful working mode.

### FR-12 — Working area

The user may drag/select a rectangular working area.

Filtering and completion operations can be scoped to it.

### FR-13 — Single completion

The user may toggle one stitch complete/incomplete.

### FR-14 — Paint completion

In completion mode, the user may drag across stitches to mark them complete.

### FR-15 — Paint incomplete

An alternate action/modifier must allow repainting stitches incomplete.

### FR-16 — Rectangular completion

A rectangular selection can be marked complete or incomplete.

### FR-17 — Bulk visible completion

The user may mark all currently visible stitches matching the active selection/filter complete.

This action must show a clear scope and be undoable.

### FR-18 — Undo/redo

All completion/edit actions in this workspace must support undo/redo.

---

## Safety / UX Requirements for Bulk Actions

Before applying bulk completion:

- scope must be clear,
- selected definitions must be clear,
- operation must be reversible.

Avoid modal confirmation for every normal action.

Undo is the primary safety mechanism.

---

## Acceptance Criteria

- [ ] Large pattern opens without mounting one DOM node per stitch.
- [ ] Multi-page pattern appears seamlessly.
- [ ] User can toggle page boundaries.
- [ ] User can zoom in/out.
- [ ] Grid numbering remains aligned.
- [ ] Stitch renders both symbol and approximate floss color.
- [ ] Clicking a stitch identifies thread, symbol, stitch type, and completion.
- [ ] Clicking a key entry highlights matching visible stitches.
- [ ] User can isolate selected stitches.
- [ ] User can select multiple definitions/colors.
- [ ] User can filter by stitch type.
- [ ] User can filter by completed/incomplete.
- [ ] User can create a rectangular working area.
- [ ] User can restrict work to viewport or working area.
- [ ] User can paint stitches complete by dragging.
- [ ] User can paint stitches incomplete.
- [ ] User can mark rectangular selection complete/incomplete.
- [ ] User can bulk-complete currently visible matching stitches.
- [ ] Bulk completion can be undone.
- [ ] Undo/redo works across completion operations.
