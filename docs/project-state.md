# Project State

Current-state snapshot only. Read source and the current worktree when this file conflicts with implementation; do not infer a feature from a model/property/API alone.

Current release: **1.2.0**.

## Product and architecture

- Native local Windows productivity/operations app built with C#, .NET 8, WPF, MVVM and .NET DI. It has MEmu and Android/ADB target providers in the same app.
- Solution projects: `MEmuScriptStudio.App`, `MEmuScriptStudio.Core`, `MEmuScriptStudio.Infrastructure`; tests are split into Core and Infrastructure test projects.
- Data is local JSON via `System.Text.Json`; scripts and settings are separate. Script transfer uses `.memuscript` JSON with validation/remapping.
- `MainWindow` is the editor. `ControlCenterWindow` is the single shared operations surface for run setup, targets, active instances and Recent Runs; both use one `MainViewModel`/scheduler state.
- `MainViewModel` remains the shared root ViewModel and binding surface; its implementation is grouped into responsibility-focused partials for initialization, devices, scripts, steps, execution and the existing composite/workspace/control/editor lifecycles.
- `ScriptExecutionEngine` executes one target sequentially. `MultiInstanceExecutionScheduler` shares one encapsulated library snapshot per launch group and materializes only each target's independent execution graph, alongside per-instance reservation and cancellation.
- Normal commands invoke `memuc.exe` directly with controlled arguments. Delay uses `Task.Delay`; process execution is async and captures stdout/stderr independently up to 64 Ki characters per stream while draining both streams fully. Android screenshots use a separate bounded binary stdout runner and never pass PNG data through text decoding.
- Android commands invoke configurable/discovered `adb.exe` directly and always include `-s SERIAL`. Android execution admission uses one lightweight `adb devices -l` transport snapshot for the group and performs no per-device model/resolution/DPI/orientation enrichment; full UI refresh retains that metadata discovery. Android and MEmu share scheduler snapshots, per-target reservation/cancellation, progress and Recent Runs while retaining separate command and health boundaries.
- Production MEMUC requests use `WaitForNaturalExit` for user cancellation and `DirectProcessOnly` for timeout. User Stop prevents later commands but never kills the current MEMUC process or process tree.
- Target success also requires instance-specific core health at bounded lifecycle boundaries. One admission pass snapshots process metadata once, resolves every requested MEmu from that shared snapshot, derives each internal VM identity from its exact `MEmu.exe` command line and maps the service-hosted `MEmuHeadless.exe` whose `--comment` matches it; the user-facing `listvms` display name is not treated as process identity. Command-line metadata uses a native reader with a built-in WMI `Win32_Process` fallback, then pins PID, creation time and verified internal identity without assuming a parent/descendant relationship. An unverified preflight mapping fails admission before the first step. After admission, checks query only that pinned PID/executable/generation directly without another full process-table scan, so confirmed core loss, replacement or PID reuse becomes `Unavailable` and cannot be replaced by a different Headless process.

## Important working behavior

