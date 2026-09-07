# Spec 07 — Project Thread Substitution

**Status:** Ready for implementation  
**Priority:** P1

---

## In Scope

- Project-level thread replacement.
- Global replacement across a project.
- Preservation of original pattern mapping.
- Reversal.
- Undo/redo.
- Workspace refresh.
- Inventory/material recalculation.

---

## Out of Scope

- Region-only substitutions.
- Automatic color matching suggestions.
- Blend recipes.
- Pattern-wide source mutation affecting other projects.

---

## Functional Requirements

### FR-01 — Global substitution

The user may replace one pattern thread with another thread for the current project.

### FR-02 — Project isolation

A substitution in one project must not affect another project using the same source pattern.

### FR-03 — Preserve source

Original thread assignment must remain unchanged and recoverable.

### FR-04 — Effective thread

The application must expose the effective project thread separately from the original thread.

### FR-05 — Rendering update

The workspace must immediately update approximate floss colors and relevant labels.

### FR-06 — Material update

Inventory owned/missing calculations must immediately use the effective thread.

### FR-07 — Reversal

The user may remove a substitution and return to the source mapping.

### FR-08 — Undo/redo

Substitution actions must support undo/redo.

---

## Acceptance Criteria

- [ ] User can substitute DMC 310 with another catalog thread.
- [ ] Every relevant occurrence in the current project uses the effective substituted thread.
- [ ] Source pattern data remains unchanged.
- [ ] Another project using the same pattern is unaffected.
- [ ] Removing the substitution restores original behavior.
- [ ] Undo reverses a substitution.
- [ ] Redo reapplies it.
- [ ] Inventory/material counts recalculate immediately.
