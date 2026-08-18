---

description: "Task list for Headless Core Rules Engine"
---

# Tasks: Headless Core Rules Engine

**Input**: Design documents from `/specs/002-core-rules-engine/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/core-pipeline.md](./contracts/core-pipeline.md),
[quickstart.md](./quickstart.md)

**Tests**: REQUIRED. The constitution (Principle II, Test-First — NON-NEGOTIABLE) mandates TDD, so every
rule is written as a failing Expecto test (or FsCheck property) before its implementation.

**Organization**: Tasks are grouped by user story. All paths are repository-relative to
`/home/john/dev/duelyst_fsharp/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 (P1, full match), US2 (P2, legalActions), US3 (P3, determinism)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Wire the new `Duelyst.Core.Tests` project into the existing solution; no rule logic yet.

- [X] T001 Create `tests/Duelyst.Core.Tests/Duelyst.Core.Tests.fsproj` (Expecto + FsCheck, references
  `src/Duelyst.Core/Duelyst.Core.fsproj`) and a minimal `tests/Duelyst.Core.Tests/Program.fs` Expecto
  entry point
- [X] T002 Add `tests/Duelyst.Core.Tests/Duelyst.Core.Tests.fsproj` and confirm
  `src/Duelyst.Core/Duelyst.Core.fsproj` are both wired into `Duelyst.sln`
- [X] T003 Confirm `dotnet build Duelyst.sln` and `dotnet test tests/Duelyst.Core.Tests` both run
  (0 tests, green) before any rule code is written

**Checkpoint**: Empty but wired test project — ready for foundational types.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The domain types and rules constants every user story's rules are written against
(per [data-model.md](./data-model.md)). No pipeline behavior yet — just types + `GameState.init`.

**⚠️ CRITICAL**: Must complete before any user story.

- [ ] T004 [P] Implement `PlayerId`, `EntityId`, `CardId`, `Position`, `Rng` (seeded PRNG: `next`,
  `shuffle`) in `src/Duelyst.Core/Types.fs` and `src/Duelyst.Core/Rng.fs`
- [ ] T005 [P] Define the rules constants module (board 9×5, `MaxMana=9`, `StartingMana=2`,
  `MaxHandSize=6`, `StartingHandSize=5`, mulligan replace count 2, movement range 2, fatigue damage
  amount) in `src/Duelyst.Core/Rules.fs`
- [ ] T006 [P] Implement `Modifier`, `Entity`, `PlayerState`, `GameState`, `Outcome` records in
  `src/Duelyst.Core/GameState.fs`
- [ ] T007 [P] Implement `Action`, `Event`, `InvalidReason` discriminated unions in
  `src/Duelyst.Core/Actions.fs`
- [ ] T008 Write failing Expecto tests for `GameState.init` (board/players/generals placed, starting
  hand/mana/deck per `Rules` constants, seeded `Rng`) in `tests/Duelyst.Core.Tests/GameStateTests.fs`,
  then implement `GameState.init` in `src/Duelyst.Core/GameState.fs` to pass them

**Checkpoint**: Domain types compile and `GameState.init` is tested — user story work can begin.

---

## Phase 3: User Story 1 - Play a complete headless match to a win (Priority: P1) 🎯 MVP

**Goal**: The full `validate → modifyForExecution → apply → triggers` pipeline behind `step`, covering
mana ramp, summon-near-friendly, move (BFS range 2), attack + counterattack, exhaustion/summoning
sickness, mulligan, fatigue/hand-cap draw, and general-death win/draw — provable end-to-end via a
scripted headless match.

**Independent Test**: From a fresh `GameState.init`, script mana ramp → summon → move → attack (with
counterattack) → repeat to a general's death through `step`; confirm `Outcome` becomes `Win _`/`Draw` and
no invariant is violated along the way.

### Tests for User Story 1 (write first, MUST fail) ⚠️

- [ ] T009 [P] [US1] Write failing Expecto tests for mana ramp (`StartTurn`/`Refresh`: cap +1 up to
  `MaxMana`, mana refills to cap) and mulligan (replace ≤ allowed count; over-selection rejected with
  `TooManyMulligans`) in `tests/Duelyst.Core.Tests/GameStateTests.fs`
