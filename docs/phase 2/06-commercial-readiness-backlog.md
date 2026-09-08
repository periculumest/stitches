# Commercial Readiness Backlog — After SH-IDENTITY-002

**Status:** Backlog

The identity/persistence phase now includes user-downloadable backups/exports. The remaining commercialization work is preserved here.

---

# P0 — Before Public Commercial Launch

## Account lifecycle

- self-service account deletion
- deletion/retention semantics
- user profile/account-management surface
- potentially self-service restore/import of Stitch Helper backup archives

## Infrastructure disaster recovery

Separate from user backups and from the hosting-ready architecture delivered in SH-IDENTITY-002:

- managed PostgreSQL infrastructure backup policy
- object-storage provider backup/versioning policy
- full restore rehearsal
- monitored backup failures
- documented recovery objectives

## Security/operations hardening

Baseline HTTPS, external durable storage, secret-driven configuration, and hosted deployability are part of SH-IDENTITY-002.

Before commercial launch additionally complete:

- production monitoring/error tracking
- rate limiting/abuse controls
- security update process
- authorization regression suite in CI
- operational alerting
- recovery/runbook documentation

## Privacy/legal

- privacy policy
- terms of service
- user explanation of uploaded pattern storage/processing
- retention/deletion policy
- legal review of commercial-pattern upload/storage behavior

## Import robustness

Maintain representative import corpus covering:

- different publishers
- color/black-and-white charts
- legends/layouts
- multi-page patterns
- unusual symbols/fonts
- fractional stitches
- backstitch
- French knots
- blends
- multiple floss brands
- overlap/registration markers
- large charts
- scans/low-quality PDFs
- unusual numbering

## Import confidence/review UX

Promote manual correction into a first-class workflow such as:

```text
Import complete
✓ Grid alignment validated
✓ Legend detected
⚠ 3 mappings need review

[Review Import]
```

---

# P1 — Around Launch

## Entitlements abstraction

Future:

```text
Account
Subscription
Entitlements
```

Avoid scattered `IsPremium` conditionals.

## Product/storage metrics

Measure:

- source asset bytes/user
- backup bytes/user
- pattern/project counts
- import processing time
- progress record volumes

## Accessibility

Audit:

- keyboard support
- grid navigation
- symbol legibility
- zoom
- high contrast
- color-vision support
- non-color-only states
- focus indicators

## Soft delete/recovery

Recommended especially for high-progress projects.

---

# P2 — Future Expansion

- native Android
- offline-first sync
- SignalR/live sync if product value justifies it
- alternate identity providers
- optional guest/trial
- subscription billing
- self-service backup restore/import
- additional thread brands/catalogs
- richer diagnostics/support tooling

---

# Explicit Non-Goals Unless Reopened

- user-to-user sharing of purchased/commercial patterns
- public pattern URLs
- collaborative shared projects
- global community pattern repository populated from private user uploads
