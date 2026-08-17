# MEmu Script Studio — Product Specification

## 1. Product definition

MEmu Script Studio is a local, native Windows WPF productivity/operations app for creating, editing and running explicit scripts against multiple target providers: MEmu instances through `memuc.exe`, and USB-connected Android devices through `adb.exe`.

It is not an image macro recorder, an AI emulator controller or a cloud service. Current implementation status is canonical in [`project-state.md`](project-state.md); this specification separates current behavior from planned gaps. A model/property/API without a complete UI/runtime path is not an implemented feature.

## 2. Current product behavior

### 2.1 Startup and local configuration

- Show the main window before asynchronous initialization; keep unavailable actions disabled and show initialization/error state in that window.
- Enforce one app process per Windows user/session and activate the existing main window for a secondary launch.
- Discover `memuc.exe` and `adb.exe` when possible, allow manual selection, persist each selected path and validate it before use. A valid configured ADB path wins; otherwise discovery checks the Portable `tools/adb` runtime, installed Android SDK Platform Tools/PATH, then the sibling/installed ADB shipped with MEmu. Manual selection is the final fallback. The app does not download or install external software at runtime.
- Keep scripts, settings, logs and application-name mappings local. Do not send data to the Internet.

### 2.2 Target-provider discovery and selection

- Call `memuc listvms` and parse index, name, running state and PID/window metadata when present.
- Display index, name and running/stopped state; refresh on demand. PID is currently internal metadata and is not displayed.
- Keep editor focus instance separate from run-target selection.
- Allow one or many running, unreserved targets. Never assume the first instance is index `0` and never auto-start a stopped instance.
- Support one common script or per-instance script assignment, including bulk assignment and target search/sort/filter in Control Center.
- A stored `DefaultInstanceIndex` and automatic target resolution from it are not current behavior.
- Call `adb devices -l` for Android discovery. Android targets use the exact ADB serial as identity; `device` is runnable, while `unauthorized`, `offline` and unknown states remain visible but unavailable.
- Read Android manufacturer/model/version/SDK, current `wm size`, `wm density` and orientation for diagnostics without changing the device. Failure of optional metadata keeps a `device` transport runnable and shows a bounded warning. A disconnected, unreserved device disappears on refresh; a reserved row remains until its session is terminal, while its serial-based script assignment is preserved for reconnection.
- Allow a local display alias keyed by exact Android serial. The alias changes presentation only; target identity and assignment remain `android-adb:SERIAL`. Hide an ADB endpoint only when an allowlisted MEmu process under a Microvirt installation owns its localhost listener, or allowlisted Android product properties positively identify MEmu/Microvirt. Localhost, emulator-like and otherwise unknown endpoints remain visible without that evidence.
- Control Center labels every target as `MEmu` or `Android / ADB` and shows index or serial separately so an Android device cannot be confused with a MEmu instance. Editor preview follows a single selected target (or the sole discovered target), including the exact Android serial.

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

Android / ADB executes enabled Delay, Tap, Hold, Swipe, Input Text, Clipboard Paste, Force Stop, Open App and Home/Back/Recent Apps key-event steps; Note remains non-executable. Android Hold uses `input swipe X Y X Y DurationMs` to preserve the stored absolute coordinates and millisecond duration. Clipboard Paste uses `input keyevent KEYCODE_PASTE` and optional `KEYCODE_ENTER`; it does not read or change the Windows clipboard. Force Stop uses `am force-stop PACKAGE` and never requires an activity. Every Android process command includes `adb -s SERIAL`. Input Text keeps its separate ADB `%s` encoding contract. Android Shell remains schema/load/save compatible but is hidden from new-step authoring, and Close All Chrome Tabs remains explicitly unsupported on Android. Other unsupported step kinds and key events fail admission clearly and are never routed through MEMUC or silently skipped. Coordinates are passed through unchanged and are never scaled.

Each regular step has a stable ID, name, enabled state and type-specific data. Executable steps carry timeout and continue-on-error behavior where applicable. The editor provides typed fields, validation and command preview; common commands do not require users to write full MEMUC syntax.

Regular scripts support create, explicit rename, duplicate and confirmed delete; ordered step add/edit/duplicate/delete; multi-select copy/paste; drag/up/down reorder; and per-script, in-session Undo-only history. The library supports name search combined with Regular/Composite filtering and user-selectable current-order/name A→Z/name Z→A sorting; sorting does not rewrite persisted order. A new step is persisted only through **Add**. Every existing step edit, including Delay, is persisted only through **Save**. Text inputs keep native clipboard/Undo behavior.

The regular-step editor can run one current valid, enabled and executable Create/Edit draft on the exact `SelectedEditorTarget`. This creates an isolated transient one-step script and uses the existing scheduler admission, provider-specific health checks, reservation, timeout/cancellation and Active/Recent Runs lifecycle. It never saves or mutates the script library, persists run settings, or changes Control Center selection/assignments; Android remains exact-serial scoped and unsupported steps are disabled before admission.

