# Domain & Persistence Design — Identity Phase v2

**Status:** Implementation guidance  
**Target:** ASP.NET Core + EF Core + Npgsql + PostgreSQL

---

# 1. Identity

## ApplicationUser

Use ASP.NET Core Identity:

```csharp
public sealed class ApplicationUser : IdentityUser<Guid>
{
    // Add application-specific profile fields only when justified.
}
```

The Identity schema owns:

- external provider mappings;
- Google provider key;
- security/session-related identity data.

Domain entities reference:

```text
ApplicationUser.Id : Guid
```

Never:

- Google subject IDs
- email addresses
- provider names

---

# 2. Current User Context

Introduce an application abstraction such as:

```csharp
public interface ICurrentUserContext
{
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}
```

The implementation reads authenticated ASP.NET Core claims/session context.

Do not let controllers/services accept a client-provided owner ID as authorization.

---

# 3. Pattern

```text
Pattern
-------
Id
OwnerUserId
Name
Width
Height
SourceAssetId?
ImporterVersion?
CreatedAt
UpdatedAt
Version
```

Constraints:

```text
OwnerUserId required
Version required
```

Pattern remains private to its owner.

---

# 4. Project

```text
Project
-------
Id
OwnerUserId
PatternId
Name
CreatedAt
UpdatedAt
ArchivedAt?
Version
```

Invariant:

```text
Project.OwnerUserId == referenced Pattern.OwnerUserId
```

Enforce in application/service logic and with database constraints where practical.

---

# 5. Sparse Project Progress

Recommended:

```text
ProjectStitchState
------------------
ProjectId
StitchId
State
UpdatedAt
```

Recommended key:

```text
PRIMARY KEY (ProjectId, StitchId)
```

Authorization must resolve project ownership before mutation/query.

For a default unworked state, prefer no stored row where that fits the current progress model.

---

# 6. Thread Catalog

Shared reference data:

```text
ThreadCatalogItem
-----------------
Id
Brand
Code
Name
DigitalColor
CatalogVersion?
```

Recommended uniqueness:

```text
UNIQUE (Brand, Code)
```

Examples:

```text
DMC | 310
DMC | 3799
```

Catalog data is not user inventory.

---

# 7. Thread Usage

```text
ThreadUsage
-----------
Id
Kind        Single | Blend
```

```text
ThreadUsageComponent
--------------------
Id
ThreadUsageId
ThreadCatalogItemId
StrandCount nullable
SortOrder
```

Constraints:

```text
UNIQUE (ThreadUsageId, ThreadCatalogItemId)
```

Validation:

```text
Single -> exactly 1 component
Blend  -> at least 2 components
StrandCount is null OR StrandCount > 0
```

Do not invent a strand count during normalization/import.

---

# 8. Pattern Legend

```text
PatternLegendEntry
------------------
Id
PatternId
Symbol
ThreadUsageId
StitchType
...
```

The symbol references one thread usage.

The thread usage may be a single physical thread or a blend.

---

# 9. Component-Level Substitution

Recommended conceptual shape:

```text
ProjectThreadSubstitution
-------------------------
Id
ProjectId
OriginalThreadUsageComponentId
ReplacementThreadCatalogItemId
CreatedAt
UpdatedAt
```

Rules:

- substitution belongs to project owner;
- replacement changes only that component;
- single-color substitution naturally uses the same model because a single usage has one component;
- deleting/reverting the substitution restores original component.

If substitutions also alter strand count in the existing product requirements, preserve that separately rather than encoding strand count into a fake thread ID.

---

# 10. User Inventory

```text
UserInventory
-------------
Id
OwnerUserId
CreatedAt
UpdatedAt
```

```text
InventoryItem
-------------
Id
UserInventoryId
ThreadCatalogItemId
BobbinQuantity
StorageBox
StorageRow
Notes?
```

A blend is never an inventory catalog item.

Required thread lookup for a blend independently resolves each `ThreadCatalogItemId`.

---

# 11. Pattern Source Asset

```text
PatternSourceAsset
------------------
Id
OwnerUserId
StorageKey
OriginalFileName
MediaType
ByteSize
Checksum
CreatedAt
```

The storage key is opaque and server-generated.

Use an interface such as:

```csharp
public interface IPatternAssetStore
{
    Task<StoredAsset> PutAsync(...);
    Task<Stream> OpenReadAsync(...);
    Task DeleteAsync(...);
    Task<bool> ExistsAsync(...);
}
```

Environment-specific implementations:

```text
Development:
  LocalPatternAssetStore

Hosted production:
  ObjectStoragePatternAssetStore
```

Local storage is a development convenience only. Hosted production must use durable private storage outside the application container. Physical/object storage must never be publicly exposed by default.

