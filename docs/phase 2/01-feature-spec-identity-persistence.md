# Feature Specification — Identity, Persistence, Backups & Multi-Device Progress

**Feature ID:** SH-IDENTITY-002  
**Status:** Ready for implementation  
**Primary objective:** Establish commercial-ready identity and user-owned persistence foundations without introducing unnecessary legacy-migration or collaboration complexity.

---

# In Scope

- Google-first authentication
- ASP.NET Core Identity
- 30-day sliding application session
- PostgreSQL persistence via EF Core/Npgsql
- strict per-user authorization
- private source-asset storage
- user-downloadable backups and current export
- one retained daily backup per user
- one retained weekly backup per user
- multi-device desktop/laptop progress consistency
- 30-second active-project refresh plus focus refresh
- atomic/narrow progress mutations
- aggregate concurrency control
- blended-thread support
- component-level substitutions
- shared thread catalog
- automated security/isolation tests

# Out of Scope

- legacy SQLite/user-data migration
- native Android
- offline-first sync
- SignalR/WebSockets
- project sharing
- pattern sharing
- billing/subscriptions
- guest accounts
- self-service backup restore
- Apple/Microsoft/passkey login
- account merging

---

# User Journeys

## Journey A — New user signs in

1. User chooses **Continue with Google**.
2. ASP.NET Core starts the Google external-login challenge.
3. Google authenticates the user.
4. ASP.NET Core validates the external authentication response.
5. Identity resolves or creates an internal `ApplicationUser`.
6. Stitch Helper creates a 30-day sliding authenticated application session.
7. React loads current-user state.
8. All newly created patterns/projects/inventory/settings are owned by the internal application user ID.

## Journey B — Same user works on two computers

1. User signs in to the same Google account on computer A and computer B.
2. Both sessions resolve to the same internal user.
3. A and B open the same project.
4. A marks stitch 100 complete.
5. B marks stitch 500 complete.
6. Both mutations are committed independently.
7. On refresh/focus/poll, both devices see both completed stitches.
8. No whole-project stale save erases the other device's progress.

## Journey C — User downloads current data

1. Authenticated user opens **Backups & Export**.
2. User chooses **Download current data**.
3. Server constructs a user-scoped portable backup archive.
4. Archive contains the user's application data and owned pattern source assets.
5. Archive contains no authentication secrets and no other users' data.
6. Browser downloads the generated archive.

## Journey D — User downloads retained backup

1. Automatic backup process has created a daily and/or weekly backup for the user.
2. User opens **Backups & Export**.
3. Available backup timestamps are shown.
4. User downloads the latest daily or weekly backup.
5. Authorization confirms the backup belongs to the current user.

## Journey E — User works a blended symbol

1. Pattern legend defines a symbol as a blend.
2. Blend contains two or more real thread catalog items.
3. Strand count may be known or unspecified per component.
4. Grid/legend renders the symbol with split/striped component colors.
5. Required-floss/inventory views resolve each physical component.
6. User may substitute one component without replacing the entire blend.
7. Marking the stitch complete records one stitch-progress operation.

---

# Functional Requirements

## FR-001 — Application user identity

Use a `Guid`-based ASP.NET Core Identity `ApplicationUser`.

The internal user ID is the canonical application owner identifier.

Google/email identifiers must not be used as domain ownership keys.

## FR-002 — Google external login

Support Google as the only login provider in this phase.

Requirements:

- use ASP.NET Core external authentication/Identity integration;
- identify returning Google users through the provider's stable subject/login key;
- request only identity/profile scopes required for login;
- do not request Gmail, Drive, Calendar, Contacts, or unrelated scopes;
- do not persist Google access/refresh tokens without a future explicit requirement.

## FR-003 — Application session

After Google login:

- issue a server-managed application auth cookie;
- 30-day sliding expiration;
- `HttpOnly`;
- `Secure` in HTTPS deployments;
- appropriate `SameSite`;
- prevent session fixation through framework-standard sign-in behavior;
- provide logout.

## FR-004 — Current-user endpoint

Expose an authenticated endpoint such as:

```text
GET /api/me
```

Return only application-safe profile information needed by the UI.

Do not expose provider tokens or authentication internals.

## FR-005 — CSRF protection

Cookie-authenticated state-changing API calls must use the framework's antiforgery/CSRF protection strategy.

React should centralize antiforgery token/header behavior in the shared API client.

