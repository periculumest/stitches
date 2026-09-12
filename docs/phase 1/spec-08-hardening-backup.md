# Spec 08 — Reliability, Backup, and Performance Hardening

**Status:** Ready for implementation  
**Priority:** P0 before trusted daily use

---

## In Scope

- Automatic backups.
- Manual backup.
- Restore validation.
- Uploaded PDF preservation.
- Large-pattern performance testing.
- Restart/crash durability.
- Import diagnostics.
- Recovery documentation.

---

## Out of Scope

- Cloud backup.
- Multi-device synchronization.
- Hosted SaaS infrastructure.
- Enterprise observability.

---

## Functional Requirements

### FR-01 — Daily backups

The system must create automatic daily backups.

### FR-02 — Retention

Retain at least seven most recent automatic backups.

### FR-03 — Manual backup

The user may trigger a backup immediately.

### FR-04 — Complete recovery set

Backup must include enough information to restore:

- projects,
- completion state,
- substitutions,
- edits,
- inventory,
- original PDFs or their required managed assets.

### FR-05 — Restore

A documented restore workflow must exist.

### FR-06 — Restore verification

At least one automated/integration/manual release checklist must prove a backup can actually restore usable state.

### FR-07 — Crash durability

Recently persisted completion state must survive normal server restart and browser restart.

### FR-08 — Large-pattern benchmark

Create a representative large synthetic or real-test pattern.

The workspace must remain interactive while:

- zooming,
- panning,
- filtering,
- painting completion.

### FR-09 — Import diagnostics

Failed or questionable imports should retain useful diagnostic information without exposing internal complexity in normal UX.

---

## Acceptance Criteria

- [ ] Daily backup runs successfully.
- [ ] Retention policy removes backups beyond configured retention.
- [ ] Manual backup command works.
- [ ] Backup includes database and required assets.
- [ ] Restore process successfully recreates a working project.
- [ ] Browser storage can be cleared without losing persisted state.
- [ ] Server restart preserves progress.
- [ ] Representative large pattern remains usable.
- [ ] Bulk visible-cell operations do not freeze the application.
- [ ] Import failures can be diagnosed from logs/retained metadata.
