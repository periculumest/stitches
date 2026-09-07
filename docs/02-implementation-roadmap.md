# Stitch Helper — Implementation Roadmap

**Version:** 0.1  
**Status:** Ready for Codex handoff

---

## Goal

Build the application as a sequence of independently testable vertical slices.

Do not attempt full PDF intelligence, inventory, advanced rendering, and every stitch type in one implementation pass.

The first priority is establishing a durable, normalized project model and a usable stitching loop.

---

# Slice Order

## Slice 01 — Foundation and Persistence

Establish:

- ASP.NET Core backend
- React/TypeScript frontend
- SQLite database
- filesystem asset storage
- repository abstractions
- pattern/project separation
- backup infrastructure
- application shell

Outcome:

The application can create/open projects and persist durable state.

---

## Slice 02 — Pattern Import

Establish:

- PDF upload
- source retention
- page extraction
- initial grid/key parsing pipeline
- normalized pattern model
- source page coordinate mapping
- import confidence metadata

Outcome:

A commercial PDF can be transformed into a normalized candidate pattern.

---

## Slice 03 — Import Audit and Manual Correction

Establish:

- import review UI
- suspected drift/error highlighting
- symbol/thread/key correction
- grid correction
- manual stitch correction
- confirm/finalize import
- undo/redo for audit edits where practical

Outcome:

The user can trust and repair an imperfect import instead of requiring perfect automatic parsing.

---

## Slice 04 — Interactive Stitching Workspace

Establish:

- large canvas rendering
- seamless multi-page grid
- symbol + color rendering
- zoom
- grid numbering
- selection
- highlight
- isolate
- multi-select
- working area
- completion marking
- completion paint/drag
- visible-region bulk completion
- undo/redo

Outcome:

The user can actively stitch from the application.

---

## Slice 05 — Progress Experience

Establish:

- overall progress
- remaining stitches
- progress by thread/definition/type
- prominent progress display
- milestone celebrations
- project library progress summaries

Outcome:

Progress becomes an emotionally visible, reliable part of the product.

---

## Slice 06 — Thread Catalog and Inventory

Establish:

- versioned DMC JSON catalog
- catalog browsing/search
- bobbin inventory
- location text
- owned/missing comparison
- pattern material requirements
- project inventory summary

Outcome:

The user can determine which required DMC thread is already owned.

---

## Slice 07 — Thread Substitution

Establish:

- project-level global substitution
- original/effective thread distinction
- reversible substitutions
- live update of rendering
- live update of inventory/material calculations
- undo/redo

Outcome:

The user can safely replace colors without losing the source pattern.

---

## Slice 08 — Hardening and Recovery

Establish:

- automatic backups
- restore workflow
- manual export/import
- crash/restart durability testing
- large-pattern performance benchmark
- import diagnostics
- project recovery verification

Outcome:

The application is safe enough to trust with long-running projects.

---

# MVP Boundary

The initial MVP should include Slices 01 through 08.

Some advanced support for rare stitch types may be visually simplified, but the data model must represent them correctly.

---

# Explicitly Deferred

The following are post-MVP:

- Native Android application
- Image-to-pattern conversion
- Cloud synchronization
- Multi-user support
- Pattern marketplace/sharing
- Shopping integration
- Additional thread brands
- Advanced analytics/history
- Pattern creation from scratch
- Collaborative projects
