# Stitch Helper — Product Brief

**Version:** 0.3  
**Status:** Discovery complete / ready for implementation slicing  
**Primary User:** Single stitcher  
**Initial Platform:** Self-hosted desktop/web application  
**Future Platform:** Native Android application

---

## 1. Product Vision

Stitch Helper is a digital cross-stitch workspace that helps a stitcher import commercial PDF patterns, interact with them digitally, track completed work, manage substitutions, monitor progress, and maintain an inventory of DMC thread.

The core experience is:

> Import a pattern → audit the import → work from an interactive grid → isolate the stitches currently being worked → mark progress efficiently → see completion advance → know which threads are owned or missing.

The product should combine the strongest parts of a pattern-tracking tool with a thread-inventory system.

The initial implementation is intended for one user and will be hosted locally on the user's computer.

---

## 2. Product Principles

### 2.1 The stitching workspace is the product

The application must first become an excellent tool for actively stitching a pattern.

Thread inventory is important, but it must build on top of a reliable normalized pattern model rather than delay the core stitching experience.

### 2.2 Progress is a first-class experience

Progress is not secondary analytics.

Cross-stitch projects may represent hundreds of hours of work. The application should make advancement visible, motivating, and satisfying.

### 2.3 Reduce cognitive load

The UI should help answer:

- Where am I?
- What am I stitching?
- What color/thread is this?
- What stitch type is this?
- What remains?
- How much progress have I made?

The application should avoid making the user think about operating the software while actively stitching.

### 2.4 Imported PDFs are inputs, not the runtime model

The PDF should be parsed into a normalized pattern representation.

The interactive workspace must never depend on the source PDF as its primary data structure.

### 2.5 User progress must be durable

Browser-only storage is insufficient.

Progress, edits, substitutions, and inventory must be persisted server-side and recoverable from backup.

### 2.6 Large patterns are normal

The architecture must assume large, full-coverage patterns from the beginning.

---

## 3. Primary User Journey

```text
Pattern Library
    ↓
Import PDF
    ↓
Pattern Analysis
    ↓
Import Audit
    ↓
Correct Errors
    ↓
Create Project
    ↓
Open Interactive Pattern
    ↓
Select / Filter / Isolate Work
    ↓
Mark Stitches Complete
    ↓
See Progress
    ↓
Persist Automatically
```

Thread inventory integrates with the project after pattern parsing:

```text
Pattern Thread Requirements
    ↓
Compare Against Inventory
    ↓
Owned / Missing / Substituted
```

---

## 4. Confirmed Product Decisions

### Platform

- Initial implementation: desktop-oriented web application.
- Hosted locally on the user's computer.
- Touch is not required for MVP.
- Native Android is a future milestone and must be called out explicitly in design decisions.
- Browser state is not authoritative.

### Persistence

- Server-side persistence is required.
- SQLite is the preferred initial database.
- Imported PDFs should be retained on the server filesystem.
- Automatic backup is an MVP concern, not a later nice-to-have.

### Pattern Source

- MVP targets commercially generated cross-stitch PDFs.
- Scanned PDFs are not a guaranteed MVP format.
- Future capability: convert uploaded images into cross-stitch patterns.
- Manual pattern editing must always be available.

### Pattern Representation

- Imported multi-page patterns are merged into one continuous logical coordinate system.
- PDF page boundaries are source metadata and may be displayed as optional overlays.
- A seamless grid is the canonical workspace representation.
- Every stitch definition must have both a symbol and an approximate digital thread color.
- Source symbols should be preserved where possible.
- Generated symbols must be supported.

### Stitch Types

Assume the user may encounter all common stitch types.

The domain must therefore support at least:

- Full cross
- Half stitch
- Quarter stitch
- Three-quarter stitch
- Backstitch
- French knot
- Bead / embellishment
- Extensibility for additional stitch types

Even if some types receive limited UI treatment in early slices, the data model must not collapse all stitches into a single square-cell concept.

### Thread Relationships

- A stitch definition has a thread/color identity and a stitch type.
- Two stitch definitions may use the same thread but different stitch types.
- Blended-thread recipes are not an MVP requirement.
- Thread substitutions apply globally within a project and must be reversible.

### Filtering and Selection

The user must be able to:

- Highlight matching stitches.
- Isolate matching stitches.
- Select multiple colors/symbols/stitch definitions.
- Filter by completion status.
- Filter by stitch type.
- Work inside a selected rectangular working area.
- Limit highlighting/filter behavior to currently rendered/visible cells when appropriate.

The user should be able to reduce the workspace to exactly the work currently being performed.

### Completion

Completion must support:

- Individual stitch toggling.
- Drag/paint completion.
- Multi-selection.
- Rectangular region actions.
- Mark selected visible stitches complete.
- Mark selected visible stitches incomplete.
- Undo.
- Redo.

Any bulk completion operation must be reversible.

### Progress

Progress should always be prominent.

Required progress concepts include:

- Completed stitches.
- Remaining stitches.
- Completion percentage.
- Progress by thread/stitch definition.
- Progress by stitch type.
- Progress by region/page where useful.

Milestone celebrations should occur at:

- 10%
- 25%
- 50%
- 75%
- 90%
- 100%

Celebrations should be positive but non-disruptive.

### Thread Inventory

- Initial catalog is DMC-focused.
- Ship with a repository-managed DMC catalog in structured JSON.
- Support standard stranded cotton first.
- Domain should support additional DMC families such as Variations.
- Inventory quantity is measured in bobbins.
- Whole bobbin counts are sufficient.
- Storage location can be simple hierarchical/free-form text such as `Box 1 / Row 3`.

### Project Library

- The user works on multiple projects simultaneously.
- Importing the same PDF twice creates two independent projects.
- Each project has independent completion state, substitutions, edits, and progress.

---

## 5. MVP Functional Loop

The MVP is successful when this complete workflow is reliable:

```text
Import PDF
    ↓
Detect grid and key
    ↓
Audit the import
    ↓
Correct problems
    ↓
Create independent project
    ↓
Open large interactive grid
    ↓
Select a symbol/color/stitch
    ↓
Highlight or isolate visible work
    ↓
Define a working area
    ↓
Mark stitches complete efficiently
    ↓
Undo/redo changes
    ↓
Persist progress
    ↓
Display and celebrate completion progress
    ↓
Compare required thread against inventory
```

---

## 6. Explicit Future Capabilities

Not required for the first MVP:

- Native Android application
- Image-to-pattern conversion
- Pattern authoring from scratch
- Cloud synchronization
- Multiple user accounts
- Community sharing
- Shopping integrations
- Additional thread brands
- Advanced historical analytics
- Complex thread-blending recipes
- Collaborative stitching

These future capabilities should not distort MVP scope, but foundational models should avoid preventing them unnecessarily.

---

## 7. Product Success Criteria

The product is successful if the user prefers to work from Stitch Helper rather than a static PDF and can safely rely on it to retain progress.

A strong first version should make it:

1. Easier to see only the stitches currently being worked.
2. Faster to mark progress than clicking each completed stitch individually.
3. Safer to experiment with thread substitutions.
4. Satisfying to see project completion advance.
5. Easy to know whether all required DMC thread is already owned.