## FR-006 — PostgreSQL persistence

Use:

```text
Entity Framework Core
Npgsql
PostgreSQL
```

All durable user/domain state in this feature persists in PostgreSQL.

Use versioned database migrations.

## FR-007 — User-scoped authorization

Every user-owned query/mutation derives the user ID from authenticated server context.

Never authorize based on a client-supplied owner ID.

Preferred repository/API pattern:

```text
GetOwned(authenticatedUserId, resourceId)
```

For an inaccessible/foreign private resource, prefer a not-found response so the API does not disclose another user's resource existence.

## FR-008 — Owned data

Top-level user-owned aggregates include at minimum:

- Pattern
- Project
- PatternSourceAsset
- Inventory
- UserPreferences
- ImportRecord
- BackupArtifact

Progress and substitutions inherit/validate ownership through their project and user-scoped queries.

## FR-009 — Private pattern assets

Pattern sources must:

- live outside public/static web directories;
- use opaque storage keys;
- be retrieved only after authorization;
- be referenced through an asset abstraction;
- use local protected filesystem storage only in development;
- use durable private object storage in hosted/production environments;
- never rely on the web-container filesystem for durable production assets.

## FR-010 — Pattern/project same-owner invariant

A project may reference only a pattern owned by the same application user.

Cross-user project-to-pattern relationships are invalid.

## FR-011 — Sparse/narrow progress persistence

Progress changes must be represented as narrow per-stitch state mutations.

Do not require replacement of the full project/progress object.

Recommended persisted shape:

```text
ProjectStitchState
  ProjectId
  StitchId
  State
  UpdatedAt
```

Recommended unique key:

```text
(ProjectId, StitchId)
```

## FR-012 — Client progress batching

React may collect rapid stitch-state changes in an in-memory buffer and submit them in a short batch.

Recommended debounce target:

```text
250-500 ms
```

Mutation API should set explicit state and be safe to retry.

The mutation buffer is not an offline datastore.

## FR-013 — Saving-state UX

Project UI must expose persistence state when mutations are pending or failing.

At minimum support:

- Saving…
- Saved
- Save failed / retrying

Do not represent a failed persistence mutation as safely saved.

## FR-014 — Multi-device refresh

While a project is active:

- fetch current server state on project open;
- refresh on window/tab focus;
- perform a lightweight refresh approximately every 30 seconds;
- refresh/reconcile relevant state after local progress mutations.

SignalR/WebSockets are deferred.

## FR-015 — Progress conflict behavior

Different stitches:

- preserve both independently committed changes.

Same stitch:

- latest successfully committed server mutation is authoritative.

Larger aggregates:

- use explicit application-level version/concurrency value;
- stale write returns a conflict;
- client refreshes before retry.

## FR-016 — Shared thread catalog

Introduce/use shared reference records for physical floss/thread colors.

At minimum:

```text
ThreadCatalogItem
  Id
  Brand
  Code
  Name
  DigitalColor
```

Catalog data is shared reference data, not user-owned inventory.

## FR-017 — Thread usage

Pattern legend thread usage supports:

### Single

Exactly one thread component.

### Blend

Two or more thread components.

Each component includes:

- thread catalog item reference;
- nullable strand count;
- display/order metadata where needed.

## FR-018 — Strand count

Valid strand count states:

```text
null      unspecified by pattern
> 0       specified
<= 0      invalid
```

Importers must not infer/invent a strand count when source data does not provide one.

## FR-019 — Blend visualization

The UI shall:

- show pattern symbol;
- show each component color;
- use split/striped/multi-component presentation;
- show catalog identifier/name;
- show strand count when known;
- indicate unspecified strand count when details are expanded.

Do not synthesize a fake catalog/RGB blend color.

## FR-020 — Component-level substitution

Substitutions target a thread-usage component.

A blend component may be replaced independently.

Single-color substitution uses the exact same component mechanism.

Substitutions remain reversible.

## FR-021 — Inventory compatibility

Inventory records real physical thread items.

For a blend:

- resolve each component independently;
- show inventory availability/location per component;
- do not create a synthetic "blend bobbin";
- preserve existing box/row/bobbin behavior.

## FR-022 — User backup artifact

A user backup is an application-level, user-scoped archive.

Model enough metadata to represent:

- backup ID
- owner user ID
- kind (`Daily`, `Weekly`, optionally `Manual`/`Export`)
- created timestamp
- format version
- storage key
- byte size
- checksum/status

