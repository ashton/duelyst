# Feature Specification: Headless Core Rules Engine

**Feature Branch**: `002-core-rules-engine`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Model the core domain for the headless rules engine (M1 per
docs/planning.md): GameState (9x5 board, two PlayerState with mana/manaCap/hand/deck/graveyard/generalId,
activePlayer, turnNumber, seeded Rng, event/action log), strongly-typed EntityId/PlayerId, Entity (id,
cardId, owner, position, atk, curHP/maxHP, modifiers, exhausted/hasMoved flags), Position, and the Action
pipeline (validate -> modifyForExecution -> apply -> triggers) with a public step: GameState -> Action ->
Result<GameState * Event list, InvalidReason> entry point and legalActions: GameState -> Action list. Core
rules to support: turn/mana ramp, summon-near-friendly, move (range 2), attack + counterattack, exhaustion,
general-death win condition. Must be playable end-to-end via a text/headless test harness (no client/UI, no
Effect DSL/keywords yet -- those are M2). Determinism is required: same seed + same action list must always
produce the same event list. This is a new feature distinct from and building on
001-project-skeleton-asset-import (M0, delivered) -- Duelyst.Core stays pure/IO-free per the constitution."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Play a complete headless match to a win (Priority: P1)

A contributor drives a full two-player match purely by scripting a sequence of actions against the rules
engine — no rendering, no UI — starting from the opening deal, through mulligan, mana ramp, summoning units
near friendly units/generals, moving them, attacking (with counterattack applying), and ending when one
general's HP reaches 0. The engine reports what happened at every step as an ordered list of events.

**Why this priority**: This is the headline proof that the rules engine actually works end-to-end — every
later milestone (Effect DSL, client rendering, AI) depends on this rules core existing and behaving
correctly. A match that can be played to completion via a text harness is the concrete, demonstrable
deliverable of this milestone.

**Independent Test**: From a fresh `GameState` (given a seed), script a sequence of actions through the
public `step` entry point that plays out mana ramp, at least one summon, one move, one attack with
counterattack, and drives one general's HP to 0; confirm the match reports a winner and that no illegal
state (negative mana, negative HP, double-occupied tile) ever occurs.

**Acceptance Scenarios**:

1. **Given** a freshly initialized match, **When** each player takes their turn in sequence, **Then** each
   player's mana cap increases up to the turn cap and their mana refills to that cap at the start of their
   turn.
2. **Given** a player has enough mana and a legal board position, **When** they play a unit card, **Then**
   the unit is summoned only onto a tile adjacent to one of that player's existing units or general, and
   the engine reports a summon event.
3. **Given** a summoned unit under its movement range, **When** the owning player moves it, **Then** it
   relocates to a reachable, unoccupied tile within its movement range and the engine reports a move event.
4. **Given** two units are adjacent (or in attack range) with opposing owners, **When** one attacks the
   other, **Then** both units exchange damage (counterattack) unless the defender is destroyed outright,
   and the engine reports the damage/kill events in order.
5. **Given** a unit that has already acted this turn, **When** the owning player attempts to act with it
   again, **Then** the action is rejected as illegal and no state changes.
6. **Given** a general's HP is reduced to 0, **When** the triggering action resolves, **Then** the match
   immediately reports a win for the opposing player and no further player actions are accepted.

---

### User Story 2 - Query legal actions for the active player (Priority: P2)

