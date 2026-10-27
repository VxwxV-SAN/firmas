# PDF Editor — Design Spec

**Date:** 2026-10-26  
**Status:** Approved for implementation planning  
**Product rename:** Firmas PDF → **PDF Editor**  
**Approach:** Extend current WPF stack (PDFsharp + Windows.Data.Pdf) with content analysis (PdfPig)

## 1. Goals

Transform the existing Windows desktop signing app into a PDF editor named **PDF Editor** that can:

1. Edit existing text and images in a PDF when technically feasible (native edit).
2. Fall back to visual cover/replace when native edit is not reliable (tap/overlay).
3. Add new text and images to pages.
4. Keep the existing visual signature library and digital (CMS/PKCS#7) signing workflow.
5. Fully rename the product, project, namespaces, executable, local data path, and docs.

## 2. Non-goals (v1)

- OCR of scanned pages
- Table-as-table editing or advanced AcroForm form editing
- Perfect native edit of every PDF
- Overwriting the original file in place (always save a copy)
- Changing the digital-signing crypto model (remains CMS/PKCS#7 detached SHA-256)

## 3. Product identity (full rename)

| Current | Target |
|---|---|
| Display name `Firmas PDF` | `PDF Editor` |
| Folder / project `FirmasPDF` | `PdfEditor` |
| Namespace `FirmasPDF` | `PdfEditor` |
| Assembly / exe `FirmasPDF` | `PdfEditor` |
| Solution `FirmasPDF.slnx` | `PdfEditor.slnx` |
| Tests `FirmasPDF.Checks` | `PdfEditor.Checks` |
| `%LocalAppData%\FirmasPDF\` | `%LocalAppData%\PdfEditor\` |

- Tagline: “Edita, firma y guarda tus PDFs.”
- Migrate signatures from legacy AppData folder when new folder is empty.
- Update README, INSTRUCCIONES, run.bat, manifests, publish profiles.

## 4. Architecture

- **Viewer:** Windows.Data.Pdf
- **Export / write / digital sign:** PDFsharp-WPF
- **Content analysis:** PdfPig
- **Edit model:** ExistingText, ExistingImage, NewText, NewImage, Signature
- **Text modes:** Native when reliable; Tap (cover + redraw) otherwise; automatic fallback on native failure
- **Save pipeline:** native mutations → tap/new overlays → signatures → optional CMS → new file
- Digitally signed input PDFs remain read-only

## 5. UI

- Brand PDF Editor; tabs Editar / Firmas; tools Select, Text, Image, Signature
- Properties for selection; unified undo/redo; Spanish strings; starts maximized

## 6. Success criteria (v1)

1. Consistent PDF Editor identity
2. Edit a sentence (native or tap), save copy, visible in standard viewer
3. Delete/replace detected images or inform if impossible
4. Insert new text/images; place signatures
5. Digital signing works on edited output
6. Integration checks cover new paths

## 7. Decisions log

| Topic | Decision |
|---|---|
| Scope | Advanced editor (edit existing + add new) |
| Uneditable content | Hybrid: native when possible, else tap |
| Naming | Full rename to PDF Editor / PdfEditor |
| Signatures | Keep visual library + digital signing |
| Stack | Extend current (PDFsharp + PdfPig) |
| OCR | Out of scope for v1 |
| Save | Always save a copy |
| Signed input PDFs | Read-only |