---

# 12. User Preferences

```text
UserPreferences
---------------
UserId
...
Version
```

One-to-one with user unless a future requirement introduces device-specific preferences.

---

# 13. Import Record

```text
ImportRecord
------------
Id
OwnerUserId
PatternId?
SourceAssetId
ImporterVersion
NormalizationVersion?
Status
StartedAt
CompletedAt?
ErrorCode?
```

Import data remains private.

---

# 14. Backup Artifact

```text
BackupArtifact
--------------
Id
OwnerUserId
Kind                Daily | Weekly
FormatVersion
StorageKey
ByteSize
Checksum
CreatedAt
Status              Creating | Available | Failed
```

Recommended uniqueness/retention behavior should ensure at most one retained `Available` daily and one retained `Available` weekly artifact per user after cleanup/supersession.

Do not delete the previous available artifact before the replacement archive is completely generated and validated.

On-demand current export does not have to be retained as a `BackupArtifact` unless implementation simplicity favors temporarily storing it.

---

# 15. Portable Backup Manifest

Example:

```json
{
  "format": "stitch-helper-backup",
  "formatVersion": 1,
  "createdAt": "2026-09-07T14:00:00Z",
  "applicationVersion": "x.y.z",
  "schemaVersion": 12,
  "sections": {
    "patterns": 4,
    "projects": 6,
    "progressRecords": 12093,
    "inventoryItems": 88
  },
  "assets": [
    {
      "id": "...",
      "file": "assets/.../pattern.pdf",
      "sha256": "..."
    }
  ]
}
```

Archive structure:

```text
manifest.json
data/
  patterns.json
  projects.json
  progress.json
  inventory.json
  preferences.json
  imports.json
  substitutions.json
  thread-usages.json
  thread-usage-components.json
  relevant-thread-catalog.json
assets/
  ...
```

Include the relevant shared thread catalog rows so the archive remains interpretable independently of the live catalog version.

Exclude all ASP.NET Identity credentials/provider secrets/session state.

---

# 16. Repository Authorization Pattern

Prefer explicit methods:

```text
IPatternRepository.GetOwned(userId, patternId)
IProjectRepository.GetOwned(userId, projectId)
IProjectRepository.ListOwned(userId)
IInventoryRepository.GetForUser(userId)
IBackupRepository.GetOwned(userId, backupId)
```

Preferred query:

```sql
SELECT *
FROM projects
WHERE id = @projectId
  AND owner_user_id = @authenticatedUserId;
```

Avoid retrieving arbitrary rows then performing only controller-side ownership checks.

---

# 17. Concurrency

## Progress

Use narrow state upserts/deletes.

## Larger aggregates

Use explicit application version:

```text
Version : long
```

Flow:

```text
read Version 10
submit expectedVersion 10
successful update -> Version 11
stale Version 10 -> conflict
```

Do not couple domain concurrency behavior directly to PostgreSQL-specific `xmin`.

---

# 18. Local Development

Recommended:

```text
React                  local dev process
ASP.NET Core            dotnet run/watch
PostgreSQL              Docker Compose
Protected asset store   local filesystem outside web root
```

Configuration should include:

```text
ConnectionStrings:StitchHelper
Authentication:Google:ClientId
Authentication:Google:ClientSecret
Authentication:SessionDays = 30
Storage:PrivateAssetRoot
Storage:BackupRoot
PublicBaseUrl
```

Secrets must use development secret storage/environment variables and must not be committed.


# 19. Hosting Infrastructure Boundaries

Recommended infrastructure abstractions:

```csharp
IPatternAssetStore
IBackupArtifactStore
ICurrentUserContext
IBackupSchedulerOrJobCoordinator
```

Production adapters may target a specific hosting provider, but application/domain code must not reference provider SDK types directly.

## Backup storage

Retained backup artifacts should be stored through `IBackupArtifactStore`, separate from database metadata.

```text
BackupArtifact table
    |
    +-- StorageKey
            |
            v
IBackupArtifactStore
```

A container-local path is not a valid durable production `StorageKey` implementation.

## Application instances

No singleton product state may live only in process memory.

Safe in-memory state:

- request-local data;
- short client mutation batching;
- caches that can be rebuilt;
- transient archive construction buffers.

Not safe as process-only state:

- authoritative project progress;
- auth identity;
- backup retention metadata;
- backup schedule ownership;
- source assets.

## Deployment migration command

The Infrastructure project should expose a documented migration path, for example through:

```text
dotnet ef database update
```

or a small dedicated migration executable/job using the same compiled migrations.

The exact command may follow repository conventions, but production deployment must be able to migrate separately from normal web startup.