A contributor (or a future caller such as the AI in M4 or the client's UI in M3) asks the engine, at any
point in a match, exactly which actions are currently legal for the active player, without needing to
guess-and-check by attempting actions and catching rejections.

**Why this priority**: `legalActions` is the shared foundation both future UI affordances and the AI depend
on. It is not independently visible to an end user, but it's a discrete, testable capability that can be
validated on its own against the same rules `step` enforces, and de-risks M3/M4 before they start.

**Independent Test**: From a variety of scripted mid-match states (mid-turn with mana remaining, no mana
left, a unit already exhausted, a lethal attack available), call `legalActions` and confirm every action it
returns is accepted by `step`, and that at least one known-illegal action (e.g. summoning where no adjacent
friendly unit exists) is absent from the list.

**Acceptance Scenarios**:

1. **Given** a mid-match state, **When** `legalActions` is called, **Then** every returned action is
   subsequently accepted (not rejected) if passed to `step`.
2. **Given** a mid-match state with a known illegal move (e.g. moving an exhausted unit), **When**
   `legalActions` is called, **Then** that illegal action does not appear in the result.
3. **Given** the active player has no legal actions besides ending the turn, **When** `legalActions` is
   called, **Then** it returns only the end-turn action.

---

### User Story 3 - Verify deterministic replay (Priority: P3)

A contributor takes an initial seed and a recorded list of actions from a completed or in-progress match
and replays them from scratch, confirming the resulting sequence of events is identical every time — the
property that makes matches reproducible, testable, and eventually network-ready.

**Why this priority**: Determinism is a cross-cutting correctness property rather than a standalone
user-visible feature, so it is ranked after the two directly demonstrable capabilities above. It is
nonetheless a hard requirement of the architecture (documented in `docs/planning.md` and the constitution)
and must be verified before later milestones build on top of `step`.

**Independent Test**: Record the seed and full action list from a scripted match (per US1); replay the same
seed and action list through a fresh `GameState`; confirm the resulting event list is identical to the
first run, across multiple scenarios including at least one that consumes randomness (e.g. a random-target
effect stand-in or shuffle).

**Acceptance Scenarios**:

1. **Given** an initial seed and an ordered action list, **When** the match is replayed from a fresh
   `GameState` twice, **Then** both runs produce an identical ordered event list.
2. **Given** two different seeds with the same action list, **When** both are played out, **Then** any
   randomness-dependent outcomes (e.g. shuffled deck order) differ between the two runs, confirming the seed
   actually drives the randomness rather than being ignored.

---

### Edge Cases

- **Drawing from an empty deck**: A player forced to draw with no cards left in their deck takes fatigue
  damage to their general instead of drawing, rather than the engine crashing or silently no-opping.
- **Hand full on draw**: A player at the maximum hand size who is forced to draw has the drawn card
  discarded (burned) instead of exceeding the hand size limit.
- **Summon target illegal**: Summoning onto a tile that is occupied, out of bounds, or not adjacent to any
  friendly unit/general is rejected with a specific reason, not silently ignored.
- **Move path blocked or out of range**: Moving to a tile that is occupied, out of bounds, or unreachable
  within the unit's movement range is rejected with a specific reason.
- **Acting twice with the same unit**: Attacking or moving with a unit that has already used that action
  this turn (per its exhausted/hasMoved flags) is rejected.
- **Simultaneous general deaths**: If an action reduces both generals' HP to 0 in the same resolution (e.g.
  a mutual-damage effect), the match ends in a draw rather than crashing or picking an arbitrary winner.
- **Action attempted after game over**: Once a match has ended (a general has died), further actions are
  rejected rather than silently mutating a finished match.
- **Mulligan over-selection**: Attempting to mulligan more cards than the allowed replace count is rejected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The engine MUST initialize a new match's `GameState` with a 9×5 board, two players each with
  their own mana/mana-cap, hand, deck, graveyard, and general, the starting hand size, starting mana, and
  mulligan replace count defined as project-wide rules constants (not scattered literals).
- **FR-002**: The engine MUST ramp each player's mana cap by one at the start of their turn (up to the
  maximum mana cap) and refill their mana to the current cap.
- **FR-003**: The engine MUST allow summoning a unit only onto an unoccupied, in-bounds tile adjacent to an
  existing friendly unit or the friendly general ("summon near friendly").
- **FR-004**: The engine MUST allow moving a unit only to an unoccupied, in-bounds tile reachable within
  that unit's movement range (2, this milestone), and MUST NOT allow a unit to move more than once per turn.
- **FR-005**: The engine MUST resolve an attack by applying damage to the defender and, unless the defender
  is destroyed by the attack, applying return damage (counterattack) to the attacker.
- **FR-006**: The engine MUST track per-unit `exhausted`/`hasMoved` state, resetting it at the start of that
  unit's owner's turn, and MUST reject a second move or attack from a unit that has already used that action
  this turn.
- **FR-007**: A unit summoned this turn MUST NOT be allowed to attack the same turn it was summoned
  ("summoning sickness"), consistent with no `Rush`-like keyword existing yet (Effect DSL/keywords are M2).
- **FR-008**: The engine MUST end the match and declare the opposing player the winner immediately when a
  general's HP reaches 0 (or declare a draw if both reach 0 from the same resolution), and MUST reject any
  further player-initiated action once the match has ended.
- **FR-009**: The engine MUST expose a pure validation step, `validate : GameState -> Action -> Result<unit,
  InvalidReason>`, that rejects illegal actions with a specific, inspectable reason before any state
  mutation occurs.
- **FR-010**: The engine MUST expose a pure reducer, `apply`, that turns a validated action into a new
  `GameState` plus an ordered list of events describing exactly what happened.
- **FR-011**: The engine MUST include a triggers stage in the resolution pipeline that matches produced
  events against subscriptions and can enqueue follow-up actions to be resolved through the same pipeline;
  this milestone has no content registering triggers, but the stage MUST exist as the extension point M2's
  Effect DSL builds on.
- **FR-012**: The engine MUST expose a single public entry point, `step : GameState -> Action ->
  Result<GameState * Event list, InvalidReason>`, that runs validate → modifyForExecution → apply → triggers
  (draining any follow-up actions) and returns the final state and the full ordered event list.
