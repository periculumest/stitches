# Stitch Helper — Identity, Persistence, Backup & Sync Decisions

**Status:** Locked for implementation  
**Phase:** Google-First Identity, User-Owned Persistence, Backups, Multi-Device Progress, and Blended Floss  
**Date:** 2026-09-07

## Purpose

This document is the canonical decision record for the next Stitch Helper implementation phase. Codex should treat the decisions below as implementation constraints unless a later design document explicitly supersedes them.

---

# Locked Decisions

## 1. Authentication is Google-first

- Google is the first supported authentication provider.
- Stitch Helper owns the application user identity.
- Google identity is linked to a provider-neutral application user.
- The application's stable user identifier is an application-generated `Guid`.
- Google-specific identifiers must not appear on Pattern, Project, Inventory, Import, Preference, or other domain-owned entities.
- Email address must not be used as the primary/immutable user identifier.
- The Google provider subject identifier (`sub`) is the stable external identity key.

### Initial login surface

Supported:

- Google login

Not supported in this phase:

- local username/password login
- password reset
- email magic-link login
- Microsoft login
- Apple login
- passkeys
- guest/anonymous accounts

Future providers must be addable without changing user ownership relationships throughout the domain.

---

## 2. ASP.NET Core Identity is the authentication infrastructure

Use ASP.NET Core Identity with a `Guid`-based `ApplicationUser`.

Conceptually:

```csharp
ApplicationUser : IdentityUser<Guid>
```

External Google login mappings are owned by ASP.NET Core Identity infrastructure.

The domain recognizes only the internal application user ID.

---

## 3. Web authentication uses server-managed cookies

The React application shall not store or manage Google access tokens, refresh tokens, or ID tokens.

Authentication flow:

```text
React
  -> ASP.NET Core Google challenge
  -> Google
  -> ASP.NET Core callback
  -> resolve/create ApplicationUser
  -> issue Stitch Helper application cookie
  -> React authenticated session
```

### Session policy

- 30-day sliding session duration.
- `HttpOnly` session cookie.
- `Secure` in deployed HTTPS environments.
- appropriate `SameSite` configuration.
- logout terminates the current application session.
- no Google access/refresh tokens are persisted unless a later Google API integration explicitly requires them.

---

## 4. Same-origin web architecture

Prefer one public application origin:

```text
https://<stitch-helper-host>/
  /            React application
  /api/*       ASP.NET Core API
  /auth/*      authentication endpoints
```

Development may run React and ASP.NET separately, but the React development server should proxy `/api` and `/auth` to the backend.

Avoid introducing cross-origin API/auth complexity without a product requirement.

---

## 5. PostgreSQL becomes the durable datastore now

- PostgreSQL is the authoritative persistent datastore.
- Use Entity Framework Core with the Npgsql provider.
- Use versioned EF Core migrations.
- Production startup must not silently drop/recreate the production schema.
- Browser/local storage is not authoritative application persistence.
- Local development may run PostgreSQL using Docker Compose while React and ASP.NET Core run normally.

---

## 6. No legacy-user migration is required

There is no meaningful existing user dataset that must be preserved.

Therefore:

- do not implement SQLite-to-user claim logic;
- do not implement first-login ownership migration;
- do not implement migration allowlists;
- do not carry legacy-data migration complexity into this phase.

If useful during development, seed/test data may be recreated in PostgreSQL.

Any old local/SQLite persistence may be removed once PostgreSQL behavior is verified.

---

## 7. All persistent product data is user-owned

The following belong to exactly one application user:

- imported patterns
- normalized pattern definitions
- pattern source assets/files
- projects
- project progress
- project-specific substitutions
- thread/floss inventory
- inventory storage locations
- user preferences/settings
- import history
- import corrections
- user-generated project metadata
- user backup/export artifacts

Every read/write involving user-owned data must be authorization-scoped to the authenticated application user.

The client must never be trusted to select an owner.

---

## 8. No project or pattern sharing

Not in scope:

- shared projects
- collaboration
- invitations
- public project links
- public pattern links
- cross-account pattern copies
- user-to-user pattern sharing
- pattern marketplace/community library

Imported commercial pattern data remains private to the user who imported it.

---

## 9. Pattern source assets are private