Automatic retained daily/weekly artifacts must be downloadable only by their owner.

## FR-023 — Daily backup retention

For each user:

- maintain at most one retained automatic daily backup;
- successfully creating a replacement daily backup supersedes/removes the previous daily artifact;
- never delete the previous good backup until the new one has been successfully created and validated.

## FR-024 — Weekly backup retention

For each user:

- maintain at most one retained automatic weekly backup;
- successfully creating a replacement weekly backup supersedes/removes the previous weekly artifact;
- never delete the previous good backup until the new one has been successfully created and validated.

## FR-025 — Current dataset export

An authenticated user can request/download a current export at any time.

The generated archive must contain the user's current durable dataset and owned source assets.

It must not contain another user's data.

## FR-026 — Portable archive structure

Recommended archive:

```text
manifest.json
data/*.json
assets/**
```

The manifest must include:

- backup format version;
- creation/export timestamp;
- application/schema version;
- data section inventory/counts;
- asset metadata/checksums where practical.

Do not include:

- auth/session secrets;
- Google tokens;
- password/authentication internals;
- other users' data.

## FR-027 — Backup UI

Provide a **Backups & Export** UI with:

- Download current data
- latest daily backup timestamp + download action if available
- latest weekly backup timestamp + download action if available

The UI should distinguish:

- live/current export
- retained automatic daily backup
- retained automatic weekly backup

## FR-028 — Backup generation safety

Backup generation should:

1. read a consistent user-scoped snapshot;
2. include required source assets;
3. generate archive;
4. verify archive metadata/checksum;
5. persist backup artifact as available;
6. only then remove/supersede the previous backup of the same retained kind.

A failed backup must not destroy the previous successful retained backup.

## FR-029 — No raw multi-user database export

Never make a PostgreSQL database dump directly available as a user's downloadable backup.

User downloads must be application-level and ownership-filtered.

## FR-030 — Logout

Logout terminates the current browser/device session without deleting data or invalidating other active sessions unless a future "sign out everywhere" feature is added.

---


## FR-031 — Containerized hosted deployment

The web/API application shall be buildable and runnable as a production container image.

The production image must not contain:

- environment-specific secrets;
- user data;
- private source assets;
- backup archives;
- a production database.

## FR-032 — External durable dependencies

Hosted production must treat the following as external durable services:

- PostgreSQL;
- private pattern asset storage;
- backup artifact storage.

Restarting/replacing the application container must not lose acknowledged user data, uploaded pattern sources, or retained backup artifacts.

## FR-033 — Production configuration

Production configuration shall come from environment variables and/or the hosting platform's secret/configuration system.

At minimum configuration must cover:

- PostgreSQL connection string;
- Google OAuth client ID/secret;
- public base URL;
- cookie/auth security settings where environment-dependent;
- object-storage endpoint/account/bucket/container settings;
- object-storage credentials or workload identity;
- scheduled-backup invocation/worker settings.

Startup must fail clearly when mandatory production configuration is missing.

## FR-034 — Database migration deployment step

Database schema migrations must be applicable as an explicit release/deployment action.

Normal production web startup must not independently race to apply migrations across multiple instances.

The deployment documentation must define:

1. deploy/build artifact;
2. run database migration task;
3. start/roll web service;
4. verify health/readiness.

## FR-035 — Health/readiness

Expose health/readiness endpoints suitable for hosting-platform monitoring.

Readiness should reflect whether the application can safely serve authenticated requests, including critical database availability.

Do not expose secrets or verbose internal diagnostics through public health endpoints.

## FR-036 — Hosted scheduled backup execution

Automatic daily/weekly backup work must be safe in a horizontally scaled hosted environment.

Do not use an uncoordinated per-web-process timer.

Use a scheduler/worker mechanism with singleton execution or distributed locking.

## FR-037 — Hosted object storage abstraction

Provide production implementations/configuration for private object storage behind:

```text
IPatternAssetStore
IBackupArtifactStore
```

Object keys must remain opaque to clients.

Production bucket/container access must be private by default.

Downloads must be authorized by the application; any temporary signed URL approach used later must be short-lived and generated only after authorization.

## FR-038 — Deployment portability

Provider-specific hosting/storage code must remain infrastructure-scoped.

Changing hosting provider must not require changing:

- Pattern ownership rules;
- Project behavior;
- backup archive format;
- thread/blend domain model;
- authenticated user identity model.