- Startup shows the WPF window before asynchronous initialization; a second app process activates the existing window through mutex/named-pipe coordination.
- MEMUC path discovery/manual selection, `listvms` parsing and instance refresh are wired. Index/name/running state are displayed; PID/HWND support is used internally for capture.
- ADB path discovery/manual selection and `adb devices -l` refresh are wired. Android rows show provider, serial, connection state and bounded device metadata; an optional alias is persisted by exact serial without changing `android-adb:SERIAL`. A localhost ADB duplicate is hidden only when its listening process is an allowlisted MEmu executable under a Microvirt installation, or allowlisted product properties positively identify MEmu/Microvirt; unreadable, arbitrary and otherwise unknown localhost endpoints remain visible. Optional metadata failures surface a warning without changing a connected transport to unavailable. Unauthorized/offline devices are unavailable and no authorization/server restart is attempted.
- Regular scripts support create/rename/duplicate/delete, `.memuscript` import/export, ordered typed steps, enable/disable, continue-on-error, preview, copy/paste, drag/up/down and Undo-only history. The library supports native multi-selection, bulk duplicate/delete, Ctrl+A/Ctrl+D/Delete/F2 shortcuts, type filtering, name search and default/name A→Z/name Z→A sorting; drag reorder is available only in the unfiltered default projection and updates persisted order. Logical and visual selection stay aligned with the visible projection. New steps require **Add**; every existing step edit, including Delay, remains a draft until explicit **Save**. Script rename remains explicit. Composite references and existing composite Delay edits also use explicit save.
- Implemented regular step types: Android shell, force-stop app, open app, delay, tap, hold, swipe, input text, Android clipboard paste, Android key event, note and close-all-Chrome-tabs.
- The MainWindow editor target selector is independent from Control Center run assignment. Tap/hold/swipe input assistance uses the selected MEmu viewport or an exact-serial Android screenshot dialog; Android capture maps the displayed PNG back to native pixels and never sends input to the device. The app picker keeps the existing MEmu route and uses exact-serial launcher activity/label queries for Android without persisting the discovered catalog. Its read-only current-app action queries ActivityManager first and WindowManager only as fallback, preserves the detected Activity even when it differs from the launcher Activity, and adds a temporary selectable candidate for non-launcher apps. Android friendly-name aliases are persisted in local settings by exact package, overlay reliable Android labels, update the picker row/search and same-package editor draft immediately on save/delete/import, and remain blank/`Không xác định` when neither alias nor reliable label exists. A current step name supplies the initial overlay only when no saved alias exists. Android library exchange uses provider-tagged `.androidappnames` metadata; the MEmu `.memuappnames` format remains unchanged. MainWindow shows the selected name read-only. Force Stop/Open App steps store that name separately from package/activity, execution remains package/activity-only and legacy scripts load safely.
- MainWindow's library, steps and step-properties panes use native Star-sized Grid columns with adjacent minimum widths and explicit realtime splitters. Neither steps nor properties can be dragged to zero; double-clicking either splitter restores the default proportions.
- Multi-instance execution supports selected/all-remaining targets, common or per-instance scripts, fixed/random launch spacing, preflight, reservations, per-instance/selected/all Stop and bounded active state. Group cancellation remains isolated backend infrastructure but has no current UI action. The scheduler has no hard product target limit; practical capacity depends on CPU, RAM, GPU, USB, ADB and the target runtimes. Fake scale fixtures are structural regression tests, not supported-device limits.
- Control Center shows the exact current all-remaining candidate count in its run action. The regular-step editor can run the current valid Create/Edit draft as an isolated transient one-step script on `SelectedEditorTarget`; it reuses scheduler admission, reservation, provider health, cancellation and Active/Recent Runs without saving the draft or changing Control Center assignments.
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
| Android USB / ADB Phase 1 | `IMPLEMENTED` | Serial-scoped discovery/execution for Delay, Tap, Hold, Swipe, Input Text, Clipboard Paste, Force Stop, Open App and Home/Back/Recent Apps. Android app selection queries launcher activities from the exact editor serial; Android Shell is load/save compatible but hidden from new-step authoring, and Close All Chrome Tabs remains admission-blocked on Android. MainWindow capture supports Tap/Hold/Swipe from a serial-scoped PNG screenshot without executing device input. |
| Composite scripts | `IMPLEMENTED` | Editor, persistence/transfer validation and execution are wired end-to-end. |
| Script variables / placeholders | `PARTIAL` | Models, cloning/transfer secret scrubbing and request dictionaries exist; no editor, substitution, missing-value validation or resolved preview/execution. |
| Templates | `PARTIAL` | Only “Khởi động lại Chrome” exists and is created for an empty library; no template catalogue/picker or other templates. |
| `DefaultInstanceIndex` | `PARTIAL` | Persisted/cloned only; no UI or run-target resolution consumes it. |
| PID / instance metadata | `PARTIAL` | Index/name/running/PID/HWND are parsed/carried and PID/HWND support capture; PID is not displayed. |
| Direct MEMUC step | `PLANNED` | No step type, editor or execution route. |
| Run/test one step | `IMPLEMENTED` | A valid enabled executable Create/Edit draft runs on the exact editor target through the normal scheduler lifecycle without persisting the transient script. |
| `.bat` export | `PLANNED` | Current transfer service and dialogs support `.memuscript` only. |
| Script search/sort | `IMPLEMENTED` | Library name search combines with Regular/Composite filtering; sorting supports current persisted order, name A→Z and name Z→A without rewriting the library. |
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
- Runtime target identity is provider-qualified. MEmu uses index plus pinned core health; Android uses exact ADB serial plus transport state and never receives MEmuHeadless probes.
- Current MVP is light-only; UI work is native WPF, not a web/frontend project.