- [ ] T010 [P] [US1] Write failing Expecto tests for summon-near-friendly legality (adjacent-to-friendly
  accepted; occupied/out-of-bounds/no-friendly-adjacent rejected with the matching `InvalidReason`) in
  `tests/Duelyst.Core.Tests/SummonTests.fs`
- [ ] T011 [P] [US1] Write failing Expecto tests for `Board.isReachable` (BFS through unoccupied tiles,
  range 2; blocked/out-of-range rejected) and `MoveUnit` legality/`HasMoved` tracking in
  `tests/Duelyst.Core.Tests/MoveTests.fs`
- [ ] T012 [P] [US1] Write failing Expecto tests for attack + counterattack (both units damaged unless
  defender destroyed), exhaustion (`AlreadyActed` on a second attack), and summoning sickness
  (`SummoningSickness` on the turn a unit is summoned) in `tests/Duelyst.Core.Tests/AttackTests.fs`
- [ ] T013 [P] [US1] Write failing Expecto tests for general-death win/draw detection (single-general
  death → `Win` for the opponent; simultaneous double death → `Draw`; `GameAlreadyEnded` rejects further
  actions), and for fatigue-on-empty-deck / burn-on-full-hand `DrawCard` outcomes, in
  `tests/Duelyst.Core.Tests/WinConditionTests.fs`

### Implementation for User Story 1

- [ ] T014 [US1] Implement `Board.hasFriendlyAdjacent` and `Board.isReachable` (BFS over unoccupied,
  in-bounds tiles) in `src/Duelyst.Core/Board.fs` to pass T010/T011
- [ ] T015 [US1] Implement `Pipeline.validate` for `PlayCard`/`MoveUnit`/`Attack`/`Mulligan`/`EndTurn`
  (turn ownership, mana cost, summon/move/attack legality per T010–T013's `InvalidReason`s,
  `GameAlreadyEnded` once `Outcome <> InProgress`) in `src/Duelyst.Core/Pipeline.fs`
- [ ] T016 [US1] Implement `Pipeline.modifyForExecution` as identity (no modifiers alter actions this
  milestone, per plan.md) in `src/Duelyst.Core/Pipeline.fs`
- [ ] T017 [US1] Implement `Pipeline.apply` for player-initiated actions (`PlayCard`→`Summon`,
  `MoveUnit`, `Attack`→counterattack `Damage`s, `Mulligan`, `EndTurn`→`StartTurn`/`Refresh`/`DrawCard`)
  to pass T009–T012, producing the matching `Event`s in `src/Duelyst.Core/Pipeline.fs`
- [ ] T018 [US1] Implement `Pipeline.apply` for system-derived actions (`Damage` with fatigue-on-empty-
  deck and general-death detection setting `Outcome`, `Heal`, `Summon`, `Kill` removing a dead entity
  from `Board`/`Entities`, `ApplyModifier`/`RemoveModifier`, `DrawCard` with hand-cap burn, `StartTurn`,
  `Refresh` resetting `Exhausted`/`HasMoved`/`SummonedThisTurn`) to pass T013 in
  `src/Duelyst.Core/Pipeline.fs`
- [ ] T019 [US1] Implement `Pipeline.triggers : GameState -> Event list -> Action list` as the inert,
  always-`[]` stage (per research.md R5) in `src/Duelyst.Core/Pipeline.fs`
- [ ] T020 [US1] Implement `Pipeline.step` (validate → modifyForExecution → apply → triggers, draining
  follow-up actions via `apply` until the queue is empty, checking/setting `Outcome` after every `apply`)
  in `src/Duelyst.Core/Pipeline.fs`
- [ ] T021 [US1] Implement the headless scripted-match harness (`playTurn`, `scriptedMatch` helpers over
  `step`) driving a full match from `GameState.init` through mulligan, mana ramp, summon, move, attack +
  counterattack, to a general's death, asserting `Outcome = Win _` and no invariant violation, in
  `tests/Duelyst.Core.Tests/ScriptedMatchHarness.fs` (FR-017, SC-001, SC-006)
- [ ] T022 [US1] Run `dotnet test tests/Duelyst.Core.Tests`, confirm all US1 tests (T009–T013,
  T021) are green, and record the result

