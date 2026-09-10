# Spec 02 — PDF Pattern Import

**Status:** Ready for implementation  
**Priority:** P0

---

## In Scope

- Upload commercial PDF cross-stitch patterns.
- Retain original PDF.
- Extract/render PDF pages for analysis.
- Detect candidate grid structure.
- Detect candidate pattern key.
- Detect symbols where possible.
- Detect thread codes/names where possible.
- Map pages into one continuous coordinate system.
- Preserve source page boundaries.
- Produce a normalized candidate pattern.
- Produce confidence/warning metadata.

---

## Out of Scope

- Guaranteeing perfect parsing.
- Scanned/image-only PDF compatibility.
- Image-to-pattern conversion.
- Final correction UI.
- Stitch completion.
- Inventory.

---

## Functional Requirements

### FR-01 — PDF upload

The user must be able to select and upload a PDF.

### FR-02 — Source retention

The original PDF must be stored server-side and associated with the imported pattern.

### FR-03 — Page analysis

The importer must analyze all relevant pages and identify likely pattern-grid pages and key/legend content.

### FR-04 — Normalized coordinates

Multi-page grid content must be merged into one continuous logical coordinate system.

### FR-05 — Page metadata

Each source page must retain enough metadata to show optional page boundaries later.

### FR-06 — Stitch definitions

The importer must produce candidate stitch definitions containing, where detectable:

- symbol,
- thread identity,
- stitch type.

### FR-07 — Symbols

Source symbols should be preserved where possible.

If a usable source symbol is unavailable, automatically assign an unused display symbol and record the replacement in the import notes. Reserve existing symbols across the whole key, and use the same replacement for every stitch with that definition. Preserve its thread mapping and strand counts. An unavailable symbol shape must not block import; an unknown thread mapping still requires review.

### FR-08 — Stitch types

The normalized model must support:

- full cross,
- half cross,
- quarter cross,
- three-quarter cross,
- backstitch,
- French knot,
- bead/embellishment,
- extensibility for others.

The initial parser may recognize only a subset automatically, but it must not discard unsupported types silently.

### FR-09 — Import confidence

The parser must emit warnings/confidence metadata for suspicious detections.

---

## Likely Import Warnings

- Grid alignment drift.
- Inconsistent row/column dimensions.
- Symbol in grid absent from key.
- Key symbol never used.
- Duplicate symbol mappings.
- Unrecognized DMC code.
- Page overlap uncertainty.
- Unexpected page geometry.
- Low-confidence OCR/text extraction.
- Stitch type ambiguity.

---

## Acceptance Criteria

- [ ] User can upload a PDF.
- [ ] Original PDF is persisted server-side.
- [ ] Each PDF page can be rendered/extracted for analysis.
- [ ] A multi-page pattern can produce one continuous logical grid.
- [ ] Source page boundaries are retained as metadata.
- [ ] Candidate symbols are represented in the normalized model.
- [ ] Candidate thread mappings are represented in the normalized model.
- [ ] Stitch type exists as a first-class field.
- [ ] Parser produces an import-warning collection.
- [ ] Import failure does not destroy the uploaded source.
- [ ] Import results are persisted and can be reopened for audit.
