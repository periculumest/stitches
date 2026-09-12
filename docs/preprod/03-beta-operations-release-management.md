# Feature Specification — Beta Operations & Release Management

**Feature ID:** SH-BETA-003  
**Status:** Ready for implementation  
**Depends on:** Existing Google identity; SH-BETA-002 correlation/import model; SH-BETA-004 audit and authorization rules

## Purpose

Provide the minimum administrative surface needed to admit users, investigate problems, communicate changes, and control beta capabilities without routine SQL or server access.

## In scope

- Single explicit administrator role/user
- Email allow-list admission
- Friendly access-required page and access-request queue
- Reversible account suspension
- Metadata-first user, project, import, feedback, and system views
- Feedback state management
- Announcement and release-note management
- Global feature flags
- Visible build/release metadata
- Audit events for sensitive admin actions
- Current health display

## Out of scope

- Multiple sophisticated admin roles or delegated permissions
- Per-user feature flag overrides
- Email or pager alerts
- Internal admin notes
- Full support-ticket workflow
- Opening user pattern content by default
- Impersonation or logging in as a user
- Editing user project/progress data from admin

## User stories

- As the operator, I can control who may enter the beta.
- As a prospective tester, I can request access after a successful Google sign-in is found ineligible.
- As the operator, I can suspend and reinstate an account without deleting data.
- As the operator, I can correlate a report to operational metadata without opening private pattern content.
- As the operator, I can publish one current banner and maintain a What's New history.
- As the operator, I can enable or disable a beta capability globally.

## Functional requirements

### FR-001 — Administrator authorization

Use an explicit application-level administrator role/claim derived from server-side configuration or persisted role membership. Being the first user, matching a client-provided email, or knowing an admin URL is insufficient.

One active administrator is sufficient for beta, but the authorization mechanism must not require redesign to add another later.

### FR-002 — Email allow-list

Beta admission is based on normalized email address at first/returning Google authentication, while domain ownership continues to use the internal user GUID.

Allow-list entries support:

- normalized email;
- status (`Allowed` or `Revoked`);
- created/updated timestamps;
- administrator audit actor.

Revoking an allow-list entry prevents future application access but does not itself delete data. Suspension is modeled separately.

### FR-003 — Access-required experience

A successfully authenticated Google user who is not allowed sees a friendly **Beta access required** page. The page does not create unrestricted product access.

It offers an access-request form with a short optional message. The authenticated Google email is used; the user cannot request on behalf of another address in the same flow.

### FR-004 — Access requests

The administrator can view pending requests and approve or decline them. Approval creates/activates the allow-list entry atomically. Duplicate requests from the same identity are consolidated or clearly grouped.

Request state may be minimal: `Pending`, `Approved`, `Declined`.

### FR-005 — Suspension

Suspension is independent from allow-list membership.

Suspending a user:

- prevents application access immediately, including existing sessions at their next authorized request;
- retains all user data and private assets;
- records who acted, when, and an optional reason;
- is reversible;
- does not remove the allow-list entry.

Reinstatement restores eligibility subject to allow-list and account-deletion state.

### FR-006 — Metadata-first administration

Admin views include:

**Users:** email/name, registration date, last active, account/access status, project count, storage usage.  
**Projects:** owner, name, created/updated dates, active revision, import status, stitch count/progress summary.  
**Imports:** status, parser version, stage, timings, warnings/failure codes, correlation ID.  
**Feedback:** category, status, user, timestamps, linked metadata, attachment presence.  
**System:** app/build version, database/storage reachability, import worker state, scheduled-backup configuration and latest result when enabled.

Default views must not render PDF pages, extracted charts, screenshots, or pattern cells.

### FR-007 — Feedback queue

The administrator can filter and transition submissions among:

- `Unread`
- `Read`
- `Completed`
- `Archived`

The admin may open explicitly attached screenshots/patterns only from the feedback detail view. Access is audited. Deleting a feedback report/attachment is not required for beta beyond retention mechanisms defined elsewhere.

Provide an explicit **Mark completed** action and a Completed view/filter. Follow SH-BETA-001 FR-005 for completion timestamp/actor, reopening, and preservation of completion metadata after archive. Archived completed tickets must remain findable by including archived results; archive is not deletion and must not erase completion tracking.

### FR-008 — Announcements

Admin can create, edit, preview, publish/unpublish, schedule, and deactivate announcements with:

```text
Title
Message
OptionalLinkText
OptionalLinkUrl
StartsAt?
EndsAt?
IsPublished
IsDismissible
CreatedAt
UpdatedAt
```

