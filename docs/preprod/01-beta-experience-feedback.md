# Feature Specification — Beta Experience & Feedback

**Feature ID:** SH-BETA-001  
**Status:** Implemented; see [decisions, dependency mapping, and validation](01-IMPLEMENTATION-DECISIONS.md)  
**Depends on:** SH-BETA-002 error contract; SH-BETA-003 administration and release metadata; SH-BETA-004 attachment authorization

## Purpose

Give authenticated beta users a small amount of guidance, unambiguous save/error state, and a low-friction way to report actionable feedback.

## In scope

- Account-associated general feedback
- Bug, feature request, confusing experience, and other categories
- Optional screenshot attachment
- Pattern-specific problem reporting with optional source-pattern attachment
- Automatically captured non-content diagnostics
- Four-step, dismissible first-run tour
- User-friendly error messages with codes and reference IDs
- Saving, saved, and failed-save state
- Empty states
- Visible beta/app version
- Administrator-controlled announcement banner
- Site-hosted What's New page populated from release entries

## Out of scope

- Anonymous feedback
- User-visible feedback history
- Email notifications
- Public voting, comments, or discussion
- Full customer-support workflow or SLAs
- Automatic PDF, chart, screenshot, or pattern-content collection
- More than one simultaneously displayed announcement

## User stories

- As a beta user, I can report a problem without leaving the page where it happened.
- As a stitcher, I can explicitly share a screenshot or pattern when I believe it is necessary for diagnosis.
- As a user, I can tell whether my work is saving and receive clear guidance if it is not.
- As a new user, I can understand the four core actions without completing a long wizard.
- As a beta participant, I can see current announcements and review published changes.

## Functional requirements

### FR-001 — Authenticated feedback

Every submission is associated with the authenticated internal application user. Anonymous submission is not supported.

Feedback categories are:

- `Bug`
- `FeatureRequest`
- `ConfusingExperience`
- `Other`

Message text is required and must have configurable length limits.

### FR-002 — Feedback context

The server always records account identity, submission time, app version, and authorized report associations. An **Include technical diagnostics** choice controls additional browser/screen and bounded import/error context; users can submit without it. Explain the included fields before submission. The available context is:

- user ID;
- current route;
- deployed app/build version;
- timestamp;
- browser/user-agent and screen dimensions;
- project ID and import-run ID when applicable;
- recent error code and correlation/reference ID when applicable;
- parser/import lifecycle metadata relevant to the current project.

The client must not be trusted to provide or authorize another user's identifiers.

Store `DiagnosticsIncluded` and the disclosure version. Route capture uses a route name/template without query strings or fragments. Error associations are authorized metadata, not permission to fetch arbitrary logs. Minimal server operational logging remains separate from optional feedback diagnostics.

### FR-003 — Explicit attachment consent

Screenshots and source patterns are never attached automatically.

The form must present separate, unchecked choices. For a pattern:

> Attach my pattern to this report so the administrator can inspect it.

When a report concerns an existing stored pattern, reference the authorized existing source asset rather than duplicating its bytes. The consent event is stored on the feedback record. A screenshot is stored as a new private attachment.

### FR-004 — Pattern-specific reporting

The pattern viewer exposes **Report a problem with this pattern**. It pre-associates the current user-owned project, active pattern revision, latest relevant import run, parser version, and recent error reference where available.

### FR-005 — Feedback status

Administrative queue states are:

- `Unread`
- `Read`
- `Completed`
- `Archived`

Submission defaults to `Unread`. Status changes do not notify the submitting user. `Completed` exists so the administrator can track addressed tickets. Archiving is organizational, not deletion.

Completion must be an explicit admin action; opening a report does not complete it. Store `CompletedAt` and `CompletedByAdminUserId` when completed. Preserve this completion metadata if the report is later archived so addressed tickets remain identifiable. Provide a Completed filter/view and allow archived completed reports to be included. Reopening to Read or Unread clears current completion metadata.

### FR-006 — Guided onboarding

The first-run tour contains no more than four steps:

1. Upload a pattern.
2. Click symbols or stitches to inspect and highlight them.
3. Mark stitches complete and follow project progress.
4. Use Send Feedback when behavior is unclear or incorrect.

The tour is dismissible. Completion or dismissal is stored as `CompletedOnboardingVersion`, allowing a materially changed future tour without repeatedly showing the same one.

### FR-007 — Save-state contract

The UI supports at least:

- `Saving…`
- `Saved` with a recent timestamp;
- `Unable to save` or `Disconnected` with the action the user must take.

`Saved` may appear only after server persistence is confirmed. Detailed mutation behavior is defined by SH-BETA-002.

### FR-008 — Error presentation

Significant failures show:

- a plain-language description;
- an appropriate next step or retry action;
- a stable error code/category;
- a unique occurrence/reference ID.

Do not expose stack traces, database details, secrets, or source document content.

### FR-009 — Announcement banner

The app displays at most the most recently published active announcement whose start/end window includes the current time.

An announcement may contain title, short message, optional link text/URL, start/end times, active/published state, and dismissibility. Dismissal is stored per user and per announcement; it must not suppress future announcements.

External announcement links use safe-link behavior. Internal links may point to the What's New page.

### FR-010 — What's New

Provide an authenticated, beta-authorized What's New page populated from published structured release entries. Public Privacy/Terms pages remain separately accessible. Published structured release entries contain:

```text
Version
ReleaseDate
Title
BodyMarkdown
PublishedAt
```

Markdown must be sanitized before rendering. Draft releases are not visible to beta users.