MainWindow has one editor-target selector for preview, app selection and coordinate capture; it is independent from Control Center run selection and assignment. The MEmu app picker keeps its index-scoped behavior. The Android app picker queries launcher activities and reliable non-localized labels only from the exact selected serial and does not persist the discovered catalog. Its read-only current-app action queries `dumpsys activity activities` first and `dumpsys window` only when ActivityManager has no verified resumed component; every query includes `adb -s SERIAL`. A detected component is selected exactly, including a non-launcher Activity or a temporary non-launcher package candidate. The picker persists user-friendly aliases in local settings by exact package and resolves the displayed name as saved alias, then reliable Android label, then blank/`Không xác định`; package is never a friendly-name fallback. A matching current step name is the initial overlay only when no alias is saved. Ctrl+S/save/delete/import synchronizes the same-package editor draft without selecting a new component, including when the dialog later closes through Cancel/X. Choose also persists the current name and returns friendly name/package/activity; Cancel/X never changes package/activity. Android library import/export uses `.androidappnames` schema 1 with `Provider=AndroidAdb` and package/activity/friendly-name entries; the provider-neutral alias remains keyed by package and conflicts reuse the explicit overwrite/skip/cancel flow. The MEmu `.memuappnames` workflow remains unchanged. MainWindow displays the name read-only. App steps store friendly name separately from package/activity, and execution ignores that display metadata. Tap/hold/swipe capture remains a bounded input-assistance session: MEmu uses the selected viewport, while Android uses a serial-scoped PNG screenshot and maps display DIPs back to that PNG's native pixels. Android capture never sends input to the device, and neither provider path continuously records actions or changes device/window layout.

The MainWindow editor body uses three native Star-sized Grid panes for script library, steps and step properties. Each content pane has a practical minimum width, both splitters resize only their adjacent panes in either direction, and steps/properties never collapse to zero during window resize or extreme dragging. No whole-window horizontal-scroll or fixed-total-width workaround is used.

### 2.4 Persistence and exchange

- Persist the script library and application settings as versioned local JSON.
- Import/export selected or all scripts through validated `.memuscript` documents.
- Validate the complete document before mutation; copy import remaps script/step/composite-item IDs and references atomically.
- Scrub secret variable values during transfer even though variable authoring/substitution is not yet wired.
- Keep machine-local settings, logs and application-name mappings out of `.memuscript`.

### 2.5 Execution and cancellation

- Execute enabled steps sequentially on one target, using `Task.Delay` for delays and a direct provider-specific executable for commands.
- Dispatch process-backed steps by target provider. MEmu retains the existing MEMUC command/health contract; Android execution admission uses one lightweight `adb devices -l` transport snapshot for the launch group, matches only exact serial/state and performs no per-device metadata enrichment. Android runtime uses direct `adb.exe -s SERIAL ...` commands and exact-serial ADB transport state before process-backed steps, after Delay boundaries and before terminal success.
- Capture command preview, start/end time, exit code, and independently bounded stdout/stderr in execution results while fully draining both redirected streams. Truncated streams carry a clear marker. UI active/recent state remains bounded and does not retain full logs indefinitely.
- Respect per-step continue-on-error, timeout and cancellation; never freeze the WPF dispatcher.
- Do not equate MEMUC exit code `0` with target success by itself. Resolve all requested MEmu cores from one process-metadata snapshot per admission pass, using verified VM identity rather than an assumed process-tree relationship, and pin each core's PID and process generation for the run. Recheck that pinned PID/executable/generation directly before process-backed steps, after Delay boundaries and before terminal success without rediscovering the instance or rescanning the full process table. A confirmed lost/replaced pinned core or reused PID stops later steps and ends as `Unavailable`; an initially unverified mapping is `Unknown`, remains unpinned and cannot become `Succeeded` merely because a matching or replacement core appears later.
- User Stop prevents later steps/commands but does not terminate the current MEMUC process. The runner waits for that PID to exit naturally and drains streams. The original timeout remains independent and may direct-kill only that command process after its grace period; production MEMUC paths never tree-kill.
- Keep an instance reserved and show `Đang dừng…` until execution/session cleanup is terminal; reject rerun during that interval.
- Closing MainWindow during execution or cleanup must request the same safe Stop-all behavior, reject new execution admission, keep the app alive through terminal cleanup/reservation release and then close once. It must not add a force-kill path or bypass editor draft resolution.

### 2.6 Multi-instance operations

