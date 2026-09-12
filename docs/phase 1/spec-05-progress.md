# Spec 05 — Progress Experience

**Status:** Ready for implementation  
**Priority:** P0

---

## In Scope

- Overall completion.
- Completed/remaining counts.
- Percentage.
- Progress by thread.
- Progress by stitch definition.
- Progress by stitch type.
- Project-library summaries.
- Milestone celebrations.
- Non-disruptive progress feedback.

---

## Out of Scope

- Social sharing.
- Gamification systems.
- Streaks.
- Historical charts/advanced analytics.
- Cloud leaderboards.

---

## Product Principle

Progress is a celebration.

The user may spend months on a project. The application should continuously show that the work is moving forward.

---

## Functional Requirements

### FR-01 — Persistent progress display

Overall project progress should be visible from the main stitching workspace.

Example:

```text
12,482 / 32,000 stitches
39.0% complete
```

### FR-02 — Remaining count

The application must show remaining stitches.

### FR-03 — Thread progress

The user must be able to see completion for a selected thread/stitch definition.

### FR-04 — Stitch-type progress

The user should be able to see completion grouped by stitch type.

### FR-05 — Library progress

Each project card should display meaningful progress.

### FR-06 — Milestones

Trigger a small celebration when crossing:

- 10%
- 25%
- 50%
- 75%
- 90%
- 100%

### FR-07 — Non-disruptive behavior

Milestone feedback must not block the stitching workflow.

### FR-08 — Idempotence

A milestone should not repeatedly celebrate simply because the project is reopened.

If progress is undone below a threshold and later crosses it again, behavior should be intentional and documented.

---

## Acceptance Criteria

- [ ] Workspace always displays current project completion.
- [ ] Completion updates immediately after marking stitches.
- [ ] Remaining count is correct.
- [ ] Progress can be viewed by thread/stitch definition.
- [ ] Progress can be viewed by stitch type.
- [ ] Project library displays completion percentage.
- [ ] Milestone feedback exists for all defined milestone percentages.
- [ ] Milestone feedback does not require dismissal to keep stitching.
- [ ] Persisted milestone state prevents accidental repeated celebration on reload.
- [ ] Undoing completion correctly recalculates progress.
