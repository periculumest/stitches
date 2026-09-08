# Implementation Slices — SH-IDENTITY-002

**Status:** Codex-ready execution plan

---

# Slice 1 — PostgreSQL Foundation

## In scope

- PostgreSQL
- EF Core/Npgsql
- versioned migrations
- local Docker Compose database
- move authoritative persistence server-side

## Out of scope

- Google login
- multi-user authorization
- backups
- blends

## Acceptance

- [ ] clean PostgreSQL instance can be initialized through migrations;
- [ ] current core domain data can persist/retrieve;
- [ ] server restart preserves state;
- [ ] no production drop/recreate behavior.

---

# Slice 2 — ASP.NET Identity + Google Login

## In scope

- `ApplicationUser<Guid>`
- ASP.NET Core Identity persistence in PostgreSQL
- Google external provider
- current-user endpoint
- 30-day sliding cookie
- logout
- antiforgery integration

## Acceptance

- [ ] Google sign-in succeeds;
- [ ] returning Google identity resolves same internal user;
- [ ] React never stores Google tokens;
- [ ] state-changing authenticated requests use antiforgery protection;
- [ ] session configuration matches decision record.

---

# Slice 3 — User Ownership Retrofit

## In scope

Ownership for:

- Pattern
- Project
- source asset
- inventory
- preferences
- imports
- substitutions
- progress authorization paths

Introduce `ICurrentUserContext`.

## Acceptance

- [ ] all private API paths derive owner from server auth context;
- [ ] AUTHZ tests pass;
- [ ] no global/default user behavior remains;
- [ ] no project may reference another user's pattern.

---

# Slice 4 — Private Asset Storage

## In scope

- `IPatternAssetStore`
- local protected implementation for development
- durable private object-storage implementation/configuration for hosted production
- opaque storage key
- asset metadata/checksum
- authenticated reads

## Acceptance

- [ ] source files are outside static web root;
- [ ] unauthenticated direct retrieval impossible;
- [ ] import pipeline can still read owned asset;
- [ ] cross-user asset test passes;
- [ ] production configuration stores assets outside the web container;
- [ ] container replacement does not lose source assets.

---

# Slice 5 — Multi-Device Progress Persistence

## In scope

- sparse/narrow stitch progress
- batched mutation endpoint
- 250-500 ms client mutation buffer
- saving state UX
- 30-second active refresh
- focus refresh
- explicit aggregate versioning

## Out of scope

- SignalR
- offline-first queue

## Acceptance

- [ ] distinct concurrent stitch changes both survive;
- [ ] same-stitch last server commit wins;
- [ ] stale aggregate edit conflicts;
- [ ] saving failure is visible;
- [ ] second browser receives remote changes on refresh/focus/poll.

---

# Slice 6 — Thread Catalog + Blended Floss

## In scope

- shared physical thread catalog
- single/blend thread usage
- nullable strand count
- split/striped blend visualization
- inventory projections
- component-level substitution
- importer/model updates

## Acceptance

- [ ] two+ component blends work end to end;
- [ ] three-component blend supported by domain;
- [ ] null strand count supported;
- [ ] synthetic mixed RGB is not used as catalog identity;
- [ ] one blend component can be substituted independently;
- [ ] single-color substitution uses same component model;
- [ ] existing ordinary patterns do not regress.

---

# Slice 7 — Portable User Backup/Export

## In scope

- backup archive format v1
- manifest
- JSON data sections
- relevant thread catalog records
- owned source assets
- current on-demand export
- protected backup storage/download

## Acceptance

- [ ] user can download current dataset;
- [ ] archive is user-scoped;
- [ ] archive includes owned assets;
- [ ] archive excludes auth secrets;
- [ ] archive manifest is versioned;
- [ ] another user cannot download it.

---

# Slice 8 — Automatic Daily/Weekly Backups

## In scope

- scheduled daily user backup creation
- scheduled weekly user backup creation
- latest-one retention for each kind
- safe replacement behavior
- Backups & Export UI
- download retained daily/weekly archive

## Retention

```text
Daily:  1 latest successful artifact
Weekly: 1 latest successful artifact
```

## Acceptance

- [ ] new daily backup safely supersedes old daily;
- [ ] failed daily generation preserves old daily;
- [ ] new weekly backup safely supersedes old weekly;
- [ ] failed weekly generation preserves old weekly;
- [ ] UI exposes timestamps/download actions;
- [ ] current export remains independently available.

---



# Slice 9 — Hosted Deployment Backbone

## In scope

- production Dockerfile/container build
- environment/secret-driven configuration
- external PostgreSQL configuration
- production private object storage for assets/backups
- health/readiness endpoints
- explicit migration/release command
- hosted scheduler/worker pattern for daily/weekly backups
- singleton/distributed-lock safety for scheduled jobs
- deployment documentation

## Out of scope

- provider-specific autoscaling optimization
- Kubernetes requirement
- multi-region deployment
- CDN optimization
- production billing infrastructure

## Acceptance

- [ ] clean hosted environment can be configured without source-code changes;
- [ ] app container is stateless for durable product data;
- [ ] PostgreSQL is external and durable;
- [ ] private assets/backups are external and durable;
- [ ] Google OAuth callback/public URL are configurable;
- [ ] HTTPS deployment supports secure auth cookies;
- [ ] migrations run as a separate deployment step;
- [ ] health/readiness checks work;
- [ ] multiple app instances do not duplicate scheduled backups;
- [ ] restart/replacement of web container loses no durable user data.

---

# Slice 10 — Hardening & Release Gate


## In scope

- full authorization suite
- two-user isolation rehearsal
- two-browser sync rehearsal
- backup inspection
- security/session configuration review
- failure-state UX
- documentation

## Release gate

- [ ] PostgreSQL is authoritative;
- [ ] Google login works;
- [ ] session is 30-day sliding;
- [ ] every private resource is user-scoped;
- [ ] source assets are private;
- [ ] two-device progress does not lose disjoint updates;
- [ ] SignalR is not required;
- [ ] blends work, including nullable strand count;
- [ ] component substitution works;
- [ ] current user export downloads successfully;
- [ ] one retained daily and one retained weekly backup work;
- [ ] backup isolation tests pass;
- [ ] no old-user migration subsystem exists;
- [ ] required configuration docs contain no secrets.

---

# Recommended Codex Order

```text
1. PostgreSQL
2. Identity + Google
3. Ownership
4. Private assets
5. Multi-device progress
6. Thread catalog/blends/substitutions
7. Portable export
8. Automatic backups
9. Hosted deployment backbone
10. Security + regression hardening
```
