<!--
SYNC IMPACT REPORT
Version change: (unratified template) → 1.0.0
Rationale: Initial ratification. The prior file was the unfilled scaffold; this is the first
concrete constitution, so the version starts at 1.0.0 (MAJOR).

Principles (all newly defined from placeholders):
  - [PRINCIPLE_1_NAME] → I. Domain-First Modeling
  - [PRINCIPLE_2_NAME] → II. Test-First Development (NON-NEGOTIABLE)
  - [PRINCIPLE_3_NAME] → III. Functional Core / Imperative Shell
  - [PRINCIPLE_4_NAME] → IV. Immediate-Mode UI with TEA (No Stateful Components)
  - [PRINCIPLE_5_NAME] → V. Designed for Evolution (Simplicity & YAGNI)

Sections:
  - Added: Core Principles (5), Technology & Architecture Constraints, Development Workflow,
    Governance
  - Removed: none

Deferred TODOs: none. RATIFICATION_DATE set to 2026-08-16 (greenfield project initialized today).

Runtime guidance file referenced by Governance: docs/planning.md
-->

# Duelyst F# Constitution

## Core Principles

### I. Domain-First Modeling

Every feature MUST begin by modeling its domain before any behavior, IO, or UI is written:
name the types (records, discriminated unions), the invariants, and the vocabulary of the change.

- Make illegal states unrepresentable: prefer discriminated unions and strongly-typed ids
  (`EntityId`, `PlayerId`, `CardId`) over primitives and boolean flags.
- Domain types MUST be immutable and free of IO, rendering, and framework concerns.
- Behavior is added only once the types that constrain it exist.

Rationale: a well-modeled domain is the cheapest thing to evolve. Encoding invariants in types
moves errors to compile time and makes new cards, sets, and rules additive rather than invasive.

### II. Test-First Development (NON-NEGOTIABLE)

TDD is mandatory. Implementation ALWAYS starts by writing tests.

- Red-Green-Refactor is strictly enforced: write the test, watch it fail, implement to pass,
  then refactor.
- No production code may be committed without a failing test that motivated it.
- The deterministic core MUST be covered by example tests, FsCheck property tests for invariants
  (e.g. mana never negative, HP floors at 0, board never double-occupied), and determinism tests
  (same seed + same action list ⇒ identical event list).

Rationale: tests are the executable specification and the safety net that makes aggressive
evolution safe. Writing them first forces the domain and its contracts to be pinned down before
implementation drifts.

### III. Functional Core / Imperative Shell

Game rules live in a pure, deterministic core; all IO and runtime concerns live in a thin shell.

- The core (`Duelyst.Core`) MUST contain NO IO, no Raylib dependency, and no ambient `Random`;
  randomness is threaded through an explicit seeded PRNG carried in `GameState`.
- State transitions MUST be pure reducers producing a new state plus an ordered event list; the
  shell performs IO and animates that event stream.
- The core MUST remain engine-agnostic and independently testable without the client.

Rationale: isolating purity keeps the rules unit-testable, replayable, and network-ready, and
confines renderer/runtime churn to the shell so it can never block or corrupt rules work.

### IV. Immediate-Mode UI with TEA (No Stateful Components)

The UI MUST be immediate-mode and follow The Elm Architecture as a *concept* (not a specific
technology): Model → View → Update(Msg), with a single source of truth in the Model.

- Stateful or retained UI components are PROHIBITED; widgets MUST NOT hold their own mutable state.
- View MUST be a pure function of the Model, re-derived every frame.
- User input produces messages; messages update the Model; nothing else mutates UI state.

Rationale: one-way data flow with no hidden widget state makes the UI predictable, testable, and
trivially resettable and replayable alongside the deterministic core.

### V. Designed for Evolution (Simplicity & YAGNI)

Code MUST be optimized for change, not cleverness.

- Prefer data over code: new content SHOULD be expressed as data (e.g. `CardDef` values in the
  effect DSL); the typed F# escape hatch is reserved for the exotic minority of cases.
- Avoid speculative abstraction. Add structure only when a concrete second case demands it (YAGNI).
- Any added complexity MUST be justified against these principles or be removed.

Rationale: this rewrite exists to design away the original's class explosion. Keeping the common
case data-driven and the structure minimal is what keeps evolution cheap and honest.

## Technology & Architecture Constraints

- Language and runtime: **.NET 9, F#**.
- Renderer and shell: **Raylib-cs**; the sprite-animation, UI, and particle layers are built on top
  of it and confined to the shell.
- Testing stack: **Expecto + FsCheck**.
- `Duelyst.Core` MUST NOT reference Raylib or perform IO; violations block merge (see Principle III).
- A match MUST be reducible to `initialSeed + player action list` — serializable, replayable, and
  ready for server-authoritative online play without a core rewrite.
- Rules constants (board 9×5, `MAX_MANA=9`, `STARTING_MANA=2`, `MAX_HAND_SIZE=6`,
  `STARTING_HAND_SIZE=5`, mulligan replace count 2) live in the core as data, not scattered literals.

## Development Workflow

Tasks proceed in a fixed order, and every change is reviewed against it:

1. **Model the domain** — define the types and invariants for the change (Principle I).
2. **Write failing tests** — encode the expected behavior first; confirm Red (Principle II).
3. **Implement to Green** — the smallest code that passes the tests.
4. **Refactor** — improve structure while keeping tests green and the core pure.

- Every PR MUST demonstrate that tests preceded implementation and that the core stayed pure.
- CI gate: `dotnet test` green, including determinism and FsCheck property tests.
- Code review MUST verify: illegal states are unrepresentable, no stateful UI components, no IO in
  the core, and new content is expressed as data wherever feasible.

## Governance

This constitution supersedes ad-hoc practices and conventions. When guidance conflicts, the
constitution wins.

- **Amendments** require a documented rationale, a version bump per the policy below, and — for any
  change to a principle — a short migration note describing impact on existing code.
- **Versioning policy** (semantic): MAJOR for backward-incompatible governance/principle removals or
  redefinitions; MINOR for a new principle/section or materially expanded guidance; PATCH for
  clarifications and non-semantic refinements.
- **Compliance** is reviewed at every PR. Violations MUST be justified against a principle's
  rationale or fixed before merge; unjustified complexity is grounds to reject.
- Runtime development guidance and architecture detail live in `docs/planning.md`.

**Version**: 1.0.0 | **Ratified**: 2026-08-16 | **Last Amended**: 2026-08-16
