# AGENTS.md — Codex Operating Rules for ShopManager

> Permanent repository-level operating rules for Codex.
> Project history, phase status, findings, and handoff details belong in the project documentation, not here.

## Core Principles

**Never assume. Inspect first.**
**Evidence before implementation.**

## Required Workflow

AUDIT → SCOPE → DoD → IMPL → TEST → REVIEW → COMMIT

No stage may be skipped.

## Scope Rules

- Never modify files outside the explicitly approved scope.
- Do not introduce unrelated refactors or cleanup.
- Do not add, remove, or update packages without explicit approval.
- Do not change database schema or migrations without explicit scope.
- Do not implement Future Backlog items unless explicitly requested.
- If an out-of-scope problem is discovered, report it separately. Do not fix it.

## Evidence Rules

- Never mark a DoD item complete without verifiable evidence.
- Never convert uncertainty into a factual claim.
- When evidence is insufficient, use `POSSIBLE`, `NOT FOUND`, or `VERIFICATION PENDING` as appropriate.
- Do not claim a build, test, migration, benchmark, or runtime behavior succeeded unless it was actually executed and verified.
- Do not update `MASTER-BACKLOG.md`, `AUDIT-RECONCILIATION.md`, or a phase document to claim completion before the required evidence exists.

## Sensitive-Code Gate

Before modifying any of the following:

- financial logic
- inventory logic
- transaction boundaries
- concurrency or idempotency
- database schema or migrations
- backup or restore logic
- authentication, authorization, or other security-sensitive code

you must:

1. inspect the relevant implementation;
2. report the current behavior;
3. identify affected invariants and risks;
4. identify the files/symbols expected to change;
5. present the implementation plan;
6. wait for explicit approval before modifying code.

## Quality Gate

- Build target: **0 errors, 0 warnings**.
- All existing tests must pass.
- New or changed business logic requires appropriate tests.
- Test failure must be investigated; do not weaken or delete a valid test merely to make the suite pass.
- Do not hide warnings, exceptions, or failing behavior to satisfy the quality gate.

## Data and Git Safety

- Never delete or overwrite user data, database files, backups, migrations, or Git history without explicit approval.
- Never use `git reset --hard`, `git clean -fd`, force push, destructive checkout, history rewriting, or equivalent destructive commands without explicit approval.
- Do not commit, tag, push, merge, or rebase unless explicitly instructed.
- Before an approved commit, show the relevant diff/status and verify that unrelated files are not included.

## Documentation

Update project status documentation only after the required implementation, tests, and review provide evidence.

If a Phase is incomplete, record only verified findings; never represent the Phase as complete.

## Context and Token Efficiency

- Read `AGENTS.md` first.
- Use the current task prompt to determine which project documents and source files are relevant.
- Do not reread the entire repository when targeted inspection is sufficient.
- Prefer targeted search (`rg`, symbol search, or equivalent) before opening large files.
- Read only the relevant sections of large files whenever possible.
- Do not dump large source files into the response unless explicitly requested.
- Do not repeat long project history already available in repository documentation.
- Keep responses concise: findings, evidence, risks, proposed action.
- Expand context only when the current evidence is insufficient.
- Reuse verified facts from the current Phase session instead of repeatedly rediscovering them.
- Never reduce context so aggressively that correctness, safety, or required verification is compromised.

## Communication

- Technical explanations to the user: Persian.
- Code, identifiers, commit messages, and technical file content: English unless the existing project convention requires otherwise.
- Before implementation, identify the files/symbols to be changed and summarize the intended changes.
- Exact line numbers are required only when useful and reliable.
- Separate verified facts from assumptions or recommendations.

## Agent Roles

**ChatGPT is the Architect / Lead / Coordinator.**

Responsibilities include defining scope and DoD, sequencing work, coordinating agents, reviewing evidence, and enforcing quality, documentation, and Git gates.

**Claude Code is the Primary High-Value Repository Agent / Reviewer.**

Use Claude Code selectively for difficult, sensitive, architectural, correctness-critical, or high-risk repository work, including approved implementation and deep repository review.

**Codex is a High-Value Repository Agent / Independent Reviewer.**

Use Codex selectively for difficult verification, sensitive-code review, second opinions, and independent adversarial review when justified.

**DeepSeek is the General / Cost-Efficient Agent.**

Use DeepSeek primarily for scoped audits, documentation work, targeted investigation, and approved implementations that do not require a high-value agent.

**Work is the Independent Adversarial Reviewer.**

Use Work at important quality gates when an independent review materially improves confidence.

Claude Code and Codex are both high-value resources. Do not routinely duplicate the same work across both. Use independent cross-review when the risk or importance justifies the additional cost.

Only one AI agent may modify the working tree at a time. Other agents may perform read-only review concurrently.

No agent has automatic permission to modify repository code based on role alone. All agents remain subject to the approved Scope, Sensitive-Code Gate, explicit approval requirements, Quality Gate, Data and Git Safety rules, Documentation rules, and Stop Conditions.

For substantial phases, use:

1. **Prompt A — AUDIT ONLY**
2. **Prompt B — IMPLEMENT APPROVED PLAN**
3. **Prompt C — ADVERSARIAL REVIEW**

During Prompt A and Prompt C, do not modify source code unless the user explicitly changes the mode.

## Stop Conditions

Stop and report instead of improvising when:

- the requested change conflicts with these rules;
- the actual code contradicts the approved plan;
- required evidence cannot be obtained;
- an unexpected schema/data migration is required;
- unrelated existing changes could be overwritten;
- a destructive operation appears necessary;
- scope becomes materially larger than approved.

Ask for a decision before continuing.

---

**End of repository operating rules.**
