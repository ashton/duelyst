# Implementation Plan: Headless Core Rules Engine

**Branch**: `002-core-rules-engine` | **Date**: 2026-08-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-core-rules-engine/spec.md`

## Summary

Model `Duelyst.Core`'s domain — `GameState`, `PlayerState`, `Entity`, `Position`, the `Action`/`Event` DUs,
and `InvalidReason` — as pure, immutable F# types (Constitution I), then build the four-stage resolution
pipeline (`validate → modifyForExecution → apply → triggers`) behind a single pure entry point `step :
GameState -> Action -> Result<GameState * Event list, InvalidReason>`, plus `legalActions : GameState ->
Action list`. Implement the M1 rule slice on top of it — mana ramp, summon-near-friendly, move (pathfinding,
range 2), attack + counterattack, exhaustion/summoning-sickness, mulligan, fatigue/hand-cap draw handling,
and general-death win/draw detection — with all randomness threaded through an explicit seeded PRNG in
`GameState` so `step` is deterministic and replayable. No Effect DSL, keywords, triggers content, AI, or
client integration this milestone (M2+); the triggers pipeline stage exists as an inert extension seam only.
Proven via a headless Expecto + FsCheck test suite that scripts full matches through `step`/`legalActions`
with no UI. Technical approach and decisions are detailed in [research.md](./research.md).

## Technical Context

**Language/Version**: F# on **.NET 10** (SDK 10.0.300; same solution as M0, retargeted from .NET 9 per
constitution v1.1.0; no new toolchain).

**Primary Dependencies**: None beyond FSharp.Core / .NET BCL — `Duelyst.Core` stays dependency-free per
Constitution III (no Raylib, no IO libraries). Tests use **Expecto + FsCheck** (already vendored from M0).

**Storage**: N/A — `GameState` is in-memory only this milestone; no persistence/save format.

**Testing**: **Expecto + FsCheck** (`tests/Duelyst.Core.Tests`) — example tests per rule, FsCheck property
tests for invariants (mana never negative, HP floors at 0, board never double-occupied, exhausted units
never act twice), and determinism tests (same seed + action list ⇒ identical event list). A small headless
"scripted match" test harness (plain F# functions over `step`, no external harness framework) plays a full
match end-to-end as the acceptance vehicle for US1/SC-001.

**Target Platform**: Same as M0 — .NET 10, cross-platform; this milestone adds no platform surface (headless
library only, no window).

**Project Type**: Library addition to the existing multi-project .NET solution (`Duelyst.sln`) — fills in
the `Duelyst.Core` project that M0 created as an empty stub.

**Performance Goals**: Not perf-sensitive this milestone (no rendering, no real-time loop); `step` should
resolve a single action (including any triggered follow-ups) well under 1ms so property tests running
thousands of randomized action sequences stay fast in CI.

**Constraints**: `Duelyst.Core` MUST NOT reference Raylib or perform IO (Constitution III; violations block
merge). All randomness MUST flow through the seeded `Rng` in `GameState` — no ambient `System.Random`
(FR-014). `step` and `legalActions` MUST be pure functions (no mutation of inputs, no hidden state).

**Scale/Scope**: The M1 rule slice only — turn/mana ramp, summon-near-friendly, move (range 2, pathfinding),
attack + counterattack, exhaustion/summoning sickness, mulligan, fatigue/hand-cap, general-death win/draw.
No keywords, no Effect DSL content, no card-specific behavior (Content/AI/Client remain M0 stubs).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluated against `Duelyst F# Constitution` v1.0.0:

- **I. Domain-First Modeling** — PASS. This milestone *is* domain modeling: `GameState`/`Entity`/`Action`/
  `Event`/`InvalidReason` types are designed first ([data-model.md](./data-model.md)) before the pipeline
  functions that operate on them. Strongly-typed `EntityId`/`PlayerId` replace bare ints/strings; `Action`/
  `Event` are discriminated unions so illegal shapes (e.g. a `MoveUnit` with no destination) are
  unrepresentable.
- **II. Test-First (NON-NEGOTIABLE)** — PASS. Every rule (mana ramp, summon adjacency, movement range,
  attack/counterattack, exhaustion, mulligan, fatigue, win/draw) is written as a failing Expecto test (or
  FsCheck property) before its implementation, per [tasks.md](./tasks.md)'s Red→Green→Refactor task pairs.
  No deviation this milestone — `Duelyst.Core` and its tests are both plain F#/Expecto, unlike M0's
  decoupled Node pipeline.
- **III. Functional Core / Imperative Shell** — PASS by construction: this milestone builds only the core.
  `Duelyst.Core` has zero IO, zero Raylib reference, and no ambient `Random` (SC-005); `step`/`apply` are
  pure reducers returning `GameState * Event list`. There is no shell work this milestone (headless only).
