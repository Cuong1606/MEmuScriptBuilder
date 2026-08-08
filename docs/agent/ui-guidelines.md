# UI/UX Guidelines

Read this before changing UI/XAML. MEmu Script Studio is a native Windows WPF productivity/operations UI; product behavior remains defined by [`../product-spec.md`](../product-spec.md), not by design preference.

## 1. Current direction

- Prefer clarity, density and operational control over decorative effects.
- Use native WPF interaction patterns, keyboard/focus behavior and DPI-independent sizing.
- The current app is light-only. Dark mode is not current MVP behavior and must not be claimed from old/dormant resources.
- Do not add a web/frontend layer or Playwright for this desktop UI.

## 2. Current composition

### MainWindow

- Top bar: MEMUC path/connection, focus instance and entry to Control Center.
- Three resizable work areas: script library, step/composite list and typed inspector/preview.
- Editor owns draft, validation, persistence and list-mutation UX. It does not duplicate run controls, active tables or full logs.

### Control Center

- `Đang hoạt động`: run setup and targets on the left; one flat active-instance DataGrid on the right.
- `Kết quả gần đây`: bounded run list above and selected detail below.
- No MEmu page/order/window-layout UI and no persistent/full history.
- Active and Recent data remain bounded and concise; status uses text/glyph as well as color.

## 3. Interaction requirements

- Remain usable at desktop sizes from 1280×720 and through normal/maximized/restore states.
- Handle resize and Windows scaling 100/125/150% without clipped critical actions or unreachable content.
- Provide visible loading, empty, error, disabled, saving/executing and cancellation states.
- Keep keyboard navigation and focus visible; a mouse click must not accidentally commit unrelated draft state.
- Do not use color as the only status signal. Dangerous/destructive actions need distinct presentation and confirmation where required.
- Keep virtualized/recycling collections free of outer `ScrollViewer` wrappers that defeat virtualization.
- Preview must match execution logic and invalid draft data must not silently fall back to stale persisted values.

The Active Instances horizontal-sizing workaround and its runtime DPI validation gap are tracked only in [`../project-state.md`](../project-state.md).

## 4. Step and composite editors

- Show only fields relevant to the selected type and keep validation adjacent to the field.
- Distinguish Create, Edit and no-selection states. Preserve/resolve dirty or invalid drafts at navigation, close, run and export boundaries.
- Keep text-control clipboard/Undo native; route script/composite list shortcuts only when focus policy allows.
- Direct MEMUC, one-step test, variables/placeholders and `.bat` export are planned gaps, not hidden UI features.

## 5. Design-skill routing

Use `ui-ux-pro-max` once at the start only for a major native WPF UI audit/redesign or a substantial design-system revision. Small XAML fixes use the existing design system and do not require a skill call.

Frontend Design is not part of project routing. A design skill cannot add, remove or reclassify product functionality.

## 6. Verification

Use the single quality contract in [`verification.md`](verification.md). Automated XAML/unit tests do not prove real resize, focus, clipping, contrast or DPI behavior; run the authorized manual WPF/MEmu smoke path when those behaviors are material.