Only the most recently published eligible announcement displays to users. Admin preview must not publish.

### FR-009 — Release notes

Admin can create, edit, preview, publish/unpublish release entries with version, release date, title, and sanitized Markdown body. Published entries populate the site's What's New page in reverse chronological order.

### FR-010 — Global feature flags

Feature flags are global on/off values. Each has a stable key, description, enabled state, and updated timestamp/actor.

Unknown flags fail closed. Security and authorization controls may not be disabled through a feature flag. The UI and server must both enforce disabled product capabilities where an endpoint could otherwise be invoked directly.

### FR-011 — Build/release metadata

Expose deployed version/build and deployment timestamp/commit identifier where available. User-safe version appears in the application; richer metadata may appear in admin.

### FR-012 — Audit events

Record at least:

- allow-list approval/revocation;
- access-request decision;
- suspension/reinstatement;
- feedback attachment access;
- announcement/release publication;
- feature-flag change;
- account restore and permanent purge actions.

Audit records identify actor, action, target type/ID, timestamp, result, and correlation ID. They must not contain private document contents or secrets.

### FR-013 — Current health

Admin displays current state only; proactive alerts are deferred. Health data must be safe, bounded, and permission protected. Public health endpoints expose only what the hosting platform needs.

### FR-014 — Identity and eligibility precedence

Resolve the existing account by verified provider issuer/subject mapped to the internal user ID. Email allow-list membership controls admission; it must not be used to merge accounts or bypass a pending deletion. Require a verified provider email for allow-list evaluation. Do not invent Gmail dot/plus alias equivalence or auto-link different provider subjects by matching email alone.

Apply eligibility on every protected request, not only the OAuth callback, and again before background jobs publish output. Use the following precedence; passing a later check cannot bypass an earlier restriction.

| State | Permitted experience | Product data access |
| --- | --- | --- |
| No verified authentication | Sign-in, public policies, configured support contact | None |
| Purging/Purged | Safe status/support route; never restoration of partially purged data | None |
| Pending deletion | Deletion status/deadline, limited restoration request, logout/policies | None |
| Suspended | Suspension notice and configured support contact; own account deletion route | None |
| Not allow-listed | Access-required page and access request; existing users retain own account deletion route | None |
| Policy acceptance pending | Read/accept policies, logout, own account deletion | No normal product use |
| Eligible | Normal beta application | Owner-scoped only |

Admin role does not implicitly bypass a deleted/suspended identity. Administrative operations on other accounts require distinct authorization. Reject existing download requests after ineligibility; do not rely solely on previously issued bearer URLs. If a fully purged user later registers under an allowed identity, create a fresh empty account under current admission policy, never restore erased data.

### FR-015 — Restoration support and administrator continuity

Provide a limited, authenticated restoration request on the pending-deletion page, bound to the original provider identity. Store only request ID, account reference, requested time, state and decision actor/time; reuse the existing administrative request infrastructure where practical. It does not grant normal product access or create user-visible ticket history. Admin approves only before the deadline and before purge starts; no self-service restoration or email notification is added.

For users unable to authenticate as the original identity, display the operator's configured support contact. A typed email or knowledge of a project name is not enough to approve restoration. Document manual verification/escalation before using it.

Provision the sole administrator through controlled deployment configuration/seed data and rehearse recovery of administrator access. Avoid allowing an ordinary admin UI action to suspend/delete/revoke the sole administrator without a separately documented recovery route. Log administrative recovery; never add a public bootstrap endpoint.

### FR-016 — Deterministic release and flag behavior

Announcement adds `PublishedAt`. Use server UTC with StartsAt inclusive and EndsAt exclusive; missing bounds are unbounded. Select the latest eligible published announcement by PublishedAt with ID as deterministic tie-breaker, then apply the user's dismissal. Do not reveal an older banner solely because the newest was dismissed. Editing a published announcement retains its identity/dismissal; a new message intended for everyone gets a new announcement ID. Validate end after start.

Use `IsPublished` consistently for publication eligibility and `PublishedAt` for ordering/history on both announcements and releases; unpublishing revokes visibility even when history remains. What's New is beta-authorized for this release. Sanitize preview and public rendering identically; reject executable URL schemes. Avoid remotely loaded tracking images by allowing only approved site-owned image assets.

Flag changes have a declared maximum cache propagation interval, visible to the operator. At request/job start use the current effective flag, and recheck before publishing output where required. Disabling a feature cannot delete stored data, block export/deletion, or retroactively corrupt an already committed operation. Separate versioned code/schema changes from flag configuration.

