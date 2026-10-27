# PDF Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename Firmas PDF to PDF Editor and add hybrid PDF content editing (native when possible, tap/overlay otherwise), plus insert text/images, while keeping visual and digital signatures.

**Architecture:** Keep WPF + `Windows.Data.Pdf` for viewing and PDFsharp for export/signing. Add PdfPig to index text/images. Introduce a unified edit-object model and export pipeline: native mutations → tap/new overlays → signatures → optional CMS sign. Always save a copy.

**Tech Stack:** .NET 10 WPF, PDFsharp-WPF 6.2.4, PdfPig (MIT), Windows.Data.Pdf, existing MVVM patterns.

## Global Constraints

- Product display name: `PDF Editor`
- Project / namespace / assembly / exe: `PdfEditor`
- AppData: `%LocalAppData%\PdfEditor\Signatures\` (migrate from `FirmasPDF` if present and new folder empty)
- UI language: Spanish
- Save: always a new file; never overwrite the open path
- Digitally signed input PDFs: read-only
- No OCR in v1
- Dual text mode: native when reliable, else tap; on native export failure retry as tap and report status
- Keep visual signature library + digital CMS/PKCS#7 SHA-256 signing
- Geometry must respect page rotation, crop box, and media origin via existing `PdfGeometry`

**Spec:** `docs/superpowers/specs/2026-10-26-pdf-editor-design.md`

---

### Task 1: Full rename to PdfEditor
Rename folders/projects/namespaces/docs/AppData; add SignatureStore migration; preserve maximized window. Verify build+checks on Windows.

### Task 2: Edit object model + unified undo snapshots
Add `EditorTool`, `PageEditObject`, `EditObjectState`, `DocumentEditSnapshot`; wire `EditObjects` collections; snapshot round-trip check.

### Task 3: PdfContentIndex (PdfPig)
Add PdfPig; build text/image hits with normalized bounds; hook `PdfSession`; THIRD-PARTY-NOTICES; indexing check.

### Task 4: UI shell — branding, tabs, tools
PDF Editor branding; Editar/Firmas tabs; tools Select/Text/Image/Signature.

### Task 5: Insert new text and images + canvas
`EditObjectControl`; insert tools; unified undo via `DocumentEditSnapshot`; signatures as edit objects.

### Task 6: Select/edit existing content
Materialize indexed hits as overlays; inline edit; delete/replace; status counts.

### Task 7: Export — tap overlays + new objects + signatures
`PdfEditEngine` + extend `PdfExporter` for edit ops; save when any edits exist.

### Task 8: Native text/image path + tap fallback
Best-effort native; catch → tap; `ExportResult.UsedTapFallback` + Spanish status.

### Task 9: Properties bar, errors, polish
Properties strip; unsaved/read-only messages from spec.

### Task 10: Expand PdfEditor.Checks + publish smoke
Migration, text/image export, native/tap, mixed undo, digital sign after edits; publish profile.
