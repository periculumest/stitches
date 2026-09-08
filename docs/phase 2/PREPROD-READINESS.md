# Preproduction readiness: owner setup guide

Prepared 2026-09-07. No cloud resources have been created or deployed by this implementation.

This is the work you need to complete to give Stitch Helper a real Google login and a durable hosted home. The concrete path below uses Google Cloud Run, Cloud SQL for PostgreSQL, private Cloud Storage, Secret Manager, and Cloud Scheduler. Another container host can use the same image, database, and storage interfaces.

## 1. Choose an environment and create the Google Cloud project

1. Create a Google Cloud project dedicated to Stitch Helper preproduction. Enable billing and set a budget alert in the console.
2. Choose a region near your initial users. Keep the application, database, and buckets in that region.
3. Enable Cloud Run, Cloud SQL Admin, Cloud Storage, Artifact Registry, Secret Manager, Cloud Scheduler, and Cloud Build if you will build images in Google Cloud.
4. Keep development and production in separate databases, buckets, secret sets, and preferably separate Cloud projects. Do not point local development at production data.
5. Record these non-secret choices:

| Item | Your value |
| --- | --- |
| Google Cloud project ID | |
| Region | |
| Container registry/repository | |
| Cloud Run service name | |
| Public origin, once selected | |
| Cloud SQL instance connection name | |
| Database name | |
| Pattern bucket | |
| Backup bucket | |
| Operator / alert recipient | |

## 2. Choose the public address

You can start with Cloud Run's generated HTTPS service URL. A purchased domain is optional for preproduction.

For a custom domain, obtain the domain, configure its HTTPS routing to the Cloud Run service, and use one canonical origin such as `https://stitch.example.com`. Follow the host's current domain/HTTPS workflow. Do not serve the application container directly over public HTTP.

If the generated service URL is not known yet, use this bootstrap sequence:

1. Complete database, storage, secrets, image, and migration preparation below.
2. Create the Cloud Run service **requiring platform authentication**, temporarily setting `PublicBaseUrl=https://setup.invalid`.
3. Copy the generated service URL from the console.
4. Replace `PublicBaseUrl` with that exact origin and register its Google callback below.
5. Roll out the corrected revision before allowing public invocation. Never test end-user login against the placeholder origin.

After setup, the exact canonical origin must match `PublicBaseUrl` and the Google callback. Changing hostnames only requires configuration and OAuth-client updates.

## 3. Create the Google OAuth clients

In **Google Auth Platform**, configure Branding, Audience, and Data Access. Supply the app name, support/developer contact, and the homepage/privacy information required by the console. For preproduction testing, add your intended Google test accounts where the audience setup requires them. Follow any verification or publishing requirements shown before opening access beyond the test audience. [Google setup guide](https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid)

As a Stitch Helper preproduction readiness requirement, establish all three public pages below and enter their final URLs in the corresponding **Branding** fields. This checklist does not imply that Google requires every field for every testing or publishing configuration; follow the current console requirements as well.

| Page | Content to establish | Final public HTTPS URL |
| --- | --- | --- |
| Application homepage | Explain Stitch Helper, identify the operator/support contact, and link to sign-in, the privacy policy, and terms of service. | To be established |
| Privacy policy | Describe the app's actual handling of Google profile/email information, uploaded patterns, saved progress, backups, retention, and requests for deletion or support. | To be established |
| Terms of service | Establish the terms for using the application, including users' responsibilities for uploaded pattern content and the support contact. | To be established |

Publish these pages at stable HTTPS addresses accessible without signing in. Link the privacy policy and terms from the application's sign-in/home experience so users can read them before authorizing access. Record the final URLs above and check any domain authorization/ownership requirements shown by Google Auth Platform. These are outstanding setup/content tasks; adding this reminder does not create the pages or approve their policy text.

Create a client with application type **Web application**. Prefer separate development and hosted clients.

Register exact **Authorized redirect URIs**:

| Environment | Redirect URI |
| --- | --- |
| Built local app, opened at 127.0.0.1 | `http://127.0.0.1:5057/signin-google` |
| Built local app, opened at localhost | `http://localhost:5057/signin-google` |
| Vite development UI | `http://127.0.0.1:5173/signin-google` |
| Hosted app | `https://YOUR-CANONICAL-HOST/signin-google` |

