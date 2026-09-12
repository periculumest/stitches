# Spec 06 — DMC Catalog and Thread Inventory

**Status:** Ready for implementation  
**Priority:** P1

---

## In Scope

- Repository-managed DMC catalog JSON.
- DMC search/browse.
- Standard stranded cotton support.
- Extensible family model.
- Bobbin counts.
- Simple storage location.
- Pattern/project material requirements.
- Owned vs missing comparison.

---

## Out of Scope

- Fractional bobbins.
- Shopping integrations.
- Barcode scanning.
- Additional thread brands.
- Automatic consumption estimation.
- Cloud inventory sync.

---

## Functional Requirements

### FR-01 — Catalog asset

The repository must include a versioned DMC thread catalog JSON.

### FR-02 — Catalog schema

Each catalog entry should include at least:

- brand,
- family,
- code,
- name,
- approximate display color.

### FR-03 — Additional families

The schema must support DMC families beyond standard stranded cotton, including Variations.

### FR-04 — Inventory entry

The user may record:

- thread,
- bobbin count,
- location.

### FR-05 — Quantity

Bobbin count is an integer.

Example:

```text
DMC 310
3 bobbins
Box 1 / Row 3
```

### FR-06 — Inventory search

The user must be able to quickly find a DMC code or name.

### FR-07 — Project requirements

For a project, display:

- required colors,
- owned colors,
- missing colors.

### FR-08 — Requirement details

For each required thread show:

- DMC/thread identity,
- symbol,
- approximate color,
- owned status,
- bobbin count,
- storage location.

### FR-09 — Effective thread

Project material calculations must use the project's effective thread after substitutions.

---

## Acceptance Criteria

- [ ] DMC JSON is loaded and validated successfully.
- [ ] User can search by DMC number.
- [ ] User can search by thread name.
- [ ] User can add/update/remove inventory entries.
- [ ] Inventory stores whole bobbin counts.
- [ ] Inventory stores simple location strings.
- [ ] A project displays total required thread/color count.
- [ ] A project displays owned count.
- [ ] A project displays missing count.
- [ ] Required-thread detail displays inventory location.
- [ ] Material calculations can be recomputed when project thread mappings change.
