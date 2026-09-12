# Spec 01 — Foundation and Persistence

**Status:** Ready for implementation  
**Priority:** P0

---

## In Scope

- ASP.NET Core backend.
- React + TypeScript frontend.
- SQLite persistence.
- Filesystem storage for uploaded source files.
- Pattern and Project domain separation.
- Repository abstractions.
- Project library shell.
- Durable server-side state.
- Initial automatic/manual backup support.
- Stable application IDs.
- Health/status endpoint.
- Basic error handling/logging.

---

## Out of Scope

- PDF parsing logic.
- Stitch rendering.
- Inventory UI.
- Thread substitutions.
- Android application.
- Cloud synchronization.
- Multi-user authentication.

---

## Functional Requirements

### FR-01 — Application shell

The application must expose a desktop-oriented web UI and local backend.

### FR-02 — Durable persistence

Authoritative project state must persist in SQLite and survive browser storage deletion.

### FR-03 — Filesystem assets

Uploaded PDFs and generated assets must be stored server-side using application-managed paths.

### FR-04 — Pattern / Project separation

The domain must model a pattern separately from a stitching project.

Importing the same source pattern twice must be able to produce two independent projects.

### FR-05 — Repository abstraction

Application/domain services must not depend directly on raw SQLite queries or filesystem paths.

### FR-06 — Project library

The UI must contain a project library page capable of listing project records even before full pattern import exists.

### FR-07 — Backup

The application must support:

- manual backup,
- automatic daily backup,
- retention of at least 7 automatic backups.

### FR-08 — Restore testability

A documented restore mechanism must exist and be exercised in automated or integration testing where feasible.

---

## Non-Functional Requirements

### NFR-01

The application must function without internet access after dependencies/assets are installed.

### NFR-02

Browser localStorage/sessionStorage must not be required to recover authoritative project progress.

### NFR-03

Filesystem paths must be configurable.

### NFR-04

The architecture must avoid browser-specific domain types so future Android integration remains possible.

---

## Acceptance Criteria

- [ ] Application starts locally with one command or documented frontend/backend commands.
- [ ] SQLite database is created automatically when missing.
- [ ] A project can be created, persisted, loaded, and deleted.
- [ ] Two projects can reference the same pattern without sharing project state.
- [ ] Clearing browser storage does not remove server-persisted project data.
- [ ] Uploaded-source storage directory is configurable.
- [ ] Manual backup can be triggered.
- [ ] Automatic backup generation is implemented.
- [ ] At least seven rolling automatic backups are retained.
- [ ] A backup can be restored successfully in a documented validation test.
- [ ] Repository interfaces exist for core persistence operations.
- [ ] The project library loads project records from the backend.
