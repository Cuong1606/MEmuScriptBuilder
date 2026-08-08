# MEmu Script Studio — Product Specification

## 1. Product definition

MEmu Script Studio is a local, native Windows WPF productivity/operations app for creating, editing and running explicit scripts against one or more Android instances in MEmu through `memuc.exe`.

It is not an image macro recorder, an AI emulator controller or a cloud service. Current implementation status is canonical in [`project-state.md`](project-state.md); this specification separates current behavior from planned gaps. A model/property/API without a complete UI/runtime path is not an implemented feature.

## 2. Current product behavior

### 2.1 Startup and local configuration

- Show the main window before asynchronous initialization; keep unavailable actions disabled and show initialization/error state in that window.
- Enforce one app process per Windows user/session and activate the existing main window for a secondary launch.
- Discover `memuc.exe` when possible, allow manual selection, persist the selected path and validate it before use.
- Keep scripts, settings, logs and application-name mappings local. Do not send data to the Internet.

### 2.2 MEmu discovery and instance selection

- Call `memuc listvms` and parse index, name, running state and PID/window metadata when present.
- Display index, name and running/stopped state; refresh on demand. PID is currently internal metadata and is not displayed.
- Keep editor focus instance separate from run-target selection.
- Allow one or many running, unreserved targets. Never assume the first instance is index `0` and never auto-start a stopped instance.
- Support one common script or per-instance script assignment, including bulk assignment and target search/sort/filter in Control Center.
- A stored `DefaultInstanceIndex` and automatic target resolution from it are not current behavior.

### 2.3 Regular scripts and editor

Current step types are:

1. Android shell command via `memuc.exe -i INDEX execcmd`.
2. Force-stop an app.
3. Open an app by package/activity.
4. Delay.
5. Tap.
6. Hold.
7. Swipe.
8. Input text, optionally followed by Enter.
9. Paste Android clipboard, optionally followed by Enter.
10. Android key event: Back, Home, Menu, Volume up/down and Recent apps.
11. Note that is never executed.
12. Close all Chrome page targets through instance-scoped ADB/CDP.

Each regular step has a stable ID, name, enabled state and type-specific data. Executable steps carry timeout and continue-on-error behavior where applicable. The editor provides typed fields, validation and command preview; common commands do not require users to write full MEMUC syntax.

Regular scripts support create, explicit rename, duplicate and confirmed delete; ordered step add/edit/duplicate/delete; multi-select copy/paste; drag/up/down reorder; and per-script, in-session Undo-only history. A new step is persisted only through **Add**. Editing an existing non-Delay step is persisted through **Save**. A valid duration edit on an existing Delay is autosaved after the current 400 ms debounce; a new Delay still requires its first **Add**. Text inputs keep native clipboard/Undo behavior.

Tap/hold/swipe capture is a bounded input-assistance session against the selected MEmu viewport. It does not continuously record actions and does not resize, move or focus the MEmu window.

### 2.4 Persistence and exchange

- Persist the script library and application settings as versioned local JSON.
- Import/export selected or all scripts through validated `.memuscript` documents.
- Validate the complete document before mutation; copy import remaps script/step/composite-item IDs and references atomically.
- Scrub secret variable values during transfer even though variable authoring/substitution is not yet wired.
- Keep machine-local settings, logs and application-name mappings out of `.memuscript`.

### 2.5 Execution and cancellation

- Execute enabled steps sequentially on one instance, using `Task.Delay` for delays and direct `memuc.exe` calls for commands.
- Capture command preview, start/end time, exit code, and independently bounded stdout/stderr in execution results while fully draining both redirected streams. Truncated streams carry a clear marker. UI active/recent state remains bounded and does not retain full logs indefinitely.
- Respect per-step continue-on-error, timeout and cancellation; never freeze the WPF dispatcher.
- Do not equate MEMUC exit code `0` with target success by itself. Probe the correct instance's MEmu core at preflight using verified VM identity rather than an assumed process-tree relationship, pin that core's PID and process generation for the run, then recheck before process-backed steps, after Delay boundaries and before terminal success. A confirmed lost/replaced pinned core or reused PID stops later steps and ends as `Unavailable`; an initially unverified mapping is `Unknown`, remains unpinned and cannot become `Succeeded` merely because a matching or replacement core appears later.
- User Stop prevents later steps/commands but does not terminate the current MEMUC process. The runner waits for that PID to exit naturally and drains streams. The original timeout remains independent and may direct-kill only that command process after its grace period; production MEMUC paths never tree-kill.
- Keep an instance reserved and show `Đang dừng…` until execution/session cleanup is terminal; reject rerun during that interval.
- Closing MainWindow during execution or cleanup must request the same safe Stop-all behavior, reject new execution admission, keep the app alive through terminal cleanup/reservation release and then close once. It must not add a force-kill path or bypass editor draft resolution.