# Non-Functional Requirements

## NFR-001 — User isolation

User A must never retrieve/mutate User B's:

- pattern
- source asset
- project
- progress
- substitution
- inventory
- preference
- import
- backup/export artifact

through guessed IDs or payload manipulation.

## NFR-002 — Durable acknowledged progress

A successfully acknowledged progress mutation survives:

- refresh
- logout/login
- backend restart
- another authenticated device session

## NFR-003 — Backup portability

The backup format must not depend on restoring a raw PostgreSQL database.

The format must be versioned so future application versions can introduce import/restore migrations.

## NFR-004 — Backup privacy

A user backup contains only that user's product data plus shared reference identifiers required to interpret the data.

Where shared thread-catalog records are required, either:

- include the relevant catalog records in export, or
- include enough catalog version/reference metadata to restore them deterministically.

## NFR-005 — Observability

Log authentication, backup-generation, and persistence failures with:

- internal correlation IDs
- internal user/resource IDs where appropriate
- sanitized error codes

Never log OAuth/session secrets or raw pattern contents.

## NFR-006 — Performance

Normal progress marking must not rewrite the normalized pattern or entire project progress dataset.

---

# Acceptance Criteria

## Authentication/session

- [ ] New Google login creates an internal application user.
- [ ] Returning Google login resolves the same internal user.
- [ ] Domain ownership uses internal GUID, not email/Google identifiers.
- [ ] Session uses a 30-day sliding lifetime.
- [ ] Logout invalidates the current session.
- [ ] Unauthenticated protected API requests fail.

## Authorization

- [ ] User A cannot list/read/update User B's private resources.
- [ ] User A cannot retrieve User B's backup artifact.
- [ ] User A cannot create a project referencing User B's pattern.
- [ ] Manipulated `OwnerUserId` payload data cannot bypass authorization.
- [ ] Integration tests exercise real persistence/repository paths.

## PostgreSQL

- [ ] Durable state persists in PostgreSQL.
- [ ] Schema evolves through migrations.
- [ ] Backend restart preserves acknowledged data.

## Multi-device

- [ ] Same Google account on two sessions resolves to one internal user.
- [ ] Separate-stitch updates from two sessions both survive.
- [ ] Same-stitch conflict follows latest successful server mutation.
- [ ] Stale aggregate version returns conflict.
- [ ] Focus refresh sees committed changes.
- [ ] Active-project refresh occurs approximately every 30 seconds.
- [ ] No SignalR/WebSocket implementation is required.

## Blended floss

- [ ] Single usage contains exactly one component.
- [ ] Blend contains two or more components.
- [ ] Three-component blend is valid at domain level.
- [ ] Null strand count is supported.
- [ ] Zero/negative strand count is rejected.
- [ ] Blend UI shows each component plus symbol.
- [ ] Blend UI does not present a synthetic mixed catalog color.
- [ ] One blend component can be substituted independently.
- [ ] Inventory resolves each physical component independently.
- [ ] Completing a blended stitch records one stitch state.

## Backups/export

- [ ] Authenticated user can download current data.
- [ ] Current export contains only that user's data/assets.
- [ ] Current export excludes authentication secrets.
- [ ] Current export contains a versioned manifest.
- [ ] Automatic daily backup retains only the latest successful daily artifact.
- [ ] Automatic weekly backup retains only the latest successful weekly artifact.
- [ ] Failed daily replacement does not delete the previous daily backup.
- [ ] Failed weekly replacement does not delete the previous weekly backup.
- [ ] Latest daily backup can be downloaded by its owner.
- [ ] Latest weekly backup can be downloaded by its owner.
- [ ] A second user cannot download another user's backup even with a known backup ID.


## Hosting acceptance criteria

- [ ] Production build produces a runnable container image.
- [ ] Application runs with PostgreSQL outside the container.
- [ ] Production pattern assets are stored outside the container in durable private storage.
- [ ] Production retained backups are stored outside the container in durable private storage.
- [ ] Restarting/replacing the app container preserves all durable data/assets/backups.
- [ ] Missing mandatory production configuration fails startup clearly.
- [ ] Production secrets are not committed or baked into the image.
- [ ] Health/readiness endpoint is available.
- [ ] Database migrations can be run as a separate deployment step.
- [ ] Two concurrent web instances do not create duplicate scheduled daily/weekly backups.
