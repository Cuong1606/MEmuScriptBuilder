# Context Management and Handoff

## 1. Document roles

- `AGENTS.md`: stable guardrails and routing.
- [`../project-state.md`](../project-state.md): short current state only, replaced in place as reality changes.
- [`../decisions.md`](../decisions.md): durable current decisions only; Git history is the archive.
- Product/architecture/UI/verification docs: topic-specific source of truth loaded only when relevant.

Do not move terminal logs, corrective-pass history, old test totals or superseded behavior into default context.

## 2. When to update current state

Update `project-state.md` before a handoff/compact and whenever architecture, feature status, open issues, essential commands or a durable operational fact changes. Do not append a dated checkpoint section; edit the relevant current section.

At roughly 70–80% context, preserve only what a new session needs:

- Current objective and scope if work is still active.
- Current architecture/feature status that changed.
- Uncommitted files relevant to the active task.
- Latest applicable verification state without historical counts/logs.
- Open issue/blocker and the next concrete action.

When the task ends, remove transient objective/next-step prose that is no longer current.

## 3. What not to persist

- Long stdout/stderr or repeated terminal commands.
- Corrective-pass chronology or every failed/retried check.
- Old build/test totals, artifact sizes or commit hashes used only for a past session.
- Content already canonical in product spec, architecture, verification or AGENTS.
- Resolved bugs, speculation presented as fact, secrets/tokens or secret variable values.

## 4. Verification notation

Use `passed`, `failed`, `not run` and `blocked` as defined in [`verification.md`](verification.md). Keep only the latest result that matters to an active handoff, including command, exit code and concise scope. Delete it when it no longer describes the current worktree.

## 5. Starting a new conversation

1. Read `AGENTS.md` and `docs/project-state.md`.
2. Read only relevant current decisions and routed topic docs.
3. Inspect repository status/source instead of trusting prose blindly.
4. Continue from the active objective if one exists; otherwise treat the user's new request as the objective.
5. If source contradicts docs, source wins for implementation status and the docs must be corrected in scope.
