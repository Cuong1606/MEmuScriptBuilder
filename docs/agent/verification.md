# Verification and Quality Contract

Read this before concluding a task, phase or MVP.

## 1. Evidence states

Every check uses exactly one status:

- `passed`: the check actually ran and its result met the expectation.
- `failed`: it ran and did not meet the expectation.
- `not run`: it was not run; do not infer a result.
- `blocked`: a concrete external condition prevented it; state the blocker/evidence.

For terminal checks, report the command, exit code, observed result and what that evidence actually proves. Never call build/test/integration successful from old results or from source inspection alone.

## 2. Quality contract for substantial tasks

Review the change through this single sequence; skip an item only when it is genuinely not applicable and say why:

```text
behavior
→ lifecycle/state
→ persistence
→ error/cancel
→ WPF resize/focus/DPI
→ MEmu boundary
→ performance impact
→ targeted tests
→ final verification
→ runtime smoke when tests cannot prove the real UX/integration
```

- **Behavior:** acceptance criteria and source/UI wiring are end-to-end; infrastructure alone is not a feature.
- **Lifecycle/state:** initialization, navigation, concurrent/repeated operations, cleanup and stale callbacks preserve invariants.
- **Persistence:** load/save/migration/rollback and unrelated settings fields remain intact where relevant.
- **Error/cancel:** failures are visible and bounded; cancellation/timeout semantics remain distinct; no false success.
- **WPF resize/focus/DPI:** important controls remain reachable and focus/binding behavior is correct at applicable sizes/scaling.
- **MEmu boundary:** target, arguments, process ownership and no-auto-start/no-window-mutation/no-tree-kill rules are preserved.
- **Performance:** no new unbounded retention, UI-thread blocking, polling or accidental quadratic work on large collections.
- **Tests/final verification:** run focused tests during the change, then proportionate build/suite/review evidence.
- **Runtime smoke:** required when automated tests cannot prove visual/DPI/focus behavior or real MEmu integration.

Do not duplicate this checklist in other docs; link here.

### MEmu crash rule

Before changing `ProcessRunner`, cancellation or execution because MEmu crashed:

1. Read Script Studio application lifecycle logs and MEmu/Windows Event logs.
2. Align timestamp, instance and PIDs; identify whether MEMUC exited naturally, timed out or was terminated.
3. Separate correlation from causation and record competing evidence.
4. Change code only when evidence identifies an app-owned fault or a safe mitigation with testable semantics.

Current User Stop is no-kill; an observed `MEmuHeadless.exe`/`libGLESv2.dll` graphics fault must not be “fixed” in `ProcessRunner` from timing correlation alone.

## 3. Implementation verification loop

```text
Gather context → Plan → Implement → Targeted checks → Review → Fix → Retest → Final verification
```

- Do not delete, skip, disable or weaken tests to get green output.
- After a fix prompted by a failure/review, rerun the affected checks; stale results are insufficient.
- Stop after at most three non-progressing fix/retest rounds on the same issue and report the blocker.
- Check `git diff`, scope and whitespace before completion.

For substantial source changes, final verification normally includes:

```powershell
dotnet build MEmuScriptStudio.sln -c Release --no-restore
dotnet test MEmuScriptStudio.sln -c Release --no-build --no-restore
```

Run restore first when dependencies/assets require it. Use targeted project/filter tests while iterating. Exact commands may be adjusted to the task, but evidence rules do not change.

## 4. Test coverage by affected behavior

Tests should cover the behavior changed, including applicable boundaries such as:

- MEMUC arguments, escaping, preview/execution equivalence and `listvms` parsing.
- Step conversion, sequential execution, continue-on-error, timeout and cancellation.
- Persistence round-trip, schema repair/migration and preservation of unrelated fields.
- Multi-instance preflight, snapshot isolation, reservation, group/instance cancellation and late callbacks.
- Composite validation, transfer remapping and execution context.
- WPF binding/focus/resize/virtualization contracts where automated evidence is meaningful.
- Variables/placeholders, direct MEMUC, one-step run, `.bat` export or dark mode only if those currently non-implemented features are explicitly added.

Mock the process boundary in automated tests; do not require real MEmu for unit tests.

## 5. Runtime smoke

- Automated tests do not prove real MEmu integration or WPF visual/DPI behavior.
- Never claim MEmu integration passed without an authorized run on real MEmu.
- Use `scripts\launch-smoke.cmd` exactly as required by `AGENTS.md`; on `READY`, stop automation and wait for the user.
- Record target, action, exit code/result and observation without secrets. Never delete/reset VMs or exceed the authorized action.
- If unavailable or not authorized, report smoke as `not run` or `blocked`.

## 6. Definition of Done

A source change is complete only when scope/acceptance is met, implementation and review are proportionate to risk, required build/tests pass, valid findings are retested, diff is in scope, docs/state are current and all unrun checks are explicit.

A documentation-only change does not require unrelated build/test, but every edited file must be read back in full and links, contradictions, scope and whitespace checked. Report build/test as `not run`.

MVP completion additionally requires the current scope and accepted planned-gap decisions in [`../product-spec.md`](../product-spec.md), plus authorized real-MEmu/visual smoke evidence for behavior automated tests cannot prove.
