# Hosting & Deployment Architecture

**Status:** Locked architecture guidance  
**Applies to:** SH-IDENTITY-002 and all subsequent phases  
**Goal:** Make the application hostable immediately without creating a second architecture for production.

---

# 1. Design Principle

Local development and hosted production may use different infrastructure adapters, but they must run the same application/domain behavior.

Hosting must not require replacing core persistence, identity, ownership, backup, or asset models.

---

# 2. Recommended Production Topology

```text
                       Internet
                          |
                     HTTPS ingress
                          |
                +-------------------+
                | Stitch Helper App |
                | ASP.NET + React   |
                +-------------------+
                    |           |
                    |           |
              PostgreSQL    Private Object
                             Storage
                              |       |
                              |       +-- backup archives
                              +---------- pattern source assets
```

The web application may run one or more instances.

All durable data lives outside those instances.

---

# 3. Application Packaging

Produce a production container image.

Recommended characteristics:

- multi-stage build;
- React production build served through the ASP.NET Core deployment;
- no development server in production;
- non-root runtime where practical;
- deterministic application version included in build metadata;
- no secret values baked into image;
- no writable durable-data directory required inside the container.

The same image should be deployable across common container-capable hosting platforms.

---

# 4. Configuration

Use environment/platform configuration for:

```text
ASPNETCORE_ENVIRONMENT
ConnectionStrings__StitchHelper
Authentication__Google__ClientId
Authentication__Google__ClientSecret
Authentication__SessionDays
PublicBaseUrl
Storage__Provider
Storage__PatternBucketOrContainer
Storage__BackupBucketOrContainer
Storage__Endpoint
Storage credentials / workload identity
BackupJobs configuration
```

Do not commit production values.

Development configuration may use user-secrets and Docker Compose.

---

# 5. Google OAuth Deployment

Google OAuth redirect/callback configuration must derive from the deployed public URL.

Requirements:

- HTTPS in production;
- production callback URI documented;
- localhost development callback URI separate from production;
- production Google client secret stored in platform secret manager/config;
- no provider token persistence beyond what login requires.

Changing the public hostname should require configuration changes, not code changes.

---

# 6. PostgreSQL

Production:

- external/managed PostgreSQL preferred;
- persistent storage outside web instances;
- TLS database connection where provider supports/requires it;
- credentials supplied as secrets;
- connection pooling through Npgsql/EF Core;
- migrations versioned in source control.

## Deployment migration sequence

Preferred release:

```text
build image
    |
run migration task/job
    |
deploy/roll web instances
    |
readiness succeeds
    |
traffic served
```

For potentially breaking migrations in future phases, use expand/contract migrations so old/new app versions can overlap during rolling deployment.

---

# 7. Object Storage

Hosted production must use durable private storage for:

- original/imported pattern assets;
- retained daily backups;
- retained weekly backups.

Local filesystem implementations remain valid for development/tests only.

Object storage must be private by default.

Application database stores metadata + opaque keys, not public URLs.

---

# 8. User Downloads

Two valid hosted download patterns:

## Application streaming

```text
User
 -> authorized API
 -> app reads private object
 -> app streams response
```

Simple and secure; preferred initially.

## Short-lived signed URL

May be introduced later:

```text
User
 -> authorized API
 -> app creates short-lived signed URL
 -> user downloads from object storage
```

Only after application authorization.

No permanent public URLs.

---

# 9. Background Jobs

Daily and weekly automatic backups require scheduled execution.

Avoid:

```text
web app starts
  -> Timer fires every N hours
```

because two app instances would both execute it.

Preferred provider-neutral patterns:

### Option A — Platform scheduler invokes protected job endpoint/command

Good for near-term simple hosting.

### Option B — Dedicated worker service

Worker shares PostgreSQL/object storage and uses a distributed lock.

### Option C — Durable job scheduler

Use if scheduling complexity grows later.

For first hosting, Option A is the preferred simplicity unless the chosen host lacks a reliable scheduler.

---

# 10. Backup Job Locking

Even with a scheduler, backup creation should be idempotent for a schedule window.

Use a durable key such as:

```text
(userId, backupKind, scheduleDate)
```

and a database uniqueness constraint/job record so duplicate invocations cannot create conflicting retained artifacts.

---

# 11. Health Checks

Expose:

```text
/health/live
/health/ready
```

Suggested semantics:

### Liveness

Process is running.

### Readiness

Application can safely serve requests and critical dependencies such as PostgreSQL are reachable.

Do not perform expensive object-storage probes on every public readiness request if the hosting platform calls it frequently; use a lightweight strategy.

---

# 12. Logging

Write structured logs to stdout/stderr so hosting platforms can collect them.

Do not require local log files for production observability.

Log:

- request/correlation IDs;
- internal user/resource IDs when appropriate;
- authentication outcome category;
- backup job outcome;
- storage/database failure category.

Never log:

- Google tokens;
- auth cookies;
- secrets;
- raw imported pattern content.

---

# 13. Scaling Safety

The application does not need sophisticated autoscaling now, but must be safe if a second web instance is added.

Therefore:

- no in-process authoritative session/user data;
- no local durable file dependency;
- no uncoordinated scheduled jobs;
- concurrency enforced in PostgreSQL;
- cookies work across instances using stable shared data-protection configuration if the hosting topology requires it.

If ASP.NET Core Data Protection keys are not automatically shared/persisted by the selected host, configure durable/shared key storage before multiple app instances are enabled. Losing/changing keys can invalidate auth cookies.

---

# 14. Deployment Environments

Recommended minimum:

```text
Development
Production
```

A staging environment may be added as soon as commercial/public changes become frequent.

Each environment must have isolated:

- PostgreSQL database;
- Google OAuth configuration as required;
- private object-storage namespace/bucket/container;
- secrets.

Never point development at production user data.

---

# 15. First Hosted Release Checklist

- [ ] DNS/public hostname selected.
- [ ] HTTPS enabled.
- [ ] Google production OAuth client configured.
- [ ] production PostgreSQL created.
- [ ] production private object storage created.
- [ ] secrets configured in host.
- [ ] database migrations executed.
- [ ] container deployed.
- [ ] health/readiness passes.
- [ ] Google login succeeds.
- [ ] pattern upload/import succeeds.
- [ ] pattern asset survives container restart.
- [ ] project progress survives container restart.
- [ ] current export downloads.
- [ ] daily backup job executes.
- [ ] weekly backup job configuration verified.
- [ ] retained backup survives container restart.
- [ ] unauthorized asset/backup access fails.
- [ ] logging available from hosting platform.

---

# 16. Hosting Provider Selection Criteria

The architecture intentionally does not require a specific provider.

The chosen host should make these straightforward:

- container deployment;
- HTTPS/custom domain;
- secret/environment configuration;
- managed PostgreSQL or easy external PostgreSQL connectivity;
- private object storage or S3-compatible/Azure-compatible storage;
- scheduled jobs/cron;
- persistent logging/monitoring;
- straightforward rollback/redeploy.

A provider that lacks durable external storage or scheduled jobs should not force Stitch Helper back onto container-local persistence.