- **FR-013**: The engine MUST expose `legalActions : GameState -> Action list` that enumerates every action
  currently legal for the active player, consistent with what `validate`/`step` would accept.
- **FR-014**: The engine MUST thread all randomness through an explicit seeded PRNG carried in `GameState`;
  no ambient/global `Random` may be used anywhere in the core.
- **FR-015**: Replaying an identical initial seed and ordered action list through `step` MUST always
  produce an identical resulting `GameState` and event list.
- **FR-016**: The engine MUST support a mulligan step at match start allowing each player to replace up to
  the configured replace count of cards in their starting hand, rejecting attempts to replace more.
- **FR-017**: The project MUST provide a headless text/scripted test harness that can drive a complete match
  end-to-end through `step` with no client, UI, or rendering dependency.
- **FR-018**: `Duelyst.Core` MUST remain free of IO and of any Raylib/rendering dependency; all of the above
  behavior MUST be expressible and testable without a window, asset, or display of any kind.

### Key Entities *(include if feature involves data)*

- **GameState**: The complete, immutable state of a match at a point in time — the 9×5 board, both
  players' `PlayerState`, whose turn is active, the turn number, the seeded PRNG state, and the ordered
  action/event log so far.
- **PlayerState**: One player's mana, mana cap, hand, deck, graveyard, and which entity is their general.
- **Entity**: A single unit or general on the board — its id, originating card id, owning player, board
  position, attack, current/max HP, active modifiers, and its `exhausted`/`hasMoved` flags.
- **Position**: A board coordinate (column, row) within the 9×5 grid.
- **Action**: The atomic, typed unit of intent or system change (e.g. `PlayCard`, `MoveUnit`, `Attack`,
  `Damage`, `Heal`, `Summon`, `Kill`, `ApplyModifier`, `RemoveModifier`, `DrawCard`, `Mulligan`,
  `StartTurn`, `EndTurn`, `Refresh`), tagged as player-initiated (validated) or system-derived.
- **Event**: An ordered record of something that actually happened as a result of an action being applied —
  the contract the (future) client animates and the harness/tests assert against.
- **InvalidReason**: A specific, inspectable reason an action was rejected by `validate`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A contributor can script and play a complete headless match — opening deal through a
  general's death — entirely through the public `step`/`legalActions` surface, with zero UI or rendering
  code involved.
- **SC-002**: 100% of actions returned by `legalActions` are accepted (not rejected) when subsequently
  passed to `step`, across the scripted test scenarios.
- **SC-003**: Replaying an identical seed and action list produces a byte-for-byte identical ordered event
  list in 100% of tested scenarios, including at least one scenario that consumes randomness.
- **SC-004**: Core rule invariants — mana never negative, HP never below 0, no tile ever double-occupied,
  an exhausted/already-moved unit never acts again — hold across randomized property-based test runs with
  no counterexample found.
- **SC-005**: `Duelyst.Core` has zero IO and zero Raylib/rendering references, verifiable by inspecting its
  dependencies.
- **SC-006**: A contributor unfamiliar with this milestone can read the headless harness/tests and follow a
  full scripted match's turn-by-turn outcome in under 15 minutes.

## Assumptions

- Movement uses pathfinding through unoccupied board tiles (not straight-line distance) up to a fixed
  range of 2 for every unit this milestone — later per-unit movement values and movement-affecting keywords
  (e.g. Flying) are deferred to M2+.
- All units (not just some) have summoning sickness this milestone, since the `Rush` keyword that would
  waive it does not exist until M2's Effect DSL.
- Every unit exchanges counterattack damage on a basic attack this milestone; keyword-driven exceptions
  (e.g. `Ranged`, which historically avoids counterattack) are deferred to M2.
- Fatigue-on-empty-deck and burn-on-full-hand use the original game's known behavior as a reasonable
  default, since the spec's driving goal is rules parity with the original.
- A simultaneous double general-kill resolves as a draw; this is an edge case reachable only via
  system-derived damage this milestone (no player action currently deals damage to both generals at once),
  but the engine must not crash if it occurs.
- No Effect DSL, keywords (Provoke, Rush, Zeal, Ranged, Frenzy, Blast, …), auras, or card-specific
  triggered abilities are in scope this milestone — those are M2. The triggers pipeline stage exists as
  infrastructure only; no content registers with it yet.
- No AI, networking, or client/rendering integration is in scope this milestone — those are M3/M4.
- Card stats/costs for any cards exercised by the headless harness's scripted scenarios are sourced from
  the already-imported `assets/cards.json` (from M0) rather than newly authored fixtures, where convenient;
  synthetic fixture cards may also be used for isolated rule tests.
- The rules constants (board 9×5, `MAX_MANA=9`, `STARTING_MANA=2`, `MAX_HAND_SIZE=6`,
  `STARTING_HAND_SIZE=5`, mulligan replace count 2) are fixed as specified in the constitution's Technology
  & Architecture Constraints.
