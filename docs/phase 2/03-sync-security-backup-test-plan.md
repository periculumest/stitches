# Sync, Security, Backup & Test Plan

**Feature:** SH-IDENTITY-002  
**Status:** Ready for implementation

---

# 1. Google Identity Resolution

Flow:

```text
Google callback
    ↓
ASP.NET Core validates external login
    ↓
resolve Identity external login
    ↓
if existing:
    load ApplicationUser
else:
    create ApplicationUser
    link Google login
    ↓
issue Stitch Helper application cookie
    ↓
redirect to React application
```

A changed Google email must not create a second application user when the provider login key remains the same.

---

# 2. Session Behavior

- 30-day sliding expiration.
- secure framework-standard Identity cookie.
- logout terminates current session.
- no local-storage JWT.
- no persisted Google API tokens.

Test both:

- valid login session;
- expired/invalid/anonymous session.

---

# 3. Multi-Device Synchronization

## Server authority

PostgreSQL is authoritative.

Client caches exist only for UX/performance.

## Active project refresh

Minimum behavior:

```text
project open    -> fetch
window focus    -> fetch/reconcile
every ~30 sec   -> lightweight fetch/reconcile
after mutation  -> reconcile relevant state
```

No SignalR/WebSocket connection.

## Different stitches

Device A:

```text
100 -> complete
```

Device B:

```text
500 -> complete
```

Final state:

```text
100 complete
500 complete
```

## Same stitch

A commits:

```text
100 -> complete
```

B subsequently commits:

```text
100 -> incomplete
```

Final state:

```text
100 -> incomplete
```

## Aggregate conflicts

Versioned edits use optimistic concurrency.

Stale updates return HTTP `409 Conflict` or the application's equivalent typed conflict response.

---

# 4. Client Mutation Buffer

Recommended behavior:

1. user changes stitch state;
2. update optimistic local view;
3. append/set mutation in in-memory buffer;
4. debounce approximately 250-500 ms;
5. send explicit final states in batch;
6. acknowledge/remove successful mutations;
7. show saving status;
8. retry transient failures;
9. show persistent error if the server cannot save.

Mutations should be idempotent state assignments rather than increments/toggles that become ambiguous when retried.

---

# 5. Backup Generation

## User-scoped snapshot

Backup generation must query by authenticated/target owner ID.

A generated user archive must never query an unscoped multi-user dataset.

## Daily job

For each eligible user:

1. create candidate daily backup;
2. serialize user data;
3. copy/package owned assets;
4. create manifest;
5. calculate/verify archive checksum;
6. mark candidate available;
7. replace/supersede previous retained daily backup;
8. remove previous archive only after new archive is known-good.

Retained result:

```text
0 or 1 latest successful daily backup per user
```

## Weekly job

Same process for weekly backup.

Retained result:

```text
0 or 1 latest successful weekly backup per user
```

A failed replacement leaves the previous successful artifact untouched.

## Current export

Generated on demand from current server state.

May stream directly or temporarily stage an archive, but it must use the same versioned portable format.

---

# 6. Backup Download Authorization

Conceptual route:

```text
GET /api/backups/{backupId}/download
```

Server operation:

```text
GetOwned(authenticatedUserId, backupId)
```

If not owned/not found:

```text
404
```

Never open a storage key received directly from the client.

---

# 7. Security Requirements

## Authentication

- use supported ASP.NET Core Identity/Google external login infrastructure;
- do not log external tokens/codes;
- do not store Google refresh/access tokens without need.

## Session

- HttpOnly;
- Secure under HTTPS;
- appropriate SameSite;
- antiforgery protection for mutations.

## Authorization

- authenticated user context is server-derived;
- user IDs in payloads do not grant access;
- all top-level resource queries are owner-scoped.

## Private assets/backups

- outside public web root;
- no static-file exposure;
- opaque server-side storage keys;
- path traversal prevention;
- owner authorization before read/download.

## Backup privacy

The archive must exclude:

- Identity password hashes;
- external provider tokens;
- session state;
- security stamps unless explicitly needed for future account restore (not recommended);
- other accounts;
- operator secrets/configuration.

---

# 8. Automated Test Matrix

## Identity

### AUTH-001 — New Google user

Valid external login creates `ApplicationUser`.

### AUTH-002 — Returning Google user

Existing external login reuses user.

### AUTH-003 — Email snapshot changes

Same provider login key does not create duplicate user.

### AUTH-004 — Anonymous access

Protected APIs reject anonymous callers.

### AUTH-005 — Session config

