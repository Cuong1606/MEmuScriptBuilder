# MEmu Script Studio — Project Instructions

## 1. Product identity and source of truth

MEmu Script Studio is a local, native Windows WPF productivity/operations application for creating, editing and running explicit MEmu scripts through `memuc.exe`. It is not a web app, image macro recorder or AI emulator controller.

- Current implementation status: [`docs/project-state.md`](docs/project-state.md).
- Product scope and planned gaps: [`docs/product-spec.md`](docs/product-spec.md).
- Architecture and process boundaries: [`docs/agent/architecture.md`](docs/agent/architecture.md).
- Current source is the implementation evidence. A model, property, API or test fixture alone does not make a feature implemented.
- Keep changes incremental. Do not build the whole product or a large redesign in one change.

## 2. Required technology

- C#, .NET 8, WPF, MVVM and .NET Dependency Injection.
- `System.Text.Json` for JSON.
- `ProcessStartInfo` for processes; `async`/`await` for execution and `CancellationToken` for cancellation.
- `ObservableCollection` for dynamic UI collections.
- Add an MVVM dependency only when necessary and explain why before adding it.
- Do not replace the main stack with Electron, Python or a web/server architecture without user approval.

## 3. Stable scope boundaries

Do not add these unless the user explicitly requests them:

- Mouse/action recording or continuous macro recording.
- Screenshots, OCR, computer vision or image-based button finding.
- AI control of the emulator.
- Server/cloud services, accounts or online sync.
- Secret monitoring, external software download/install, ad/account/browser-data management.
- MEmu window arranging/resizing/focus/restore or VM configuration outside an explicit script requirement.

All application data remains local. UI design must not silently expand product scope.

## 4. Safety boundaries

- Never delete files or MEmu VMs.
- Do not expose built-in `memuc remove`, clone, import, export or reset operations in the current MVP.
- Do not run a command without a resolved target instance.
- Warn before any raw command that can be dangerous.
- Call `memuc.exe` directly per normal step; do not use `cmd.exe`, `&&` or a shell command chain.
- Prefer `ProcessStartInfo.ArgumentList`; handle spaces safely and never build uncontrolled argument strings.
- Use `Task.Delay` for delays, not `timeout.exe`.
- Capture stdout/stderr and check exit code. Preview and execution must be logically equivalent.
- Do not store plaintext passwords/tokens or write secret variable values to logs.
- Before changing command building, process running or execution, read [`docs/agent/architecture.md`](docs/agent/architecture.md).

## 5. Source ownership and optional agents

The primary agent is the only source-code writer unless the user explicitly assigns work differently. Subagents may inspect, review or verify; they must not edit human-authored source/tests.

Use project agents only when their value matches the task:

- `project_explorer`: broad or genuinely unclear repository exploration.
- `qa_verifier`: meaningful final verification for substantial source changes.
- `code_reviewer`: shared-state, race, process, cancellation, security or correctness risk.

Small, local or documentation-only tasks do not require mechanical agent calls. No agent may run MEmu without explicit permission in the current task. Detailed workflow: [`docs/agent/workflow.md`](docs/agent/workflow.md).

## 6. Task flow

1. Read this file and [`docs/project-state.md`](docs/project-state.md).
2. Inspect repository structure, `git status` and relevant diff. Preserve unrelated user changes.
3. Load only the task-specific documents routed below.
4. Confirm current behavior from source when a claim is uncertain; do not infer end-to-end support from infrastructure alone.
5. State a short plan before a substantial change, then edit only the relevant scope.
6. Apply the quality contract and verification rules in [`docs/agent/verification.md`](docs/agent/verification.md).
7. Update current state or durable decisions only when they actually change.
8. Report files changed, commands and exit codes, verification status, and anything not run or still open.

## 7. Documentation routing

| Task | Read first |
| --- | --- |
| Continue in a new conversation | [`docs/project-state.md`](docs/project-state.md), then relevant current entries in [`docs/decisions.md`](docs/decisions.md) |
| Change product behavior, scope or MVP criteria | [`docs/product-spec.md`](docs/product-spec.md) |
| Change models, projects, process runner, execution or persistence | [`docs/agent/architecture.md`](docs/agent/architecture.md) |
| Change UI/XAML behavior | [`docs/ui-design-system.md`](docs/ui-design-system.md), [`docs/agent/ui-guidelines.md`](docs/agent/ui-guidelines.md), and the related product section |
| Start an implementation change | [`docs/agent/workflow.md`](docs/agent/workflow.md) |
| Conclude a task | [`docs/agent/verification.md`](docs/agent/verification.md) |
| Compact or hand off context | [`docs/agent/context-management.md`](docs/agent/context-management.md) |

For a major native WPF UI audit or redesign, use `ui-ux-pro-max` once at the start if available. Do not require it for small XAML fixes. Frontend Design and Playwright are not project routing dependencies.

## 8. Documentation-only work

- Do not create source code, install dependencies, build/test, package, commit or push unless the user separately asks.
- Read every edited document back in full.
- Check links, scope and whitespace; report build/test as `not run` when they are not applicable.
- A proposed implementation step needs a new user request before work begins.

## 9. Context and durable records

- `AGENTS.md` contains stable rules and routing only.
- `docs/project-state.md` is current state only; never append corrective-pass history or old test counts.
- `docs/decisions.md` contains only durable, current decisions. Git history is the archive for retired prose.
- Do not place Markdown instructions in `.codex/rules`; that directory is for terminal execution policy only.

## 10. Runtime smoke test

- Build separately; never add build to the launcher.
- On Windows, open the app only through `scripts\launch-smoke.cmd`, once per user request. Do not call the `.ps1`, executable, `dotnet run` or another launcher directly.
- The wrapper rejects a second `MEmuScriptStudio.App` process and uses process-local `-ExecutionPolicy Bypass`; never change system or `CurrentUser` policy.
- On `READY`, stop automation and wait for the user to perform the manual runtime smoke test.
- On `TIMEOUT`, report the blocker and wrapper output only; do not start an extended diagnostic chain.
- Do not kill, restart or open another app process without explicit permission.
- Do not operate the app, execute scripts or control MEmu unless the user specifically requests it.