- Imported source files must not live under a public/static web root.
- Source assets are stored behind an asset-store abstraction.
- Domain records refer to opaque asset/storage identifiers.
- Asset access requires authenticated ownership authorization.
- Local protected filesystem storage is acceptable for local development only.
- Hosted/production deployments must use durable private object storage or another explicitly durable mounted storage service.
- Production correctness must never depend on an ephemeral application-container filesystem.
- The asset abstraction must allow provider changes without changing domain behavior.

---

## 10. Multi-device desktop/laptop usage is supported

A user may log into the same account on multiple desktop/laptop browsers.

The server is authoritative.

### Freshness behavior

- fetch current project state when the project is opened;
- refresh when the browser/tab regains focus;
- perform a lightweight refresh approximately every 30 seconds while a project is active;
- refresh relevant state after local progress mutations;
- SignalR/WebSocket live synchronization is explicitly deferred.

### Conflict behavior

- different-stitch progress changes merge naturally;
- latest successfully committed mutation wins when the same stitch is changed;
- larger aggregate edits use explicit version/concurrency metadata;
- stale aggregate writes return a conflict rather than silently overwriting newer data.

True offline-first synchronization is deferred.

---

## 11. Progress persistence uses narrow mutations

Do not save the complete project/progress document after each stitch.

Prefer sparse state such as:

```text
ProjectStitchState
  ProjectId
  StitchId
  State
  UpdatedAt
```

with a composite unique/primary key:

```text
(ProjectId, StitchId)
```

The default/unworked stitch state should not require a database row where practical.

The React client may batch rapid progress mutations with a short debounce window, but the server remains authoritative.

---

## 12. Blended floss is fully implemented in this phase

The domain supports:

- single-thread usage
- blended-thread usage containing two or more physical thread components

Each component contains:

- thread reference
- optional strand count

Strand count rules:

- `null` = source pattern did not specify
- positive integer = known count
- zero/negative = invalid

Do not invent strand counts during import.

---

## 13. Blend visualization

Display blends using the real component colors plus the pattern symbol.

Use split/striped/multi-component visual treatment.

Do not calculate or present a synthetic RGB "mixed floss color" as if it were a real catalog color.

The legend/details view must show every component and its strand count when known.

---

## 14. Substitutions operate at thread-component level

Substitution is modeled against a thread usage component.

This applies equally to:

- ordinary single-color usage (one component)
- blended usage (two or more components)

A user may substitute one component of a blend without replacing the others.

Substitutions remain reversible.

---

## 15. Thread catalog is shared reference data

Physical thread catalog records such as DMC colors are not user-owned.

Conceptually:

```text
ThreadCatalogItem
  Id
  Brand
  Code
  Name
  DigitalColor
```

User inventory references catalog items.

Pattern thread usages reference catalog items.

A blend is a composition of catalog items; it does not create a synthetic catalog color.

---

## 16. User-downloadable backups are a first-class product requirement

This is not merely a future commercialization task.

Each authenticated user must be able to download backups containing their complete Stitch Helper dataset.

### Automatic retained backups

For each user, retain:

- one latest daily backup
- one latest weekly backup

When a new daily backup is successfully created, it replaces the previous retained daily backup.

When a new weekly backup is successfully created, it replaces the previous retained weekly backup.

### On-demand current export

The user must also be able to generate/download a current dataset export at any time.

The application should expose a **Backups & Export** surface containing at least:

- Download current data
- Download latest daily backup
- Download latest weekly backup

when those automatic backups exist.

### User isolation

A downloadable backup contains only the authenticated user's data and private assets.

Never expose a PostgreSQL database dump containing other users.

---

## 17. Portable backup/export format

User backups/exports should be application-level portable archives rather than raw PostgreSQL dumps.

Recommended archive:

```text
stitch-helper-backup-<timestamp>.zip
  manifest.json
  data/
    patterns.json
    projects.json
    progress.json
    inventory.json
    preferences.json
    imports.json
    substitutions.json
    ...
  assets/
    <opaque-asset-id>/
      <original-file>
```

`manifest.json` should contain:

- backup format version
- exported timestamp
- application/schema version
- user-scoped dataset metadata
- checksums for included files/assets where practical

