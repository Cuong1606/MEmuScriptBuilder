# Project State

Current-state snapshot only. Read source and the current worktree when this file conflicts with implementation; do not infer a feature from a model/property/API alone.

## Product and architecture

- Native local Windows productivity/operations app built with C#, .NET 8, WPF, MVVM and .NET DI.
- Solution projects: `MEmuScriptStudio.App`, `MEmuScriptStudio.Core`, `MEmuScriptStudio.Infrastructure`; tests are split into Core and Infrastructure test projects.
- Data is local JSON via `System.Text.Json`; scripts and settings are separate. Script transfer uses `.memuscript` JSON with validation/remapping.
- `MainWindow` is the editor. `ControlCenterWindow` is the single shared operations surface for run setup, targets, active instances and Recent Runs; both use one `MainViewModel`/scheduler state.
- `ScriptExecutionEngine` executes one target sequentially. `MultiInstanceExecutionScheduler` shares one encapsulated library snapshot per launch group and materializes only each target's independent execution graph, alongside per-instance reservation and cancellation.
- Normal commands invoke `memuc.exe` directly with controlled arguments. Delay uses `Task.Delay`; process execution is async and captures stdout/stderr independently up to 64 Ki characters per stream while draining both streams fully.
- Production MEMUC requests use `WaitForNaturalExit` for user cancellation and `DirectProcessOnly` for timeout. User Stop prevents later commands but never kills the current MEMUC process or process tree.
- Target success also requires instance-specific core health at bounded lifecycle boundaries. At preflight, a dedicated resolver confirms the `listvms` host, derives the internal VM identity from that exact `MEmu.exe` command line and maps the service-hosted `MEmuHeadless.exe` whose `--comment` matches it; the user-facing `listvms` display name is not treated as process identity. Command-line metadata uses a native reader with a built-in WMI `Win32_Process` fallback, then pins PID, creation time and verified internal identity without assuming a parent/descendant relationship. An unverified preflight mapping fails admission before the first step. After admission, checks use only that pinned PID/generation, so confirmed core loss, replacement or PID reuse becomes `Unavailable` and cannot be replaced by a different Headless process.

## Important working behavior

- Startup shows the WPF window before asynchronous initialization; a second app process activates the existing window through mutex/named-pipe coordination.
- MEMUC path discovery/manual selection, `listvms` parsing and instance refresh are wired. Index/name/running state are displayed; PID/HWND support is used internally for capture.
- Regular scripts support create/rename/duplicate/delete, `.memuscript` import/export, ordered typed steps, enable/disable, continue-on-error, preview, copy/paste, drag/up/down and Undo-only history. New steps require **Add**; existing non-Delay steps require **Save**; only a valid duration edit on an existing Delay is autosaved after the current 400 ms debounce. Script rename remains explicit. Composite references use explicit add/save, while existing composite Delay duration edits use the same debounce autosave lifecycle.
- Implemented regular step types: Android shell, force-stop app, open app, delay, tap, hold, swipe, input text, Android clipboard paste, Android key event, note and close-all-Chrome-tabs.
- Tap/hold/swipe input assistance is bounded one-shot capture against the selected MEmu viewport; it is not a macro recorder.
- Multi-instance execution supports selected/all-remaining targets, common or per-instance scripts, fixed/random launch spacing, preflight, reservations, per-instance/selected/all Stop and bounded active state. Group cancellation remains isolated backend infrastructure but has no current UI action.
- Closing MainWindow while execution or cleanup is active starts the existing safe Stop-all flow, keeps the app/window alive until every session is terminal and every reservation is released, then closes once; repeated Close requests do not bypass cleanup.
- Composite scripts support regular-script references plus delay, editor CRUD/clipboard/Undo, validated import/export closure and execution through the normal scheduler.
- Local settings and script persistence reject unsupported future schemas before mutation/save. Corrupt settings are backed up and recover to writable defaults; corrupt script libraries are backed up and remain write-blocked until explicit recovery is confirmed. Failed script saves roll back library/item models, selection, Undo history, assignments and timestamps for regular and composite mutations.
- Recent Runs is RAM-only, newest-first, maximum 20 terminal snapshots. It stores bounded scalar summaries for every target, not full logs/tasks/execution objects, and is not persisted.
- Current UI loads the light resource dictionary only. There is no runtime theme switch.

## Feature status matrix

| Feature | Status | Current evidence/meaning |
| --- | --- | --- |
| Recent Runs | `IMPLEMENTED` | End-to-end Control Center list/detail, bounded to 20 RAM snapshots. |
| Multi-instance | `IMPLEMENTED` | Target assignment, scheduler, snapshot, reservation, progress and cancellation are wired. |
| Composite scripts | `IMPLEMENTED` | Editor, persistence/transfer validation and execution are wired end-to-end. |
| Script variables / placeholders | `PARTIAL` | Models, cloning/transfer secret scrubbing and request dictionaries exist; no editor, substitution, missing-value validation or resolved preview/execution. |
| Templates | `PARTIAL` | Only “Khởi động lại Chrome” exists and is created for an empty library; no template catalogue/picker or other templates. |
| `DefaultInstanceIndex` | `PARTIAL` | Persisted/cloned only; no UI or run-target resolution consumes it. |
| PID / instance metadata | `PARTIAL` | Index/name/running/PID/HWND are parsed/carried and PID/HWND support capture; PID is not displayed. |
| Direct MEMUC step | `PLANNED` | No step type, editor or execution route. |
| Run/test one step | `PLANNED` | No command/UI path; run commands execute assigned full scripts. |
| `.bat` export | `PLANNED` | Current transfer service and dialogs support `.memuscript` only. |
| Script search/sort | `PLANNED` | Library currently has only Regular/Composite type filtering; target search/sort is separate and implemented. |
| Dark mode | `DROPPED / NOT CURRENT MVP` | App merges only `Colors.Light.xaml`; no dark dictionary or switch is shipped. |

## Current known issues

1. **MEmu graphics crash causation remains unresolved.** Script Studio User Stop does not kill MEMUC. Current lifecycle evidence shows the command PID exiting naturally with no direct/tree kill, while Windows Application events identify repeated `MEmuHeadless.exe` faults in `libGLESv2.dll` with `0xc0000005`. Runtime health detection now prevents a confirmed core loss from being reported as target success, but it does not repair or attribute the graphics crash; follow the MEmu crash rule in [`agent/verification.md`](agent/verification.md).

No other production bug was confirmed by the evidence reviewed in this baseline. Planned/partial features above are gaps, not open bug claims.

## Commands for a new development session

Run only when the task authorizes build/test:

```powershell
dotnet restore MEmuScriptStudio.sln
dotnet build MEmuScriptStudio.sln -c Release --no-restore
dotnet test MEmuScriptStudio.sln -c Release --no-build --no-restore
```

- Prefer targeted project/filter tests before the full suite when changing a local area.
- Runtime smoke is separate and only by explicit request: `scripts\launch-smoke.cmd` once after build. `READY` means stop automation and wait for manual testing.
- Do not use historical test totals as a baseline; test counts change with the source.

## Durable decisions agents need

- Source is implementation truth; product docs distinguish implemented, partial, planned and dropped states.
- Control Center owns operations UI; MainWindow remains editor-focused.
- MEmu window layout/page/order/full persisted history were removed from current product scope.
- Composite scripts may reference only regular scripts plus delays; execution uses admission snapshots.
- User Stop waits for current MEMUC natural exit; only an independent timeout may direct-kill that command process, never its tree.
- Current MVP is light-only; UI work is native WPF, not a web/frontend project.
