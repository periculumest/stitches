# Hosting cost estimate

Prepared 2026-09-07. USD monthly planning estimates, not a provider quote or measured capacity guarantee. No infrastructure or application settings were changed for this estimate.

## Recommended initial budget

Budget **$60–$85/month for six users**, using the Google deployment described in PREPROD-READINESS.md. Start with a single-zone Cloud SQL Enterprise N1 PostgreSQL instance with 1 dedicated vCPU and 3.75 GiB RAM, approximately 20 GiB SSD, backups/PITR, Cloud Run request-based billing with zero minimum instances, and private regional Standard Storage buckets. Choose us-central1 for this pricing example and keep services together.

This provides a managed database but does not include a standby for automatic zone failover. Allocate **$100/month as a budget-alert target**, not a spending cap. Benchmark imports and exports before choosing Cloud Run memory and concurrency; 2 GiB is a starting point for modest libraries, not sufficient assurance for the application's maximum export size.

| Component | Six-user monthly allowance |
| --- | ---: |
| PostgreSQL compute | About $49 |
| Database SSD, infrastructure backups, recovery storage | $5–$10 |
| Cloud Run web service and backup execution | $0–$10 |
| Private source/ZIP storage and object operations | $1–$3 |
| Internet transfer, secrets, scheduler, image registry/builds/logging | $2–$10 |
| Rounded planning envelope | **$60–$85** |

Database arithmetic: 730 hours × ($0.0413/vCPU-hour + 3.75 × $0.007/GiB-hour) = $49.31. SSD is approximately $0.17/GiB-month; 20 GiB costs $3.40. Backup usage is additional. These figures use Enterprise N1 pricing, not Enterprise Plus defaults. [Cloud SQL pricing](https://cloud.google.com/sql/pricing), [Cloud SQL storage overview](https://cloud.google.com/sql)

For a private pilot, a shared-core db-f1-micro database can reduce the total to approximately **$15–$30/month**: compute is about $7.67/month at $0.0105/hour. Its roughly 0.6 GiB RAM is restrictive and shared-core instances have no Cloud SQL SLA. Treat this as a trial configuration requiring workload validation. [Cloud SQL pricing](https://cloud.google.com/sql/pricing?hl=zh-CN), [Cloud SQL FAQ](https://docs.cloud.google.com/sql/docs/postgres/faq)

## Assumptions and exclusions

- One hosted environment, US users, pay-as-you-go prices, 730 hours/month. No temporary trial credits or committed-use discounts.
- Illustrative average per user: 0.5 GiB original PDFs, 0.5 GiB per compressed full backup, 0.1 GiB database footprint including indexes, 1 GiB/month internet delivery, and 20 active stitching hours/month. These are assumptions to replace with measurements.
- Daily and weekly full backups, with seven-day bucket soft delete and no additional versioning. Actual archive size depends on chart JSON, progress, compression, and PDF size.
- Available recurring free allowances can reduce web/job costs. They are shared across the billing account, not granted separately to each environment.
- Generated Cloud Run HTTPS URL; no purchased domain, external load balancer, paid VPC connector, NAT gateway, premium support, taxes, or engineering labor included. A custom-domain routing design needs its own estimate.
- Google login uses the existing direct OAuth/session implementation; no Google Workspace seats or paid Identity Platform subscription are included or required by that implementation. Private files are stored in the application's billed buckets, not users' personal Drive quotas.

Cloud Run request-based free allowances include 180,000 vCPU-seconds, 360,000 GiB-seconds, and two million requests/month. Above the allowance, reference rates are $0.000024/vCPU-second and $0.0000025/GiB-second. Idle minimum instances cost extra; zero minimum instances trades idle savings for cold starts. Jobs have separate execution billing rules. [Cloud Run pricing](https://cloud.google.com/run/pricing)

Two scheduler definitions fit within the three-job recurring allowance if available. Small secret usage is usually negligible. The miscellaneous allowance above also reserves room for image/build/log usage rather than assuming all supporting services are free. [Scheduler pricing](https://cloud.google.com/scheduler/pricing), [Secret Manager pricing](https://cloud.google.com/secret-manager/pricing)

## Growth scenarios

These are engineering planning ranges under the assumptions above, not user-count thresholds that automatically require upgrades. More accounts chiefly add stored data; simultaneous activity and import/export work determine compute capacity.

| Active users | Original PDFs | Approximate billed bucket storage including soft-deleted backups | Total monthly planning range |
| ---: | ---: | ---: | ---: |
| 6 | 3 GiB | 33 GiB | $60–$85 |
| 50 | 25 GiB | 275 GiB | $65–$110 |
| 250 | 125 GiB | 1,375 GiB | $100–$250 |
| 1,000 | 500 GiB | 5,500 GiB | $250–$700 |

Larger ranges allow database resizing, more app/backup compute, storage, and delivery. They exclude a high-availability database standby. At reference pricing, 2 vCPU/7.5 GiB database compute is about $99/month; its HA equivalent is about $197/month before storage. Availability requirements can cause this cost increase even with only six users. [Cloud SQL pricing](https://cloud.google.com/sql/pricing)

## What makes data cost grow

Regional Standard Storage is approximately **$0.02/GiB-month**. A further 100 GiB of actual billable objects therefore adds about $2/month. Live, soft-deleted, and noncurrent objects all count. [Storage pricing](https://cloud.google.com/storage/pricing)

The current BackupService rebuilds every user's entire export, including original PDFs, each day and each week. It retains one live daily and one live weekly ZIP. With seven-day soft delete, steady-state storage is approximately:

`source bytes + 2 live ZIPs + 7 deleted daily ZIPs + 1 deleted weekly ZIP`

If each ZIP is approximately the size of the original library, this is **11 times the source library**, rather than three times with only live copies. For example, 100 GiB of original PDFs implies about $22/month of bucket capacity with this recovery policy, versus about $6 for just sources and two live archives. Actual ZIP compression and JSON content change the multiplier. [Soft-delete cost explanation](https://cloud.google.com/resources/storage/soft-delete-announce)

Backups also read, compress, upload, and verify the entire library roughly 35 times per month. Same-region service traffic avoids internet delivery charges, but consumes execution time and object operations. Before large libraries accumulate, consider separating immutable PDF protection from incremental project/progress backups. The current 2 GB uncompressed per-user export limit and temporary-file memory requirements also need redesign or explicit product limits as individual libraries grow.

Browser delivery is roughly **$0.12/GiB** at the initial North American Premium Tier rate: another 100 GiB/month is about $12 before allowances/tiering. This applies to API responses and downloads, not just original PDFs. Current authorized asset downloads pass through Cloud Run, so do not add a second internet egress charge for the same bytes from same-region GCS to Cloud Run. [Network pricing](https://cloud.google.com/vpc/network-pricing), [Cloud Run transfer rules](https://cloud.google.com/run/pricing)

Database cost grows with chart/project JSON, completed-stitch rows, indexes, and history/receipt tables. Browser canvas rendering runs on the user's device; the server handles saves, polling, imports, and exports. At one visible workspace polling every 30 seconds, 20 active hours generate roughly 2,400 refresh requests/user/month, plus saves and other requests. Background tabs skip this polling.

## Revisit after the first month

Record actual database size/CPU/connections, source and ZIP bytes/user, soft-deleted bytes, backup duration/peak memory, Cloud Run billed time, and internet delivery. Update these estimates from those measurements before expanding the audience. Keep production and preproduction budgets separate; an additional always-running managed database adds another substantial fixed charge.