Must not include:

- Google OAuth tokens
- session cookies/secrets
- password hashes/credentials
- other users' data

The format should be designed so a future restore/import capability can consume it even if self-service restore is not implemented in this slice.

---

## 18. Infrastructure backups remain separate

User-downloadable backups do not replace infrastructure disaster-recovery backups.

Commercial/production infrastructure will still eventually require:

- PostgreSQL backups
- private asset-storage backups
- restore rehearsals

Those infrastructure backups are operator concerns and are never directly downloadable by an individual user when they contain multi-user data.

---


## 19. Hosting-ready production architecture is required now

Stitch Helper is expected to be hosted in the near term. The application must therefore be designed so the first hosted deployment is a deployment exercise, not an architecture rewrite.

### Production topology

Target this logical shape:

```text
Internet
   |
HTTPS / platform ingress
   |
Stitch Helper Web/API container(s)
   |----------------------|
   |                      |
Managed PostgreSQL     Private object storage
                          |
                          +-- pattern source assets
                          +-- retained daily backups
                          +-- retained weekly backups
```

### Production hosting constraints

- The ASP.NET Core + React application must be deployable as a container image.
- The running application container is stateless except for transient working files.
- PostgreSQL is external to the application container.
- Private assets/backups are external to the application container.
- Configuration is supplied through environment variables / platform secret configuration.
- No production secret belongs in source control or baked into a container image.
- HTTPS is required for deployed authentication.
- Health/readiness endpoints must be available for platform monitoring.
- Database migrations must be run as an explicit deployment/release step, not as uncontrolled startup-time schema mutation.
- The app must tolerate restart/replacement of the web container without losing user data.
- Multiple web instances must not create duplicate scheduled backups or other singleton work.

### Domain/hosting neutrality

Do not couple the domain to a single cloud vendor.

Use abstractions for:

```text
database connection/configuration
private object storage
backup artifact storage
background/scheduled job execution
```

Provider-specific code belongs in infrastructure/configuration.

### Hosted storage

Production should use a private object-store implementation behind the existing asset-store abstraction.

Examples of compatible storage styles include S3-compatible object storage, Azure Blob Storage, or equivalent durable private object storage.

The implementation should support:

```text
IPatternAssetStore
IBackupArtifactStore
```

with local filesystem implementations for development and object-storage implementations for hosted environments.

### Background jobs

Daily/weekly backups must not be implemented as an uncoordinated `System.Threading.Timer` or equivalent per-process timer.

Use either:

- a hosting platform scheduler that invokes a protected job endpoint/worker;
- a dedicated background worker with distributed locking;
- or a durable job scheduler with persistence/locking.

The architecture must remain safe if the web app is scaled to more than one instance.

### Production startup

Application startup should:

- validate required configuration;
- connect to PostgreSQL;
- validate dependent services where appropriate;
- expose readiness only when the app can serve requests safely.

Application startup should not:

- drop/recreate databases;
- silently fall back to SQLite;
- silently fall back to public/local ephemeral asset storage;
- generate production secrets.

### Deployability definition of done

The phase is not complete until the application can be deployed to a clean hosted environment using only:

- a built application/container artifact;
- environment/secret configuration;
- PostgreSQL connection information;
- object-storage configuration;
- Google OAuth production client configuration;
- explicit database migration execution.

No manual copying of application data into the running container may be required.

# Previously Locked Product Constraints Still in Force

- Initial platform is desktop/web.
- Native Android is a future phase.
- Commercially generated patterns are expected input.
- Pattern definition and project/progress instance are separate.
- One pattern can back multiple independent projects for the same user.
- Multi-page patterns become one normalized working grid.
- Progress operations retain undo behavior where already specified.
- Rendering/filtering should be efficient against the visible working area where practical.
- Floss representation includes digital thread color plus pattern symbol.
- Inventory supports bobbin quantity and physical storage location.
- Import corrections are supported and reversible where specified.

---

# Explicitly Deferred

- SignalR/WebSocket synchronization
- offline-first synchronization
- native Android client
- project sharing
- pattern sharing
- billing/subscriptions
- entitlements
- guest accounts
- self-service restore from downloaded backup
- alternate auth providers
- account merge
- public pattern marketplace/library
