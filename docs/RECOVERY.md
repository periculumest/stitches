> Historical phase 1 document. For the current PostgreSQL release, see [phase 2 decisions](<phase 2/IMPLEMENTATION-DECISIONS.md>) and [preproduction setup](<phase 2/PREPROD-READINESS.md>). SQLite recovery commands below apply only to the old MVP.

# Backup and recovery

## What is stored

By default the repository’s `.data/` folder contains:

```text
stitch-helper.db        SQLite database (authoritative project and inventory state)
stitch-helper.db-wal    May exist while the app runs
stitch-helper.db-shm    May exist while the app runs
sources/               Original PDFs and import diagnostics
backups/               Automatic/manual ZIP recovery sets
```

Do not copy only a live `.db` file: pending WAL content may be required. Use **Backups & care → Back up now**, then download the resulting archive. A backup includes all source files, inventory, normalized patterns, projects, edits, substitutions, working areas, milestones, and undo/redo.

Daily backups happen while the app is running. Seven automatic archives are retained; manual archives are not rotated. Copies stored beside the database do not protect against disk loss. Periodically download one to a different disk.

## Restore safely on Windows

1. Stop the application with Ctrl+C in its server terminal. Keep the existing data directory in place.
2. Put the downloaded archive somewhere accessible. In the repository directory, run:

   ```powershell
   dotnet run --project server -c Release --no-build -- --restore "C:\Backups\manual-example.zip" "C:\Users\jpric\vibe\stitcher\restored-data"
   ```

   Substitute the actual archive path. The destination must be nonexistent or empty. If no Release build exists, omit `--no-build`.

3. The command extracts into staging, checks checksums, verifies SQLite integrity/schema and retained PDF references, and only then publishes the restored directory. It prints “Backup verified and restored…” on success. A failed check leaves the destination untouched.
4. Start using that directory:

   ```powershell
   .\Start.ps1 -SkipBuild -DataDirectory '.\restored-data'
   ```

5. Open http://127.0.0.1:5057. Confirm project counts, completed stitches, substitutions, inventory locations, and an original PDF. Make a fresh backup. Continue using `-DataDirectory` on subsequent launches or move the verified data to your preferred location while the app is stopped.

The recovery command refuses to overwrite a nonempty directory. There is no need to delete your original data to try recovery. Keep it until the restored version has been checked.

## If a save fails

The workspace shows an error and returns to its last confirmed save. Reload the saved project before continuing. If a request committed but its response was lost, reload recovers the newer server state; an automatic retry could be ambiguous, so the app does not silently replay it. When the server is unavailable, restart it using the same data directory. Clearing browser storage does not remove projects.

## Validation

`CompleteBackupRestoresProgressInventorySourceAndHistory` restores into a separate folder, reopens the database through the repository, checks all critical state and source content, and successfully undoes a restored substitution. `CorruptBackupDoesNotReplaceTarget` proves checksum failure prevents publication. The test suite also verifies seven-file automatic retention and preservation of manual backups.

Restore currently accepts schema version 1 archives up to 2 GB uncompressed. A future schema migration must update restore validation as well as startup initialization.