- Control Center is the only run/stop surface. MainWindow remains editor-focused and shows only compact operational summary.
- Each click creates an independent launch group. A target cannot be active/waiting in two groups, but unrelated groups may overlap.
- Reservations, progress and cancellation use the provider-qualified target key (`memu:INDEX` or `android-adb:SERIAL`), so multiple Android devices and MEmu indices cannot collide.
- Preflight all targets without starting VMs. Unavailable targets are recorded and skipped by default; an optional policy may abort before valid targets start.
- Start the first valid target immediately. Apply a fresh fixed/random launch spacing only between later admissions in the same group; groups do not wait for one another or for the previous target to finish.
- Create one immutable, encapsulated script-library snapshot per launch at admission and share that source snapshot across the group. Materialize only the required root/composite closure as a separate execution graph for each target, so later editor changes affect only later runs and runtime mutation cannot cross instances; cancellation/results also remain independent.
- Expose Stop per target, selected targets and all active targets/groups. Backend group/session tokens remain isolated, but there is no current user action to stop exactly one launch group. Cancellation for one target/session must not leak into unrelated work.
- Never scale/clamp stored tap/hold/swipe coordinates during execution.
- Do not impose a hard product limit on target count. Actual capacity depends on CPU, RAM, GPU, USB, ADB and target-runtime behavior; fake workloads such as 3/20/50/100 are regression fixtures, not supported-device claims or limits.

### 2.7 Control Center and Recent Runs

- Top-level tabs are `Đang hoạt động` and `Kết quả gần đây`.
- Active view contains run setup/targets on the left and one flat virtualized/recycling active-instance DataGrid on the right. Search covers index/name/script; filters distinguish all/waiting/running/problem.
- Recent Runs is newest-first, RAM-only and capped at 20 completed launch snapshots. It is cleared on process restart.
- Each snapshot contains bounded scalar data for every terminal target: instance, script, last meaningful step, status and short message. It must not retain live tasks, execution objects, stdout/stderr or full logs.
- Users may select Failed/Unavailable targets that are currently runnable for a later action; the app does not auto-retry.
- The all-remaining action displays `Chạy N thiết bị chưa chạy`, where `N` comes from the same candidate resolver used by that command and updates across discovery, admission and terminal cleanup.
- Target, active and Recent Runs tables show provider plus index/serial; Android diagnostic metadata remains bounded and Recent Runs still stores scalar snapshots only.
- Persist Control Center size/maximized state and native splitter ratios in `ApplicationSettings`; clamp valid finite values and repair invalid data at the persistence boundary.

### 2.8 Composite scripts and Chrome tabs

- A script is `Regular` or `Composite`; legacy data without the discriminator loads as Regular.
- Composite scripts contain only references by `ScriptId` to regular scripts and delay items. Nested composite references and broken references are invalid.
- Composite editor supports CRUD, selection, reorder, internal clipboard and Undo-only history. Import/export includes the required child-script closure.
- Adding a composite reference or Delay is explicit. Editing an existing reference or composite Delay requires **Save**.
- Composite execution uses the existing one-instance engine for child scripts and the existing multi-instance scheduler above it.
- Close-all-Chrome-tabs targets `com.android.chrome` for the correct instance through dynamic ADB forwarding. Prefer Modern CDP `Target` commands; only typed capability/protocol incompatibility may fall back to legacy HTTP endpoints. Close page targets only, preserve non-page targets, verify zero pages, and never clear profile/cookies/history or drive Chrome UI.

## 3. Planned gaps

These remain product work, not current behavior:

- Script-variable authoring, deterministic placeholder substitution, missing-value validation, resolved preview and secret-safe execution logging.
- A safe direct-MEMUC step with explicit dangerous-command warning and validation.
- `.bat` export logically equivalent to the script, with C# delay represented safely for external execution.
- A template catalogue/picker. Only the automatic “Khởi động lại Chrome” template for an empty library exists today.
- End-to-end behavior for `DefaultInstanceIndex` and optional PID display.

Planned work is not accepted until source, UI, persistence/error paths and targeted tests are wired end-to-end.

## 4. Dropped or outside the current MVP

- Dark mode. The shipped app is light-only; dormant/old dark-theme documentation must not be treated as a feature.
- MEmu window page/order/grid management, move/resize/focus/restore and stored geometry.
- Persistent/full execution history, full-log viewer and automatic retry.
- Redo history for script/composite list mutations.
- Built-in VM remove/clone/import/export/reset commands.
- Continuous mouse/action or screenshot recording, OCR, computer vision or image-based button finding; the bounded Android coordinate-capture screenshot above is the only current screenshot path.
- AI control, server/cloud services, accounts, online sync, secret monitoring or automatic external installs.
- Wireless ADB, scrcpy/screen mirroring, coordinate scaling, image recognition, UIAutomator, device profiles and automatic APK/package discovery UI.

## 5. Acceptance rules

- Current implemented behavior must continue to satisfy the boundaries above and the architecture in [`agent/architecture.md`](agent/architecture.md).
- A planned item becomes implemented only when it is usable end-to-end and its lifecycle, persistence, error/cancel behavior and tests are complete.
- Automated tests do not prove real MEmu integration or WPF visual/DPI behavior. Use the smoke-test rules in [`agent/verification.md`](agent/verification.md).
- Never claim MVP completion while a required planned gap remains or a required MEmu/visual smoke test is `not run`/`blocked`.