The external provider callback is **`/signin-google`**, not `/auth/callback`. ASP.NET handles the Google callback, then internally redirects to `/auth/callback` to issue the application session. Redirect matching is exact; scheme, hostname, port, and path matter. This server-side flow does not require a browser Google JavaScript SDK. [Google web-server OAuth flow](https://developers.google.com/identity/protocols/oauth2/web-server)

Only basic identity/profile/email access is requested. Users do not grant Drive, Gmail, or Cloud Storage access. The application's service account handles storage separately.

For local development, set the client through .NET user secrets:

```powershell
dotnet user-secrets set --project server "Authentication:Google:ClientId" "YOUR-DEVELOPMENT-CLIENT-ID"
dotnet user-secrets set --project server "Authentication:Google:ClientSecret" "YOUR-DEVELOPMENT-CLIENT-SECRET"
```

Use your local secret-entry workflow to avoid retaining actual secrets in command history. For hosted use, enter values directly into Secret Manager and map them to the environment names in step 7. Never put them in source files or container build arguments.

## 4. Create PostgreSQL

1. Create a managed **PostgreSQL 17** Cloud SQL instance and a database, for example `stitch_helper`.
2. Enable automated infrastructure backups and point-in-time recovery according to your intended recovery policy. Record the retention and recovery procedure. User ZIP exports do not replace this.
3. Create a migration database role with permission to create/alter schema, and a runtime role with only required data access. For a small private preproduction rehearsal, one dedicated app role can simplify setup; separate migration privileges before public operation.
4. Configure the Cloud Run service and each job with the Cloud SQL instance connection. Give their service account the **Cloud SQL Client** role. The Cloud Run attachment provides the authenticated transport; do not open PostgreSQL to every internet address. [Cloud SQL connection guide](https://docs.cloud.google.com/sql/docs/postgres/connect-run)
5. Store the Npgsql connection string as a secret.

For the Cloud Run Unix socket attachment, an example connection string is:

```text
Host=/cloudsql/PROJECT:REGION:INSTANCE;Database=stitch_helper;Username=APP_DATABASE_USER;Password=DATABASE_PASSWORD;Maximum Pool Size=20
```

For a managed database reached over TCP, use the provider's hostname and certificate settings, normally `SSL Mode=VerifyFull`, with its required root certificate. Do not use certificate-validation bypasses. Npgsql also supports a Unix socket directory as `Host`. [Npgsql connection parameters](https://www.npgsql.org/doc/connection-string-parameters.html)

After migrations, a separate runtime role needs schema usage; SELECT/INSERT/UPDATE/DELETE on application and Identity tables; appropriate sequence access, including Data Protection key IDs; and SELECT on `__EFMigrationsHistory` for readiness. Add default privileges for future migrations. Verify this with the actual role before release.

## 5. Create private storage and the runtime service account

Create two buckets, one for original pattern assets and one for backup archives. Use unique names within the environment.

For **both** buckets:

- Enable **uniform bucket-level access**.
- Set **public access prevention** explicitly to **enforced**.
- Do not grant `allUsers` or `allAuthenticatedUsers` access.
- Do not configure public website hosting or public download URLs.
- Do not apply an age-based lifecycle rule that deletes live pattern assets or retained backup objects.

The application checks both privacy settings at startup and refuses to run if they are missing. [Uniform access](https://docs.cloud.google.com/storage/docs/using-uniform-bucket-level-access), [public access prevention](https://docs.cloud.google.com/storage/docs/using-public-access-prevention)

Create a dedicated runtime service account. Give it:

- Bucket-scoped object create/read/delete access to the two buckets; a bucket-scoped **Storage Object Admin** role is a straightforward initial choice.
- The `storage.buckets.get` permission on those buckets for the startup privacy check, supplied through a minimal custom role or a suitable bucket metadata role. Object Admin alone does not supply this metadata permission.
- Secret accessor access only to the specific secrets it consumes.
- Cloud SQL Client when using the Cloud SQL attachment.

Attach this service account to the web service and backup jobs. Cloud Storage uses Application Default Credentials automatically from the workload identity; no downloadable service-account JSON key is needed on Cloud Run. Local testing against test buckets can use `gcloud auth application-default login`. [Cloud Storage authentication](https://docs.cloud.google.com/storage/docs/authentication)

Decide bucket recovery/versioning/soft-delete policy separately from product retention. The app retains one live daily and one live weekly archive; provider recovery copies may remain according to the operator policy. Monitor and eventually clean unreferenced candidates left by abrupt process termination after upload.

## 6. Prepare the shared Data Protection certificate

Cookie encryption keys are persisted in PostgreSQL. Production additionally requires a certificate that encrypts those keys at rest. This certificate is separate from the HTTPS certificate managed by your host.

1. Generate an RSA certificate with its private key and export it as a password-protected PFX, using your organization's certificate tooling or OpenSSL.
2. Base64-encode the **PFX bytes** without line breaks. Do not base64-encode a PEM certificate without its private key.
3. Store the encoded PFX and its password as separate secrets.
4. Give every app instance and backup job the same values. Keep a protected recovery copy alongside your operator disaster-recovery material.
5. Record the certificate's expiry and a rotation plan. Do not simply replace this secret and discard the old private key: existing encrypted Data Protection keys require it for decryption.

Example OpenSSL flow in a temporary directory outside the repository:

```text
openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -keyout stitch-dp-key.pem -out stitch-dp-cert.pem -subj "/CN=StitchHelper Data Protection"
openssl pkcs12 -export -inkey stitch-dp-key.pem -in stitch-dp-cert.pem -out stitch-dp.pfx
```

Both commands prompt for their required passwords. Use your secret manager's file-import/secret-entry workflow for the base64 PFX and password, then remove temporary private-key files through your normal secure handling process. Do not commit them. [ASP.NET Data Protection persistence](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-8.0)

## 7. Configure the image and environment

Build the image locally or in your build service:

```text
docker build --build-arg APP_VERSION=0.2.0 -t YOUR_REGISTRY/stitch-helper:0.2.0 .
docker push YOUR_REGISTRY/stitch-helper:0.2.0
```

Pin the deployed image by digest after building. No secret is needed to build it.

Map these settings on the web service and the daily/weekly jobs:

| Environment variable | Value / source |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_URLS` | `http://0.0.0.0:8080` (image default) |
| `PublicBaseUrl` | Exact canonical HTTPS origin; no path/query |
| `ConnectionStrings__StitchHelper` | PostgreSQL connection secret |
| `Authentication__Google__ClientId` | Hosted OAuth client ID |
| `Authentication__Google__ClientSecret` | Hosted OAuth client secret |
| `Storage__Provider` | `GoogleCloudStorage` |
| `Storage__PatternBucketOrContainer` | Private pattern bucket name, without `gs://` |
| `Storage__BackupBucketOrContainer` | Private backup bucket name, without `gs://` |
| `DataProtection__CertificateBase64` | Encoded private PFX secret |
| `DataProtection__CertificatePassword` | PFX password secret |
| `Import__MaxMegabytes` | `25` for Cloud Run's default HTTP/1 ingress |

Local filesystem storage is rejected in production. No application data directory or volume needs to be copied into the image. Google OAuth tokens are neither browser-managed nor persisted.

Cloud Run limits HTTP/1 request size to 32 MiB. The recommended 25 MB setting leaves room for multipart overhead and is reflected in the upload UI. Archive downloads are chunked/streamed. The app stages exports temporarily and caps uncompressed exports at 2 GB. Cloud Run's writable filesystem counts toward instance memory: start with low per-instance concurrency, allocate enough memory for your tested export envelope, and test a representative largest import/export before choosing limits. This phase is not a capacity guarantee for simultaneous multi-gigabyte exports. [Cloud Run limits](https://docs.cloud.google.com/run/quotas)

Set a request timeout that accommodates your tested import/export sizes. Begin with a single instance or a small maximum instance count for the rehearsal; database connection pools and memory requirements multiply with instances. Confirm runtime support/security updates before selecting the final release image.

## 8. Run migrations as a release job

Create a Cloud Run job using the **same image**, with container arguments:

```text
--migrate
```

Keep the image entrypoint (`dotnet StitchHelper.dll`). Supply the database connection secret and Cloud SQL attachment. This command intentionally needs no Google OAuth, bucket, or Data Protection secrets.

Run the job and verify successful completion **before** routing traffic to a new application revision. It applies the checked-in EF migration and the 454-color DMC reference catalog. It does not drop/recreate the database. Re-running it is safe.

For local Docker, the equivalent is:

```text
docker run --rm --env-file YOUR_PRIVATE_ENV_FILE YOUR_IMAGE --migrate
```

The migration connection must address PostgreSQL from inside the container; `localhost` there refers to that container. Use the hosting attachment/network endpoint for deployed jobs.

## 9. Deploy and schedule backups

Deploy the web service with port 8080, the runtime service account, Cloud SQL attachment, and the settings above. Configure startup/readiness monitoring against `/health/ready` and liveness against `/health/live`. Use the host's supported HTTP probe configuration. Readiness remains unsuccessful if the schema/catalog is not prepared. [Cloud Run health checks](https://docs.cloud.google.com/run/docs/configuring/healthchecks)

After the canonical origin and OAuth callback are correct, allow public invocation of the web service: end users reach the sign-in page, while the application protects private APIs with its own session. Cloud Run IAM authentication is not the same as the app's Google login.

Create two more Cloud Run jobs from the same image/configuration:

| Job | Container arguments | Example schedule (UTC) |
| --- | --- | --- |
| Daily backups | `--backup`, `daily` | `0 3 * * *` |
| Weekly backups | `--backup`, `weekly` | `0 4 * * 1` |

Use one task per job. Configure sensible retries and a timeout sufficient for all current accounts. Each invocation processes all users. PostgreSQL locks and schedule receipts make repeated/concurrent execution safe. Failed runs return a nonzero exit code, and old retained backups remain available.

Use Cloud Scheduler to invoke the jobs with a dedicated scheduler service account granted permission to run those jobs. Follow the job scheduling console workflow; there is no public application job endpoint or secret URL to configure. [Schedule Cloud Run jobs](https://docs.cloud.google.com/run/docs/execute/jobs-on-schedule)

Run both jobs manually once, check their success logs, and verify their timestamps in **Backups & Export**. Configure alerts for failed executions and verify alerts reach the named operator. A web service being online does not mean the scheduled jobs have been configured.

## 10. Complete the preproduction release rehearsal

Record the environment, image digest, date, tester, and evidence for every result. Do not mark these as passed from local tests alone.

- [ ] `/health/live` and `/health/ready` return success on the hosted origin.
- [ ] The application homepage, privacy policy, and terms of service are published at the URLs recorded in step 3 and open in a signed-out/private browser without authentication.
- [ ] Google Auth Platform Branding contains those final URLs, and the application's sign-in/home experience links to the privacy policy and terms of service.
- [ ] A real Google account signs in, `/api/me` resolves it, and a returning login uses the same internal ID.
- [ ] The application cookie is HttpOnly, Secure, SameSite=Lax and configured for 30-day sliding expiration. Logout removes the current browser session.
- [ ] A second Google account has an empty independent workspace and cannot fetch the first account's project, source, pattern, or backup IDs.
- [ ] An uploaded PDF is retained privately and can be opened only through the authorized app route.
- [ ] Direct anonymous access to pattern and backup bucket objects fails.
- [ ] Two browsers using the same account can mark different stitches and see both after focus/30-second refresh.
- [ ] A stale rename/definition/inventory edit returns a conflict; it does not replace newer data.
- [ ] A blend with known and unspecified strand counts displays all colors/symbols and allows one-component substitution and reversal.
- [ ] A current export downloads and its ZIP manifest, JSON, checksums, and original assets are present, with no authentication data.
- [ ] Daily and weekly jobs produce one retained archive of each kind; an intentional failed replacement preserves the previous archive.
- [ ] Replace the entire web revision/container with a fresh instance of the same image/configuration. The existing session, projects, progress, sources, inventory, and backups survive.
- [ ] Run two app instances against the same services and repeat progress and backup-job contention checks.
- [ ] Test a representative large import/export under the chosen ingress, timeout, memory, and concurrency limits.
- [ ] Logs and job-failure alerts are accessible; logs do not include OAuth callbacks' query values, cookies, or source contents.
- [ ] Rehearse an operator database/object-store restore in an isolated environment and document the result.

Self-service account deletion, commercial-launch legal review of the published policies/terms, billing, broader import compatibility, and self-service archive restore remain in `06-commercial-readiness-backlog.md`. The public pages in step 3 are a preproduction readiness task; passing this technical phase does not mark the broader public-commercial-launch items complete.

## Local setup while cloud setup is pending

```powershell
docker compose up -d postgres
# Configure the development Google client through user secrets (step 3).
.\Start.ps1 -Migrate
```

Open `http://127.0.0.1:5057`. Normal later launches use `.\Start.ps1 -SkipBuild` and do not migrate automatically.

For Vite development, run the API with `ASPNETCORE_ENVIRONMENT=Development`, run `npm --prefix web run dev`, open `http://127.0.0.1:5173`, and register that origin's `/signin-google` callback. Vite proxies `/api`, `/auth`, and `/signin-google`.

Run the full local suite with `./Test.ps1`. It uses the Compose PostgreSQL connection by default, creates isolated test databases and a test-only signed-cookie fixture, and runs backend/browser/restart/backup checks without requiring a live Google client. Override `-PostgresConnection` to use another **test** PostgreSQL instance that permits creating databases. Browser test databases and artifacts are retained for inspection.
