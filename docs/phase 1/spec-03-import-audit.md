# Spec 03 — Import Audit and Manual Correction

**Status:** Ready for implementation  
**Priority:** P0

---

## In Scope

- Audit/review screen before final project use.
- Visual warnings for suspected import issues.
- Manual correction of pattern structure.
- Manual correction of symbol mappings.
- Manual correction of thread mappings.
- Manual correction of stitch types.
- Manual correction of individual stitches.
- Page/grid alignment corrections where supported.
- Confirm/finalize import.
- Undo/redo for user corrections.

---

## Out of Scope

- Automatic perfection.
- Advanced pattern authoring from scratch.
- Image-to-pattern conversion.
- Full stitching workspace.

---

## UX Goal

The import audit should feel like:

> "We think we understood your pattern. Here are the few places worth checking."

Not:

> "Manually rebuild your entire pattern."

---

## Functional Requirements

### FR-01 — Audit summary

After parsing, show a summary of:

- detected pages,
- grid size,
- stitch count,
- detected colors/threads,
- detected stitch types,
- warnings.

### FR-02 — Warning navigation

The user must be able to move directly between suspicious areas.

### FR-03 — Drift visualization

Suspected grid/page drift should be visually highlighted.

### FR-04 — Manual stitch correction

The user must be able to change the stitch definition at a selected location.

### FR-05 — Key correction

The user must be able to edit:

- symbol,
- DMC/thread,
- stitch type.

### FR-06 — Generated symbols

If a symbol cannot be preserved, the user may accept or choose a generated symbol.

### FR-07 — Undo/redo

Audit changes must support undo/redo.

### FR-08 — Confirm import

The user must explicitly confirm that the pattern looks correct enough to use.

---

## Acceptance Criteria

- [ ] Audit screen appears after import rather than immediately creating a trusted project.
- [ ] Summary displays candidate pattern dimensions and stitch totals.
- [ ] Parser warnings are visible and navigable.
- [ ] Suspected drift areas can be highlighted.
- [ ] User can change a stitch's assigned stitch definition.
- [ ] User can change a definition's symbol.
- [ ] User can change a definition's thread.
- [ ] User can change a definition's stitch type.
- [ ] User can undo and redo manual corrections.
- [ ] User can confirm/finalize the import.
- [ ] Finalized normalized pattern is persisted.
- [ ] Original source information remains recoverable after corrections.