Configured session uses 30-day sliding expiration.

---

## Authorization

Use two users A and B.

### AUTHZ-001

A cannot GET B project.

### AUTHZ-002

A cannot PATCH B project.

### AUTHZ-003

A cannot mutate B progress.

### AUTHZ-004

A cannot GET B pattern.

### AUTHZ-005

A cannot retrieve B pattern source asset.

### AUTHZ-006

A cannot create project from B pattern.

### AUTHZ-007

Payload owner manipulation cannot transfer/bypass ownership.

### AUTHZ-008

A cannot retrieve B backup.

---

## Sync

### SYNC-001

Two sessions mutate different stitches; both survive.

### SYNC-002

Same-stitch writes settle to last successfully committed state.

### SYNC-003

Stale versioned aggregate update conflicts.

### SYNC-004

Progress survives server restart.

### SYNC-005

Second session fetch sees first session committed changes.

### SYNC-006

Focus refresh triggers reconciliation.

### SYNC-007

Active project periodically refreshes without SignalR.

---

## Blend

### BLEND-001

Single usage has exactly one component.

### BLEND-002

Blend requires at least two components.

### BLEND-003

Three-component blend is accepted.

### BLEND-004

Null strand count is accepted.

### BLEND-005

Zero/negative strand count rejected.

### BLEND-006

All components appear in legend/detail projection.

### BLEND-007

Component substitution changes only selected component.

### BLEND-008

Single-color substitution uses same component mechanism.

### BLEND-009

Inventory projection resolves each component independently.

### BLEND-010

One blended stitch produces one progress record/state.

---

## Backup/export

### BACKUP-001 — Current export

Export contains current owner's domain data.

### BACKUP-002 — Isolation

Export contains no data owned by a second user.

### BACKUP-003 — Assets

Owned source assets referenced by exported patterns are included.

### BACKUP-004 — Manifest

Archive contains valid versioned manifest.

### BACKUP-005 — Secrets

Archive does not contain Identity/auth/session secrets.

### BACKUP-006 — Daily replacement

Successful new daily backup replaces previous daily after validation.

### BACKUP-007 — Daily failure

Failed daily replacement preserves previous successful daily.

### BACKUP-008 — Weekly replacement

Successful new weekly backup replaces previous weekly after validation.

### BACKUP-009 — Weekly failure

Failed weekly replacement preserves previous successful weekly.

### BACKUP-010 — Owner download

Owner can download retained backup.

### BACKUP-011 — Foreign download

Foreign user cannot download retained backup by known ID.

### BACKUP-012 — Catalog portability

Archive includes enough relevant thread catalog information to interpret exported pattern/inventory data.

---

# 9. Manual Verification

- [ ] Login with Google.
- [ ] Confirm internal user identity endpoint works.
- [ ] Create/import a pattern.
- [ ] Confirm source asset has no public URL.
- [ ] Open same project in two browsers.
- [ ] Make distinct progress changes in each.
- [ ] Confirm both survive after refresh.
- [ ] Confirm focus refresh pulls remote change.
- [ ] Confirm active project refresh occurs approximately every 30 seconds.
- [ ] Verify blend with known strand counts.
- [ ] Verify blend with unspecified strand counts.
- [ ] Substitute one component of a blend.
- [ ] Confirm original component can be restored.
- [ ] Generate current export and inspect manifest/data/assets.
- [ ] Confirm export has no authentication data.
- [ ] Download latest daily backup.
- [ ] Download latest weekly backup.
- [ ] Login as a second user and verify isolation.


# 10. Hosted Deployment Test Matrix

## HOST-001 — Stateless restart

Create user data, source assets, and retained backups; replace/restart the web container; verify all remain available.

## HOST-002 — Missing configuration

Remove one mandatory production secret/config value; startup fails with actionable non-secret diagnostics.

## HOST-003 — Migration separation

Deploy application artifact against a database requiring migration; verify migration can be run independently before web rollout.

## HOST-004 — Multiple app instances

Run two web instances against the same PostgreSQL/object storage; verify user data remains consistent.

## HOST-005 — Backup singleton execution

Trigger scheduled backup processing with multiple app instances/workers; verify only one daily/weekly retained backup generation executes per user/schedule window.

## HOST-006 — Private object storage

Attempt direct unauthenticated access to production object-storage paths/keys; verify objects are private.

## HOST-007 — Authorized download

Authenticated application download/export succeeds while object storage remains private.

## HOST-008 — Container replacement

Destroy the running app container and launch a new one from the same image/config; verify no durable data depended on the old container filesystem.