### FR-017 — Health freshness and release recovery

Show last observed time, last successful job time, current heartbeat/queue age, overdue purge/cleanup count and state. Missing/stale evidence is Unknown or Stale, never Healthy. Distinguish disabled scheduled backups from failed backups. These are bounded operational checks, not the deferred general data-integrity scanner. Keep health available to the admin when an optional dependency fails.

Before beta, record how to deploy schema changes once, restart workers safely, and roll back compatible application code or roll forward after incompatible schema changes. Rehearse against representative data; a global feature flag is not a database rollback. Keep a documented operator check cadence during beta because proactive alerting is deferred.

## Domain model

```text
BetaAllowListEntry
  Id
  NormalizedEmail
  Status
  CreatedAt
  UpdatedAt

BetaAccessRequest
  Id
  ProviderIdentityKey/UserId?
  NormalizedEmail
  Message?
  Status
  RequestedAt
  DecidedAt?
  DecidedByAdminUserId?

UserAccessState
  UserId
  IsSuspended
  SuspendedAt?
  SuspendedByAdminUserId?
  SuspensionReason?

FeatureFlag
  Key
  Description
  IsEnabled
  UpdatedAt
  UpdatedByAdminUserId

Announcement
  Id
  Title
  Message
  LinkText?
  LinkUrl?
  StartsAt?
  EndsAt?
  IsPublished
  PublishedAt?
  IsDismissible
  CreatedAt
  UpdatedAt

ReleaseEntry
  Id
  Version
  ReleaseDate
  Title
  BodyMarkdown
  IsPublished
  PublishedAt?

AuditEvent
  Id
  ActorUserId
  Action
  TargetType
  TargetId
  Result
  CorrelationId
  OccurredAt
```

## Security requirements

- Every admin route and API operation performs server-side administrator authorization.
- Admin list endpoints use pagination, bounded filters, and safe output models.
- Sensitive attachment access requires a distinct authorized request and produces an audit event.
- There is no user impersonation capability.
- Feature flags do not bypass authentication, ownership, consent, limits, or retention rules.
- Avoid exposing whether arbitrary non-beta email addresses already have accounts.

## Failure scenarios

- Allow-list approval fails: request remains pending and no partial entry is created.
- A suspended user has an existing session: the next protected request is denied; session caching cannot postpone enforcement indefinitely.
- A flag service/store is unavailable: unknown/unavailable flag evaluates disabled unless the capability is a required baseline feature.
- Announcement dates are invalid: reject publish.
- Release Markdown is unsafe: reject or sanitize before publication.
- Health dependency is unavailable: show degraded state with a reference ID; do not expose credentials or raw exception text.

## Acceptance criteria

- [ ] A non-admin cannot reach admin UI or invoke admin APIs.
- [ ] An allow-listed Google identity can enter the app.
- [ ] A non-allowed identity sees the access-required page and can submit one traceable request.
- [ ] Admin approval enables access without manual database work.
- [ ] Suspended users are denied while their data remains present.
- [ ] Reinstatement restores access when other eligibility rules pass.
- [ ] Revoking allow-list access does not delete user data.
- [ ] Admin can inspect users, projects, imports, feedback, and system health through metadata-first views.
- [ ] Pattern content is not rendered in default admin views.
- [ ] Feedback supports Unread, Read, Completed, and Archived.
- [ ] Completed feedback remains trackable after archiving; opening a report alone never marks it completed.
- [ ] Admin can publish a dismissible announcement linked to What's New.
- [ ] Admin can publish sanitized release notes.
- [ ] Global feature flags are enforced by both applicable UI and server paths.
- [ ] Every defined sensitive admin action creates an audit event.
- [ ] Account-state matrix tests cover direct APIs, returning OAuth callbacks, download requests and worker publication.
- [ ] Pending-deletion sign-in cannot create a replacement account; a same-identity restoration request works without product access.
- [ ] A restore at/after the purge deadline or after purge starts is denied, and administrator-access recovery is documented.
- [ ] Announcement dismissal, edits, unpublish and UTC boundaries produce deterministic results.
- [ ] A stale/absent worker heartbeat is not shown as Healthy; flag propagation and release/schema recovery are exercised.

## Deferred work

- Per-user/cohort flags
- Multiple admin permission tiers
- Operational email/pager alerts
- Admin notes and ticket replies
- Impersonation
- User-data editing from admin

## Open decisions

None required for beta implementation.
