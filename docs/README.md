# Stitch Helper — Specification Pack

This folder contains the product and implementation handoff for Stitch Helper. The current release is described in [phase 2 implementation decisions](<phase 2/IMPLEMENTATION-DECISIONS.md>), [preproduction setup](<phase 2/PREPROD-READINESS.md>), and [phase 2 validation](<phase 2/VALIDATION.md>).

The application is documented in the repository [README](../README.md). The top-level implementation decisions, validation, and recovery files describe the historical phase 1 SQLite MVP; its restore commands do not apply to the phase 2 PostgreSQL release.

Import findings and proposed follow-up work are recorded in [import reliability](<phase 2/IMPORT-RELIABILITY.md>).

For correcting an imported page layout, see the [visual page builder guide](<phase 2/PAGE-BUILDER.md>).

## Recommended reading order

1. `00-product-brief.md`
2. `01-architecture-and-domain.md`
3. `02-implementation-roadmap.md`
4. `spec-01-foundation.md`
5. `spec-02-pattern-import.md`
6. `spec-03-import-audit.md`
7. `spec-04-stitching-workspace.md`
8. `spec-05-progress.md`
9. `spec-06-thread-inventory.md`
10. `spec-07-thread-substitution.md`
11. `spec-08-hardening-backup.md`
12. `09-open-questions-and-future.md`

## Handoff intent

These documents are intended to be implementation-ready enough for Codex to begin work slice-by-slice while preserving the product decisions made during discovery.

The spec pack should be treated as living documentation and updated whenever implementation discoveries materially change product behavior or architecture.