- **IV. Immediate-Mode UI + TEA (No Stateful Components)** — N/A this milestone (no UI/client work; `Program.fs`
  from M0 is untouched). Re-checked: nothing here reintroduces retained UI state.
- **V. Designed for Evolution (Simplicity & YAGNI)** — PASS. The triggers pipeline stage is built as
  infrastructure now (needed for `step`'s shape and for follow-up-action draining that even M1's own rules
  need, e.g. a kill enqueueing no further actions today but the drain loop existing is required for General
  death detection to short-circuit cleanly) but ships with zero registered content — M2 adds triggers as
  data, not by reshaping the pipeline. Card-specific behavior stays out of scope; only the fixed M1 rule
  slice is hand-built.

**Technology & Architecture Constraints** — Honored: .NET 10/F#, Expecto+FsCheck, no Raylib/IO in
`Duelyst.Core`, rules constants (board 9×5, `MAX_MANA=9`, `STARTING_MANA=2`, `MAX_HAND_SIZE=6`,
`STARTING_HAND_SIZE=5`, mulligan replace count 2) live in the core as named data (Constitution's Technology
& Architecture Constraints), and a match is reducible to `initialSeed + action list` (FR-015/SC-003).

**Result: PASS (no violations, no Complexity Tracking entries needed).** Re-checked after Phase 1 design
([data-model.md](./data-model.md), [contracts/core-pipeline.md](./contracts/core-pipeline.md)) — still
PASS: `Position`/`Rng`/`Modifier`/`Entity`/`PlayerState`/`GameState`/`Action`/`Event`/`InvalidReason` are all
immutable records/DUs with illegal states unrepresentable (`Outcome` as a DU rather than a nullable winner;
one `InvalidReason`/`Event` case per concern); the pipeline contract's Guarantees section makes purity,
validate-before-apply, and `legalActions` correctness explicit and testable; nothing introduces IO, Raylib,
ambient randomness, or retained UI state.

## Project Structure

### Documentation (this feature)

```text
specs/002-core-rules-engine/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (module/function contracts — no network/API surface)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
duelyst_fsharp/
├── Duelyst.sln
├── src/
│   ├── Duelyst.Core/                # fills in the M0 stub — pure, no IO, no Raylib
│   │   ├── Types.fs                 # EntityId/PlayerId, Position, Rng seed type
│   │   ├── GameState.fs             # GameState, PlayerState, Entity records + init
│   │   ├── Actions.fs                # Action DU, Event DU, InvalidReason DU
│   │   ├── Rules.fs                  # rules constants (board size, MAX_MANA, hand size, …)
│   │   ├── Board.fs                  # adjacency/pathfinding (summon-near-friendly, move range 2)
│   │   ├── Pipeline.fs               # validate / modifyForExecution / apply / triggers / step
│   │   └── Rng.fs                    # seeded PRNG threaded through GameState
│   ├── Duelyst.Content/              # untouched this milestone (still M0 stub)
│   ├── Duelyst.AI/                   # untouched this milestone (still M0 stub)
│   ├── Duelyst.Assets/               # untouched this milestone (M0)
│   └── Duelyst.Client/               # untouched this milestone (M0)
└── tests/
    └── Duelyst.Core.Tests/           # NEW — Expecto + FsCheck, written test-first
        ├── Program.fs
        ├── GameStateTests.fs         # init, mana ramp, mulligan
        ├── SummonTests.fs            # summon-near-friendly legality
        ├── MoveTests.fs              # movement range/pathing legality
        ├── AttackTests.fs            # attack + counterattack, exhaustion, summoning sickness
        ├── WinConditionTests.fs      # general death -> win/draw, post-game rejection
        ├── LegalActionsTests.fs      # legalActions ⊆ step-accepted (US2)
        ├── DeterminismTests.fs       # same seed+actions ⇒ identical event list (US3)
        ├── InvariantPropertyTests.fs # FsCheck: mana≥0, HP≥0, no double-occupied tile, no double-act
        └── ScriptedMatchHarness.fs   # headless full-match harness (US1/SC-001/SC-006)
```

**Structure Decision**: No new project boundary — this milestone fills in the `Duelyst.Core` project M0
already created as an empty IO/Raylib-free stub and adds a sibling `tests/Duelyst.Core.Tests/` project
(mirroring M0's `Duelyst.Assets` / `Duelyst.Assets.Tests` pairing), both wired into the existing
`Duelyst.sln`. `Duelyst.Content`, `Duelyst.AI`, `Duelyst.Assets`, and `Duelyst.Client` are untouched. There
is no separate "contracts" network/API surface (this is a library, not a service); `contracts/` instead
documents the `step`/`legalActions`/`validate` function-signature contracts consumed by later milestones
(M2's Effect DSL, M3's client, M4's AI).

## Complexity Tracking

*No violations — Constitution Check passed cleanly (see above). Table intentionally omitted.*