**Checkpoint**: US1 delivers a fully playable headless match through `step` — independently shippable MVP.

---

## Phase 4: User Story 2 - Query legal actions for the active player (Priority: P2)

**Goal**: `legalActions : GameState -> Action list` that enumerates every action `step` would accept for
the active player, consistent with `validate`.

**Independent Test**: From a battery of mid-match states (mana remaining, no mana, an exhausted unit, a
lethal attack available), every action `legalActions` returns is accepted by `step`, and a known-illegal
action is absent.

### Tests for User Story 2 (write first, MUST fail) ⚠️

- [ ] T023 [P] [US2] Write failing Expecto tests asserting every action returned by `legalActions` for a
  variety of mid-match states is subsequently `Ok` when passed to `step` (US2/SC-002), and that a
  known-illegal action (e.g. summon with no adjacent friendly, move for an already-moved unit) is absent
  from the result, in `tests/Duelyst.Core.Tests/LegalActionsTests.fs`
- [ ] T024 [P] [US2] Write a failing Expecto test asserting that when the active player has no legal
  action besides ending the turn, `legalActions` returns exactly `[EndTurn _]`, in
  `tests/Duelyst.Core.Tests/LegalActionsTests.fs`

### Implementation for User Story 2

- [ ] T025 [US2] Implement `Pipeline.legalActions` (enumerate candidate `PlayCard`/`MoveUnit`/`Attack`/
  `EndTurn` actions for the active player and filter by `validate`) to pass T023–T024 in
  `src/Duelyst.Core/Pipeline.fs`
- [ ] T026 [US2] Run `dotnet test tests/Duelyst.Core.Tests`, confirm all US2 tests are green, and record
  the result

**Checkpoint**: US2 delivers the shared legality-query surface M3 (client UI) and M4 (AI) will depend on.

---

## Phase 5: User Story 3 - Verify deterministic replay (Priority: P3)

**Goal**: Replaying an identical seed + action list through `step` always reproduces an identical event
list, and different seeds actually diverge where randomness is consumed.

**Independent Test**: Record the seed + action list from a scripted match (US1); replay twice from a
fresh `GameState`; confirm identical event lists both times, and diverging outcomes with a different seed.

### Tests for User Story 3 (write first, MUST fail) ⚠️

- [ ] T027 [P] [US3] Write a failing Expecto test that replays the same seed + the US1 scripted match's
  recorded action list twice from fresh `GameState.init` calls and asserts the two resulting `Event
  list`s are identical (US3/SC-003) in `tests/Duelyst.Core.Tests/DeterminismTests.fs`
- [ ] T028 [P] [US3] Write a failing Expecto test that runs the same action list under two different
  seeds (using a `Mulligan`/shuffle-dependent scenario) and asserts the randomness-dependent outcomes
  differ, confirming the seed is load-bearing in `tests/Duelyst.Core.Tests/DeterminismTests.fs`
- [ ] T029 [P] [US3] Write failing FsCheck property tests for the core invariants — mana never negative,
  HP never below 0, no two entities share a `Position`, an exhausted/already-moved unit never acts twice
  — generated over randomized legal action sequences (via `legalActions`) in
  `tests/Duelyst.Core.Tests/InvariantPropertyTests.fs` (SC-004)

### Implementation for User Story 3

- [ ] T030 [US3] Fix any determinism/purity gaps `Rng`/`Pipeline` surfaces under T027–T028 (e.g. ensure
  `Rng` is threaded everywhere randomness is consumed — shuffle, any random-target stand-in) in
  `src/Duelyst.Core/Rng.fs` / `src/Duelyst.Core/Pipeline.fs`
- [ ] T031 [US3] Fix any invariant violations T029's FsCheck properties surface in
  `src/Duelyst.Core/Pipeline.fs` / `src/Duelyst.Core/Board.fs`
- [ ] T032 [US3] Run `dotnet test tests/Duelyst.Core.Tests`, confirm all US3 tests (T027–T029) are green,
  and record the result

**Checkpoint**: US3 delivers the verified determinism/replay property the architecture requires.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T033 [P] Run the full [quickstart.md](./quickstart.md) validation (all 7 scenarios) and record
  results: build succeeds with no Raylib/IO reference in `Duelyst.Core` (SC-005), full test suite green,
  scripted match (US1/SC-001), `legalActions` consistency (US2/SC-002), determinism (US3/SC-003),
  invariant properties (SC-004), and all named edge cases