### 2.6 Multi-instance operations

- Control Center is the only run/stop surface. MainWindow remains editor-focused and shows only compact operational summary.
- Each click creates an independent launch group. A target cannot be active/waiting in two groups, but unrelated groups may overlap.
- Preflight all targets without starting VMs. Unavailable targets are recorded and skipped by default; an optional policy may abort before valid targets start.
- Start the first valid target immediately. Apply a fresh fixed/random launch spacing only between later admissions in the same group; groups do not wait for one another or for the previous target to finish.
- Create one immutable, encapsulated script-library snapshot per launch at admission and share that source snapshot across the group. Materialize only the required root/composite closure as a separate execution graph for each target, so later editor changes affect only later runs and runtime mutation cannot cross instances; cancellation/results also remain independent.
- Expose Stop per instance, selected instances and all active instances/groups. Backend group/session tokens remain isolated, but there is no current user action to stop exactly one launch group. Cancellation for one instance/session must not leak into unrelated work.
- Never scale/clamp stored tap/hold/swipe coordinates during execution.

### 2.7 Control Center and Recent Runs

- Top-level tabs are `Đang hoạt động` and `Kết quả gần đây`.
- Active view contains run setup/targets on the left and one flat virtualized/recycling active-instance DataGrid on the right. Search covers index/name/script; filters distinguish all/waiting/running/problem.
- Recent Runs is newest-first, RAM-only and capped at 20 completed launch snapshots. It is cleared on process restart.
- Each snapshot contains bounded scalar data for every terminal target: instance, script, last meaningful step, status and short message. It must not retain live tasks, execution objects, stdout/stderr or full logs.
- Users may select Failed/Unavailable targets that are currently runnable for a later action; the app does not auto-retry.
- Persist Control Center size/maximized state and native splitter ratios in `ApplicationSettings`; clamp valid finite values and repair invalid data at the persistence boundary.

### 2.8 Composite scripts and Chrome tabs

- A script is `Regular` or `Composite`; legacy data without the discriminator loads as Regular.
- Composite scripts contain only references by `ScriptId` to regular scripts and delay items. Nested composite references and broken references are invalid.
- Composite editor supports CRUD, selection, reorder, internal clipboard and Undo-only history. Import/export includes the required child-script closure.
- Adding a composite reference or Delay is explicit. Editing an existing reference requires **Save**; a valid duration edit on an existing composite Delay uses the same 400 ms debounce autosave lifecycle.
- Composite execution uses the existing one-instance engine for child scripts and the existing multi-instance scheduler above it.
- Close-all-Chrome-tabs targets `com.android.chrome` for the correct instance through dynamic ADB forwarding. Prefer Modern CDP `Target` commands; only typed capability/protocol incompatibility may fall back to legacy HTTP endpoints. Close page targets only, preserve non-page targets, verify zero pages, and never clear profile/cookies/history or drive Chrome UI.

## 3. Planned gaps

These remain product work, not current behavior:

- Script-variable authoring, deterministic placeholder substitution, missing-value validation, resolved preview and secret-safe execution logging.
- A safe direct-MEMUC step with explicit dangerous-command warning and validation.
- Run/test exactly one selected step.
- `.bat` export logically equivalent to the script, with C# delay represented safely for external execution.
- Script-library name search and user-selectable sorting.
- A template catalogue/picker. Only the automatic “Khởi động lại Chrome” template for an empty library exists today.
- End-to-end behavior for `DefaultInstanceIndex` and optional PID display.

Planned work is not accepted until source, UI, persistence/error paths and targeted tests are wired end-to-end.

## 4. Dropped or outside the current MVP

- Dark mode. The shipped app is light-only; dormant/old dark-theme documentation must not be treated as a feature.
- MEmu window page/order/grid management, move/resize/focus/restore and stored geometry.
- Persistent/full execution history, full-log viewer and automatic retry.
- Redo history for script/composite list mutations.
- Built-in VM remove/clone/import/export/reset commands.
- Continuous mouse/action recording, screenshots, OCR, computer vision or image-based button finding.
- AI control, server/cloud services, accounts, online sync, secret monitoring or automatic external installs.

## 5. Acceptance rules

- Current implemented behavior must continue to satisfy the boundaries above and the architecture in [`agent/architecture.md`](agent/architecture.md).
- A planned item becomes implemented only when it is usable end-to-end and its lifecycle, persistence, error/cancel behavior and tests are complete.
- Automated tests do not prove real MEmu integration or WPF visual/DPI behavior. Use the smoke-test rules in [`agent/verification.md`](agent/verification.md).
- Never claim MVP completion while a required planned gap remains or a required MEmu/visual smoke test is `not run`/`blocked`.