### FR-011 — Empty and boundary states

Provide deliberate UX for:

- no projects;
- no imports;
- no feedback results in admin;
- upload and parse in progress;
- unsupported or failed upload;
- expired session;
- access denied/beta access required;
- not found;
- unexpected application failure.

Each state should explain what happened and provide the safest useful next action.

### FR-012 — Version visibility

The deployed semantic version or build identifier is visible in the footer or account/help menu and is included in feedback diagnostics and error logs.

### FR-013 — Feedback outside a usable pattern

Expose reporting from failed-import details and global error UI, even when no active pattern revision exists. Use the retained authorized source and import run when available. Explain when a source was rejected or removed and cannot be attached. A client-only failure can have a client occurrence ID; do not imply that ID identifies a server log if the request never arrived.

The error fallback must work if the main UI component fails. Allow copying safe error details. If feedback submission is unavailable, show the configured support contact without automatically sending anything.

### FR-014 — Attachment and submission lifecycle

Support PNG/JPEG screenshots with configured count, byte-size, and decoded-pixel limits; safe decoding and metadata removal belong to SH-BETA-004. Show selected files before submission, permit removal, and never automatically capture the screen. Existing-pattern consent binds the exact source asset, not whichever revision later becomes active.

Use a stable submission idempotency key: retrying after a lost response returns the same submission. Finalize attachments and text together, or retain a retryable draft; temporary uploads have bounded cleanup and cannot become orphaned permanent files. Full quota must still allow text-only feedback. After confirmed submission, show a receipt and clear the draft.

### FR-015 — Keyboard and navigation behavior

Feedback and destructive confirmations support keyboard navigation, visible focus, labeled controls, and focus restoration on close. Save/error states use text as well as color. Tour steps wait for their target to exist; a new account without a pattern is not forced into an unusable viewer step. Dismissal does not block the underlying task.

Warn before user-initiated navigation discards pending work or a feedback draft. Browser unload warnings are best effort; document actual refresh/crash recovery limits of the selected save strategy instead of promising offline durability.

## Domain model

```text
FeedbackSubmission
  Id
  UserId
  Category
  Message
  Status
  SubmissionKey
  DiagnosticsIncluded
  DiagnosticsDisclosureVersion
  CompletedAt?
  CompletedByAdminUserId?
  Route
  AppVersion
  BrowserMetadata
  ScreenMetadata
  ProjectId?
  PatternRevisionId?
  ImportRunId?
  ErrorCode?
  CorrelationId?
  PatternAttachmentConsentAt?
  CreatedAt
  UpdatedAt

FeedbackAttachment
  Id
  FeedbackSubmissionId
  Kind (Screenshot | ExistingPatternReference)
  AssetId
  CreatedAt

UserOnboardingState
  UserId
  CompletedOnboardingVersion
  CompletedOrDismissedAt

AnnouncementDismissal
  AnnouncementId
  UserId
  DismissedAt
```

## Security and privacy

- Validate ownership when associating a project, import, error, or existing pattern asset.
- Feedback attachments are private and administrator-only beyond the submitter's normal project access.
- Screenshots count toward the user's quota; an existing pattern reference does not count twice.
- Strip or ignore client-supplied file paths and attachment identifiers not authorized for the user.
- Do not place message or attachment contents in general logs.
- Store the affirmative consent timestamp when a pattern is attached.

## Failure scenarios

- If feedback submission fails, preserve entered text in the current browser session and show a retryable error.
- If an attachment fails but text submission can succeed, require the user to choose between retrying the attachment or submitting without it; do not silently omit a selected attachment.
- If a referenced project was deleted, retain the textual feedback and diagnostic snapshot but remove/invalidate the asset association according to deletion rules.
- If save state cannot be established, show the failure state and follow SH-BETA-002 mutation restrictions.
- Invalid or unsafe release-note Markdown is rejected or sanitized before publication.

## Acceptance criteria

- [x] Unauthenticated users cannot submit feedback.
- [x] Every submission is linked to the authenticated internal user.
- [x] A general report records route, version, timestamp, and available correlation data.
- [x] No screenshot or pattern is included unless the user explicitly opts in.
- [x] A pattern report cannot reference another user's project or asset.
- [x] Existing pattern bytes are not duplicated solely for feedback.
- [x] Admin can move reports among Unread, Read, Completed, and Archived.
- [x] Completing a report records when/by whom; archiving preserves that information and the report remains findable as completed.
- [x] Users do not see feedback status/history during beta.
- [x] The tour is at most four steps and does not reappear after completion/dismissal for the same version.
- [x] The UI never reports an unconfirmed mutation as Saved.
- [x] Significant errors display both a stable code and unique reference ID.
- [x] At most one eligible active announcement is displayed.
- [x] Dismissing one announcement does not dismiss later announcements.
- [x] Published release entries render on What's New; drafts do not.
- [x] App/build version is visible and captured with feedback.
- [x] Declining technical diagnostics still permits feedback; query tokens and URL fragments are never captured.
- [x] A user can report an import failure without a usable pattern, and retrying a timed-out submission does not duplicate it.
- [x] Full quota permits text-only feedback; rejected or abandoned screenshots do not leak storage.
- [x] Keyboard-only users can submit feedback, dismiss the tour, and operate confirmations with correct focus behavior.

## Deferred work

- Email notifications
- User-facing ticket history and replies
- Multiple simultaneous or prioritized banners
- Rich CMS/media management for release notes
- Automated screenshot capture

## Open decisions

None required for beta implementation.
