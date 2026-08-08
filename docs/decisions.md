# Current Decision Log

This file contains durable decisions that are still current. Retired/superseded prose lives in Git history, not in the default agent context. Current feature status remains in [`project-state.md`](project-state.md).

## D-001 — Local native Windows application

- Status: `accepted`.
- Decision: C#, .NET 8, WPF, MVVM, .NET DI and `System.Text.Json`; local data and offline operation, no server/cloud/account requirement.
- Consequence: do not replace the stack with Electron, Python or a web frontend without explicit approval.

## D-003 — Independent MEMUC commands

- Status: `accepted`.
- Decision: invoke `memuc.exe` directly for each normal command, use controlled arguments and `Task.Delay`, never `cmd.exe`, `&&` or a shell chain.
- Consequence: preview and execution share command semantics; stdout/stderr/exit code, timeout and cancellation are observed per process.

## D-007 — Polymorphic script steps

- Status: `accepted`.
- Decision: `ScriptStep` is an abstract base with stable `System.Text.Json` discriminators and derived types with type-specific validation.
- Consequence: adding a step requires model, serialization/migration, builder/executor, UI, validation and tests; an enum/model alone is not end-to-end support.

## D-019 — `.memuscript` transfer boundary

- Status: `accepted`.
- Decision: exchange versioned `.memuscript` JSON separately from local settings/logs; validate the full document before mutation and remap IDs/references atomically for copy import.
- Consequence: machine-local data is excluded and secret variable values are scrubbed even while variable execution remains partial.

## D-022 — Window-first single-instance startup

- Status: `accepted`.
- Decision: acquire the per-user/session mutex and named pipe before bootstrap. Show exactly one MainWindow before awaiting ViewModel initialization; secondary launches only request activation.
- Consequence: startup failure remains visible in the window/log, and app smoke considers an HWND as `READY` without launching a second process.

## D-023 — Undo-only list history

- Status: `accepted`.
- Decision: regular/composite list mutations keep bounded, in-session Undo history; no Redo command/stack.
- Consequence: text inputs retain native text Undo/Redo and list history is not persisted.

## D-027 — Dynamic multi-instance launch groups

- Status: `accepted`.
- Decision: the single-instance engine remains sequential and stateless; a scheduler above it preflights and runs independent targets. Each Start is a launch group with its own cancellation and admission snapshots; active/waiting instance indices are reserved globally.
- Consequence: unrelated groups may overlap, but one instance cannot belong to two active/waiting groups. Per-target results/progress remain isolated and coordinates are never scaled at run time.

## D-033 — Product trimmed around editor and operations

- Status: `accepted`.
- Decision: MainWindow owns the editor; Control Center owns run setup, targets, active instances and bounded Recent Runs. MEmu page/order/window-layout/focus/restore, persistent/full history and Redo are outside current scope.
- Consequence: old redesign/runtime documents are historical tombstones only; source and current docs must not resurrect removed routes.

## D-034 — Composite orchestration

- Status: `accepted`.
- Decision: a Composite script contains only regular-script references by ID and delays; nested/broken references fail validation. Execution snapshots the root and required library, then orchestrates the existing single-instance engine.
- Consequence: import/export carries a validated child closure and remaps script/step/item/reference IDs together.

## D-035 — Close Chrome pages through scoped CDP

- Status: `accepted`.
- Decision: close only Chrome `page` targets on the correct instance via dynamic ADB forwarding; prefer Modern CDP and fall back to legacy HTTP only for typed capability/protocol incompatibility.
- Consequence: verify zero pages, preserve non-page targets and browser data, clean forwarding in `finally`, and never use force-stop/UI automation as fallback.

## D-036/D-038 — Flat Control Center and native splitters

- Status: `accepted`.
- Decision: one flat virtualized active-instance table plus RAM-only Recent Runs capped at 20. Control Center uses native WPF Star-sized splitters; settings persist window size/maximized state and splitter ratios.
- Consequence: no launch-group cards/full logs/persistent history in UI. Layout persistence captures actual ratios on close and restores after layout without custom drag clamps.

## D-040 — User Stop waits for natural MEMUC exit

- Status: `accepted`; replaces all earlier user-cancellation termination behavior.
- Decision: production MEMUC requests use `WaitForNaturalExit` for user cancellation and `DirectProcessOnly` for timeout. Stop records cancellation, prevents later commands and waits for the same PID to exit/drain; it never calls direct/tree kill. The independent deadline may direct-kill only that command process after grace if a real timeout wins.
- Consequence: reservation and `Đang dừng…` persist through real process/session cleanup. For a MEmu crash, correlate application lifecycle and MEmu/Windows Event logs before changing `ProcessRunner`; timing alone is not causation.

## D-042 — Target success requires instance-specific core health

- Status: `accepted`.
- Decision: core discovery and runtime health are separate contracts. At preflight, the Windows resolver uses Tool Help to confirm the `listvms` host PID, derives the internal VM identity from that exact `MEmu.exe` command line, then maps the service-hosted `MEmuHeadless.exe` whose command-line `--comment` exactly matches the internal identity; the user-facing `listvms` display name is not a process identity. Native command-line metadata has a WMI `Win32_Process` fallback and ownership is never inferred from process ancestry. The resolver pins PID, creation time and verified internal identity. Bounded runtime/final checks do not resolve again and inspect only that pinned PID/generation.
- Consequence: an unverified initial mapping is `Unknown` and fails admission before the first step with `Failed`, not `Unavailable`. Confirmed pinned-core loss/replacement or PID reuse stops later work and ends the target as `Unavailable`; another Headless can never heal the existing run. Cancellation and terminal commit remain serialized per instance so an accepted Stop wins while a post-terminal Stop produces no false stopping feedback.

## D-041 — Current UI is light-only

- Status: `accepted`.
- Decision: current MVP merges `Colors.Light.xaml` only. Dark mode and runtime theme switching are not current product scope.
- Consequence: do not claim dark support from old documentation or create dark acceptance requirements unless the user explicitly reopens the feature.
