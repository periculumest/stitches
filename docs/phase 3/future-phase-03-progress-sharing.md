# Future Phase 03 — Progress Sharing

**Status:** Feature request captured / discovery deferred  
**Phase:** 3  
**MVP Impact:** None

---

## Feature Intent

Allow the user to share progress on a cross-stitch project through familiar sharing mechanisms commonly available on websites and devices.

The purpose is primarily celebratory: after making meaningful progress, the user should be able to quickly show another person how the project is coming along.

---

## Candidate Share Options

Phase 3 discovery should evaluate:

- Generated image suitable for saving or posting.
- Hosted/shareable project-progress link.
- Facebook sharing.
- Email sharing.
- Text/SMS sharing.
- Native browser or operating-system share sheet.
- Copy-to-clipboard for image, text, or link.

These are candidate capabilities, not committed implementation requirements.

---

## Candidate Shared Information

A share artifact may contain:

- Project name.
- Project thumbnail or progress visualization.
- Completion percentage.
- Completed stitch count.
- Remaining stitch count.
- Recently achieved milestone.
- Optional user-entered message.

Exact visual design will be defined later.

---

## Privacy and Copyright Guardrail

Imported commercial patterns may be copyrighted.

Progress sharing must not unintentionally publish enough of the pattern to function as a substitute for the purchased pattern.

Default share artifacts should therefore favor:

- completion summaries,
- stylized or limited progress previews,
- user-owned project imagery,
- metadata and milestone information,

rather than a full-resolution export of the source pattern.

All sharing must be explicitly initiated by the user.

---

## Architectural Considerations to Preserve During Earlier Phases

Earlier phases should not implement the sharing feature, but they should preserve:

1. A clear API/domain query for current project progress.
2. Stable project identifiers.
3. The ability to render a project-progress snapshot independently of the interactive viewport.
4. Separation between source-pattern assets and safe-to-share generated assets.
5. A future abstraction for share targets rather than embedding Facebook/email-specific behavior into the core domain.

No hosted sharing service, social SDK, public project URL, or external account integration is required before Phase 3.

---

## Deferred Questions

To be answered during Phase 3 discovery:

- Should shared links be public, unlisted, authenticated, or expiring?
- Will Stitch Helper have a hosted component by that phase?
- What exactly should the generated progress image look like?
- Should the user be able to hide project names or stitch counts?
- Should milestone celebrations provide a one-click Share action?
- Should users be able to attach their own photo of the physical work?
- Which share targets are worth dedicated integrations versus native browser/OS sharing?
- What copyright-safe portion of the rendered pattern may appear in a share artifact?
- Should shared links update as progress changes or represent a frozen snapshot?

---

## Acceptance Criteria for This Feature Request

This document is intentionally not an implementation-ready Phase 3 spec.

The request is considered successfully captured when:

- [x] Progress sharing is present in the long-term product roadmap.
- [x] It is explicitly identified as Phase 3.
- [x] Common share channels are recorded as candidates.
- [x] Privacy/copyright concerns are recorded.
- [x] MVP scope is unchanged.
- [x] Earlier architecture is advised not to block future snapshot generation.
