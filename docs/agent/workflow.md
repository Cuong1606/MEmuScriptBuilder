# Agent Workflow

## 1. Source ownership

The primary agent is the only source-code writer unless the user explicitly assigns work differently. Subagents may inspect, review or verify, but must not edit human-authored source/tests. Preserve unrelated worktree changes.

## 2. Workflow for a change

1. Read `AGENTS.md` and [`../project-state.md`](../project-state.md), then only the task-specific routed documents.
2. Inspect repository structure, `git status` and the relevant diff. Current source is implementation evidence; a model/property/API alone is not an implemented feature.
3. Establish scope, acceptance criteria and risk. State a short plan for substantial work.
4. Use `project_explorer` only for broad or genuinely unclear exploration that benefits from a separate read-only pass.
5. The primary agent makes the smallest coherent change and preserves working behavior.
6. Run targeted verification during implementation. For a substantial source change, use `qa_verifier` for meaningful final restore/build/test evidence.
7. Use `code_reviewer` when the diff carries shared-state, race, async/process, cancellation, timeout, security or correctness risk. It is optional for small local/docs-only changes.
8. Confirm findings from evidence, fix valid issues and rerun affected verification. Stop after at most three non-progressing fix/retest rounds and report the blocker.
9. Review scope/diff, apply the quality contract in [`verification.md`](verification.md), and update current state/durable decisions only if they changed.
10. Report files changed, commands/exit codes, build/test/smoke status and remaining gaps.

Agent calls are not a ceremony: small documentation edits, typo fixes and low-risk local changes do not require explorer → QA → reviewer mechanically. No agent may run MEmu without explicit permission in the current task.

## 3. Incremental delivery

- Work in small vertical slices that leave the solution coherent.
- Do not mark a planned feature implemented until UI/API wiring, lifecycle/state, persistence where applicable, error/cancel behavior and targeted tests exist end-to-end.
- Do not move to a dependent slice while the current source change has unresolved build/test failures.
- The current feature matrix and open issues live only in [`../project-state.md`](../project-state.md); do not append milestone history here.

## 4. Documentation-only tasks

- Do not edit source/tests, install dependencies, build/test, package, commit or push unless separately requested.
- Read every edited document back in full.
- Check link targets, contradictions, scope and whitespace.
- Report build/test as `not run` when not applicable; do not imply earlier results validate the current worktree.
- Wait for a new request before implementing any proposed next step.

## 5. Scope discipline

- Do not add features outside [`../product-spec.md`](../product-spec.md).
- Do not change behavior merely to satisfy an aesthetic direction.
- If a new request conflicts with a durable decision and materially changes scope, identify the conflict and request direction.
- A documentation change does not authorize source changes, tool installation, app launch or MEmu control.
