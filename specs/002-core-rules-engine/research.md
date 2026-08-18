# Phase 0 Research: Headless Core Rules Engine

No `NEEDS CLARIFICATION` markers remain in the Technical Context (this milestone reuses M0's toolchain
entirely — F#/.NET 10, Expecto+FsCheck — so there were no unresolved technology unknowns to research). This
phase instead resolves the open **design** questions the spec's Assumptions section flagged as
reasonable-default territory, so Phase 1's data model can be written with confidence.

## R1: Movement — pathfinding vs. straight-line distance

**Decision**: Movement range is resolved via breadth-first search (BFS) over orthogonally-adjacent,
unoccupied, in-bounds tiles, capped at range 2 (i.e. a tile is reachable if the shortest unobstructed path
to it has length ≤ 2).

**Rationale**: The original Duelyst's movement is pathing-based, not Chebyshev/Euclidean distance — a unit
boxed in by other units has fewer reachable tiles than raw distance would suggest, and this is core to the
game's positioning tactics (zoning, chokepoints) that later milestones (Provoke's zone-of-control in M2)
build on. Modeling it as BFS now avoids a breaking rework later. BFS over a 9×5 grid capped at depth 2 is
trivially cheap (at most ~12 tiles visited).

**Alternatives considered**:
- *Chebyshev/Manhattan distance ignoring occupancy* — simpler, but silently allows moving "through" units,
  which is materially wrong and would need to be revisited for M2's Provoke keyword anyway.
- *Full pathfinding with variable per-unit movement + terrain costs* — the original's actual complexity
  (some units have movement 1/3, some ignore blockers via Flying) — deferred to M2+ per the spec's scope
  (all units have movement 2 this milestone, no Flying yet); BFS-with-fixed-range is the correct-shaped
  subset of the eventual algorithm, not a dead end.

## R2: Attack range — melee adjacency vs. ranged

**Decision**: This milestone, "in attack range" means orthogonally or diagonally adjacent (Chebyshev
distance 1) — plain melee. No unit has a ranged attack yet (`Ranged` is an M2 keyword).

**Rationale**: Matches the spec's Assumptions ("every unit exchanges counterattack on a basic attack this
milestone"); ranged units in the original never take counterattack damage when attacking from range, which
requires keyword-driven branching that's explicitly out of scope until M2.

**Alternatives considered**: Modeling an `attackRange: int` field on `Entity` now for forward-compatibility
— rejected as premature per Constitution V (YAGNI); adding it is a small, additive change in M2 when
`Ranged` actually needs it, not a breaking one.

## R3: Fatigue and hand-cap discard semantics

**Decision**: Drawing with an empty deck deals a small, fixed fatigue amount of damage directly to that
player's general (as a system-derived `Damage` action) instead of drawing; drawing while the hand is already
at `MAX_HAND_SIZE` discards (burns) the drawn card immediately after it would have been drawn, rather than
preventing the draw.

**Rationale**: This mirrors the original game's known behavior (rules-parity is this project's explicit
goal per `docs/planning.md`'s "same cards, same rules" framing) and gives the headless test harness a
well-defined, testable outcome instead of an unspecified no-op that would make long scripted matches
ambiguous.

**Alternatives considered**: *No-op on both* (silently skip the draw) — simpler, but diverges from the
original's rules and would make a `DrawCard` action's effect state-dependent in a way that's harder to
reason about/test than "always produces an event, just which one varies."

## R4: Simultaneous general death

**Decision**: If a single `apply` resolution reduces both generals' HP to 0 (only reachable via
system-derived, non-targeted damage this milestone — no M1 player action deals damage to both at once, but
the engine must not assume this can't happen), the match ends in a draw: a `MatchEnded` event carries an
outcome of `Win of PlayerId | Draw` rather than always naming a winner.

**Rationale**: Picking an arbitrary "first-checked" winner would be non-deterministic-feeling and wrong;
`Draw` is the honest outcome and keeps `MatchEnded`'s type honest (illegal states unrepresentable —
Constitution I) rather than encoding "winner" as a nullable/optional `PlayerId` that's sometimes
meaningless.

**Alternatives considered**: Disallow the situation entirely (assert/crash) — rejected because Constitution
III requires the core never to crash on reachable states, and per FR-008/edge-cases the spec explicitly
requires graceful draw handling.

## R5: Triggers pipeline stage with no registered content

**Decision**: `step` always runs all four stages (`validate → modifyForExecution → apply → triggers`) even
though M1 registers zero triggers; `triggers` this milestone is `event list -> Action list` returning `[]`
unconditionally (an inert but real stage, not a stubbed-out no-op branch), and `step`'s follow-up-action
drain loop is exercised by system-derived actions that already need it this milestone (e.g. a lethal
`Damage` event enqueueing no further action but a `MatchEnded`-producing check running after the queue
drains).

**Rationale**: Per Constitution V, this is the one piece of "infrastructure ahead of content" that's
justified: `step`'s public signature and the drain-loop shape are exactly what M2's Effect DSL needs, and
retrofitting a triggers stage into an already-shipped `step` would be a breaking signature change for every
M1 caller (tests, and later the harness). Building the seam now costs a few lines (a stage that returns
`[]`) and removes a known future breaking change.

**Alternatives considered**: Omit the `triggers` stage entirely this milestone and add it in M2 — rejected
because `step`'s return type and calling convention would then change between M1 and M2, breaking every M1
test and the scripted-match harness; the seam is cheap enough now to not be premature.

## R6: Test harness shape

**Decision**: The "headless text/scripted harness" (FR-017) is plain F# — a small module of helper
functions (`playTurn`, `scriptedMatch`, etc.) layered over `step`/`legalActions` inside
`tests/Duelyst.Core.Tests/ScriptedMatchHarness.fs`, invoked from Expecto test cases. It is not a separate
CLI/REPL tool.

**Rationale**: The spec's US1 independent test and SC-001/SC-006 only require that a match can be *scripted
and driven end-to-end with no UI* and that a contributor can *read* that script to follow the match — an
Expecto test file achieves both (it's a runnable, readable script) without building and maintaining a
separate interactive tool that has no consumer yet (Constitution V, YAGNI). A dedicated CLI harness can be
added later if M3/M4 actually need one; nothing here forecloses that.

**Alternatives considered**: A standalone `tools/CoreHarness/` console app — rejected as speculative
infrastructure with no current consumer beyond what the test project already provides.