- [ ] T034 [P] Update `.github/workflows/ci.yml` if needed so `dotnet test Duelyst.sln` covers
  `tests/Duelyst.Core.Tests` (confirm it already does via the solution-wide test step from M0; add an
  explicit step only if it doesn't)
- [ ] T035 [P] Update `docs/planning.md` M1 status notes to reflect the delivered rules engine (mirroring
  the M0 "DELIVERED" entry's format)
- [ ] T036 Verify spec.md, research.md, data-model.md, contracts/core-pipeline.md, and tasks.md remain
  mutually consistent (no drift introduced during implementation); close out if consistent

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — blocks all stories.
- **US1 (Phase 3)**: depends only on Setup + Foundational. This is the MVP.
- **US2 (Phase 4)**: depends on US1's `Pipeline.validate`/`step` existing (legalActions filters candidates
  through `validate`).
- **US3 (Phase 5)**: depends on US1 (needs a scripted action list to replay) and exercises `Pipeline`/`Rng`
  built in US1; independent of US2.
- **Polish (Phase 6)**: depends on the stories it touches.

### Story completion order

MVP = **US1**. Then **US2** (legalActions, needed before M3/M4 can start). Then **US3** (determinism
verification) — US2 and US3 can proceed in parallel once US1 is done (different files, no shared
dependency between them).

### Within a story

Tests (Expecto/FsCheck) are written first and MUST fail before implementation (Constitution II).

### Parallel opportunities

- Foundational: T004, T005, T006, T007 in parallel (different files).
- US1 tests: T009, T010, T011, T012, T013 in parallel (different test files).
- US1 implementation: T014 can run parallel to T009–T013's authoring, but T015–T020 are sequential
  (all touch `Pipeline.fs` and build on each other).
- US2 tests: T023, T024 in parallel.
- US3 tests: T027, T028, T029 in parallel.
- Cross-story: after US1, Developer A takes US2 while Developer B takes US3.

---

## Parallel Example: Foundational Phase

```bash
Task: "Types/Rng in src/Duelyst.Core/Types.fs and src/Duelyst.Core/Rng.fs"
Task: "Rules constants in src/Duelyst.Core/Rules.fs"
Task: "GameState/Entity/PlayerState records in src/Duelyst.Core/GameState.fs"
Task: "Action/Event/InvalidReason DUs in src/Duelyst.Core/Actions.fs"
```

## Parallel Example: User Story 1 tests

```bash
Task: "Mana ramp + mulligan tests in tests/Duelyst.Core.Tests/GameStateTests.fs"
Task: "Summon-near-friendly tests in tests/Duelyst.Core.Tests/SummonTests.fs"
Task: "Move/BFS-reachability tests in tests/Duelyst.Core.Tests/MoveTests.fs"
Task: "Attack/counterattack/exhaustion tests in tests/Duelyst.Core.Tests/AttackTests.fs"
Task: "Win/draw/fatigue/burn tests in tests/Duelyst.Core.Tests/WinConditionTests.fs"
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **STOP & VALIDATE**: a full headless match
   plays to a win/draw through `step` with no invariant violations. Shippable on its own (proves the rules
   engine works end-to-end, per docs/planning.md's M1 goal).

### Incremental delivery

1. Setup + Foundational → domain types + `GameState.init` ready.
2. US1 → a complete, playable headless match (MVP).
3. US2 → `legalActions`, the shared surface M3/M4 need.
4. US3 → verified determinism/replay.
5. Polish → quickstart validation, CI confirmation, doc reconciliation.

---

## Notes

- [P] = different files, no dependency on an incomplete task.
- Constitution II is non-negotiable: verify each test fails before implementing.
- `Duelyst.Core` must stay IO/Raylib-free (Constitution III) — no task here touches `Duelyst.Client`/
  `Duelyst.Assets`.
- No Effect DSL, keywords, triggers content, AI, or client integration this milestone — `Pipeline.triggers`
  ships inert (T019) per research.md R5.
- Commit after each task or logical group.
