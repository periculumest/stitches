# Backup & Export Format v1

**Status:** Required design for SH-IDENTITY-002  
**Goal:** Give every Stitch Helper user a portable, downloadable copy of their complete application dataset without exposing multi-user infrastructure or authentication secrets.

---

# 1. Product Requirements

Users must be able to download:

- their current dataset on demand;
- their latest successful daily backup;
- their latest successful weekly backup.

Retained automatic backups:

```text
Daily:  1
Weekly: 1
```

The current on-demand export is generated from live authoritative data.

---

# 2. Archive Format

Use ZIP for the user-downloadable container.

Suggested filename:

```text
stitch-helper-backup-2026-09-07T143000Z.zip
```

Archive:

```text
manifest.json
data/
  patterns.json
  projects.json
  project-progress.json
  inventory.json
  preferences.json
  imports.json
  substitutions.json
  thread-usages.json
  thread-usage-components.json
  thread-catalog.json
assets/
  <asset-id>/
    original.<extension>
```

Exact data-file grouping can follow aggregate boundaries in the implementation, but the format must remain explicit and versioned.

---

# 3. Manifest

Minimum:

```json
{
  "format": "stitch-helper-backup",
  "formatVersion": 1,
  "createdAt": "2026-09-07T14:30:00Z",
  "applicationVersion": "1.2.3",
  "schemaVersion": 12,
  "source": "current-export",
  "sections": {},
  "assets": []
}
```

Allowed `source` values should include:

```text
current-export
daily
weekly
```

For each asset include:

- logical asset ID
- archive-relative path
- original filename
- media type
- byte size
- checksum

---

# 4. Included Data

Include all user-owned durable data needed to reconstruct the user's Stitch Helper workspace:

- normalized pattern definitions
- pattern legends
- thread usages/components
- pattern/project metadata
- project progress
- substitutions
- inventory
- storage locations
- preferences
- import records/corrections
- private pattern source assets
- any user-owned metadata added later

Also include the subset of shared thread catalog records referenced by the exported user's dataset.

---

# 5. Excluded Data

Never export:

- ASP.NET Identity password hashes
- security stamps
- authentication cookies
- session state
- Google access tokens
- Google refresh tokens
- Google ID tokens
- provider secrets/client secrets
- server configuration/secrets
- another user's records
- infrastructure database backups

User-facing backup/export is domain data, not account credential cloning.

---

# 6. IDs

Preserve stable domain IDs inside the archive.

Reason:

- relationships remain deterministic;
- future restore can recreate references safely;
- restore logic can detect collisions/remap if importing into an existing account.

Do not use database sequence order as backup identity.

---

# 7. Serialization

Use a stable application-controlled JSON schema.

Requirements:

- UTF-8
- explicit enum string/value strategy
- ISO-8601 timestamps in UTC
- nullable fields retained when semantically meaningful
- no framework-internal EF tracking/proxy metadata
- no raw database row dumps whose meaning depends on PostgreSQL implementation details

---

# 8. Integrity

Recommended:

- SHA-256 per asset;
- SHA-256 for completed archive;
- section counts in manifest.

The server should not mark a retained automatic backup `Available` until archive construction/integrity validation succeeds.

---

# 9. Current Export Endpoint

Suggested shape:

```text
POST /api/backups/export
```

or

```text
GET /api/export
```

Implementation may choose asynchronous internal generation if necessary, but user-facing behavior in this phase should remain simple.

Authorization always uses the current authenticated user.

---

# 10. Retained Backup Endpoints

Suggested:

```text
GET /api/backups
GET /api/backups/{id}/download
```

`GET /api/backups` returns only current user's retained backup metadata.

Example:

```json
{
  "daily": {
    "id": "...",
    "createdAt": "..."
  },
  "weekly": {
    "id": "...",
    "createdAt": "..."
  }
}
```

---

# 11. Safe Replacement Algorithm

For Daily or Weekly:

```text
existing good backup
        |
generate candidate
        |
serialize data
        |
package assets
        |
write manifest
        |
validate/checksum
        |
mark candidate Available
        |
update retained pointer/record
        |
delete old artifact
```

If any step before `Available` fails:

```text
previous good backup remains
candidate marked failed/cleaned up
```

---

# 12. Future Restore Compatibility

Self-service restore is not part of SH-IDENTITY-002.

However, backup format v1 must be designed so future logic can:

1. validate manifest;
2. verify format version;
3. migrate old backup schema to current import schema;
4. verify asset checksums;
5. create/remap domain records;
6. restore user-owned workspace.

Do not choose a format that requires recreating the exact original PostgreSQL schema/version to recover user data.


# 13. Hosted Storage Requirements

In hosted production:

- retained Daily/Weekly archives must live in durable private object storage;
- archive metadata remains in PostgreSQL;
- archive bytes must not live solely on the app-container filesystem;
- any temporary archive-construction file must be disposable after upload;
- failed upload must not replace the prior successful retained artifact.

Conceptual flow:

```text
PostgreSQL BackupArtifact metadata
        |
        +-- StorageKey
                |
                v
        Private object storage
```

On-demand current exports may be streamed or temporarily staged, but staging must not become authoritative durable storage.

If temporary signed URLs are used for download in a future implementation, authorization must happen first and the URL must be short-lived.
