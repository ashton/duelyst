# Contract: `Duelyst.Core` Pipeline

`Duelyst.Core` is a library, not a network service, so this "contract" documents the public function
surface later milestones (M2 Effect DSL, M3 Client, M4 AI) and this milestone's own test harness are
allowed to depend on — the boundary that must stay stable (or only grow additively) as the core evolves.

## Public surface

```fsharp
namespace Duelyst.Core

module GameState =
    /// Builds the initial GameState for a fresh match: 9x5 board, both generals placed, starting
    /// hand/deck/mana per Rules constants, seeded Rng. Does not perform mulligan (a separate Action).
    val init : seed: uint64 -> player1Deck: CardId list -> player2Deck: CardId list -> GameState

module Pipeline =
    val validate           : GameState -> Action -> Result<unit, InvalidReason>
    val apply               : GameState -> Action -> GameState * Event list
    val step                 : GameState -> Action -> Result<GameState * Event list, InvalidReason>
    val legalActions         : GameState -> Action list

module Board =
    /// True if `to_` is reachable from `from_` via unoccupied, in-bounds tiles within `range` steps (R1).
    val isReachable : GameState -> from_: Position -> to_: Position -> range: int -> bool
    /// True if `pos` is orthogonally/diagonally adjacent to any of `player`'s entities (summon-near-friendly).
    val hasFriendlyAdjacent : GameState -> player: PlayerId -> pos: Position -> bool
```

## Guarantees

1. **Purity**: no function above performs IO, mutates its inputs, or reads ambient state (`DateTime.Now`,
   `System.Random`, environment variables). Calling the same function with the same arguments always
   returns the same result (FR-014, FR-015, SC-003, SC-005).
2. **`validate` before `apply`**: `apply` MUST NOT be called directly by any caller outside `Pipeline` on a
   player-initiated `Action` without a preceding successful `validate` — `step` is the only sanctioned entry
   point for player-initiated actions. System-derived actions (produced by `apply`/`triggers` as follow-ups)
   skip `validate` by construction (they are not player intents) but MUST still uphold every `GameState`
   invariant in Data Model §Entity/§PlayerState.
3. **`legalActions` ⊆ accepted-by-`step`**: for any `GameState` and any `Action` returned by `legalActions
   state`, calling `step state action` MUST return `Ok`, not `Error` (US2/SC-002). This is a correctness
   contract verified by dedicated tests (`LegalActionsTests.fs`), not merely aspirational.
4. **Total ordering of events**: the `Event list` returned by `step` is in the exact order effects occurred,
   including all drained follow-up actions' events — callers (a future client, or tests) MUST be able to
   replay/animate them in list order and get a coherent result.
5. **Stability**: `Action`, `Event`, and `InvalidReason` are additive-only DUs going forward — M2+ MAY add
   new cases (e.g. new `Action` cases for DSL-driven effects) but MUST NOT repurpose or remove an M1 case,
   since M1's own tests pattern-match on them exhaustively.

## Non-goals (explicitly out of contract this milestone)

- No serialization format for `GameState`/`Action`/`Event` is defined yet (no save files, no network wire
  format) — only in-memory F# values. A future milestone that adds networking/persistence will define this
  separately without needing to change the types above (they're already plain immutable records/DUs, which
  is what makes future serialization additive rather than a redesign).
- No `CardDef`/Effect DSL interpreter — `PlayCard`'s `apply` this milestone directly summons a generic unit
  from `CardId` using stats it looks up (from `assets/cards.json`-sourced fixtures or a minimal in-core
  lookup), not via the M2 DSL. `Duelyst.Content`'s `CardCatalog` (M2) is expected to slot in without
  changing `Action`/`step`'s shape.
