# Phase 1 Data Model: Headless Core Rules Engine

Domain-first (Constitution I): every type below is designed before any pipeline function is written against
it. Illegal states are made unrepresentable via discriminated unions and strongly-typed ids rather than
primitives/booleans, per Constitution I and the constitution's rules-constants-as-data requirement.

## Entities

### 1. Identifiers

```fsharp
type PlayerId = PlayerId of int          // 0 or 1, but typed to prevent mixing with EntityId
type EntityId = EntityId of int          // unique per Entity for the life of the match
type CardId   = CardId of int            // reuses the original numeric card id (from assets/cards.json, M0)
```

**Rationale**: bare `int`s for player/entity/card ids were a known original-codebase pain point
(`docs/planning.md` "Key facts learned from the original repo"); single-case DUs give compile-time
separation at zero runtime cost.

### 2. Position

```fsharp
type Position = { X: int; Y: int }       // X in [0,8], Y in [0,4] — 9x5 board
```

**Validation**: in-bounds-ness (`0 ≤ X < 9 && 0 ≤ Y < 4`... `Y < 5`) is a predicate checked by `Board.fs`,
not baked into the type — keeps `Position` a plain value while `validate` is the single place that rejects
out-of-bounds targets (consistent with `validate`/`apply` being the seam that owns legality, per FR-009).

### 3. Rng (seeded PRNG)

```fsharp
type Rng = { Seed: uint64; State: uint64 }   // splitmix64-style, pure — `next : Rng -> int * Rng`
```

**Rationale**: FR-014/SC-005 require no ambient `System.Random`; a pure, immutable PRNG record threaded
through `GameState` and advanced by returning a new `Rng` alongside each random draw is the only way to keep
`apply`/`step` pure functions (Constitution III). Concrete algorithm is an implementation detail of
`Rng.fs`; the contract other modules rely on is `next : Rng -> int * Rng` and `shuffle : Rng -> 'a list ->
'a list * Rng`.

### 4. Modifier (minimal this milestone)

```fsharp
type ModifierId = ModifierId of int
type Modifier =
    { Id: ModifierId
      AtkDelta: int
      HpDelta: int }
```

**Rationale**: `Entity.Modifiers` is named in the user's own feature description and is load-bearing for
`Entity`'s shape, but no M1 rule produces or consumes a modifier yet (buffs/auras are M2's Effect DSL via
`ModifierDef`). Kept minimal (stat deltas only, no keywords/duration/triggers) — just enough that `Entity`'s
field exists and type-checks against an empty list in all M1 tests, without speculatively building M2's
`ModifierDef` (keywords, aura radius, triggered effects) ahead of need (Constitution V).

### 5. Entity

```fsharp
type Entity =
    { Id: EntityId
      CardId: CardId
      Owner: PlayerId
      Position: Position
      Atk: int
      CurHp: int
      MaxHp: int
      Modifiers: Modifier list
      Exhausted: bool          // has attacked this turn
      HasMoved: bool           // has moved this turn
      SummonedThisTurn: bool } // summoning sickness (FR-007)
```

**Invariants** (enforced by `validate`/`apply`, exercised by FsCheck — SC-004): `CurHp` never negative
(floors at 0, at which point the entity is removed via a `Kill` action, not left at a negative value); no
two entities ever share a `Position` on the same `GameState.Board`.

### 6. PlayerState

```fsharp
type PlayerState =
    { Mana: int
      ManaCap: int
      Hand: CardId list
      Deck: CardId list
      Graveyard: CardId list
      GeneralId: EntityId }
```

**Invariants**: `0 ≤ Mana ≤ ManaCap ≤ Rules.MaxMana`; `Hand.Length ≤ Rules.MaxHandSize` (enforced at the
`DrawCard`/`Mulligan` apply sites per R3 — overflow discards rather than growing the list).

### 7. GameState

```fsharp
type Outcome = InProgress | Win of PlayerId | Draw

type GameState =
    { Board: Map<Position, EntityId>       // occupancy index — no two entities share a tile
      Entities: Map<EntityId, Entity>
      Players: Map<PlayerId, PlayerState>
      ActivePlayer: PlayerId
      TurnNumber: int
      Rng: Rng
      Outcome: Outcome
      History: Action list }                 // append-only action log (event/action log, per spec)
```

**Rationale**: `Board` as `Map<Position, EntityId>` (rather than a 2D array of options) makes
"double-occupied" structurally rarer to introduce by accident and makes adjacency/BFS lookups
(`Board.fs`, R1) simple map queries; `Outcome` as a DU (not a `bool IsGameOver` + nullable winner) makes
"game over with no winner recorded" or "draw with a winner recorded" unrepresentable (Constitution I).
`History` is the action log the spec's `GameState` description calls for and what US3's determinism tests
replay.

### 8. Action (the atomic unit of intent/change)

```fsharp
type Action =
    // player-initiated (validated)
    | PlayCard of player: PlayerId * card: CardId * target: Position
    | MoveUnit of entity: EntityId * destination: Position
    | Attack of attacker: EntityId * defender: EntityId
    | Mulligan of player: PlayerId * cardsToReplace: CardId list
    | EndTurn of player: PlayerId
    // system-derived (produced during apply/triggers, not directly player-callable)
    | Damage of target: EntityId * amount: int * source: EntityId option
    | Heal of target: EntityId * amount: int
    | Summon of player: PlayerId * card: CardId * at: Position
    | Kill of target: EntityId
    | ApplyModifier of target: EntityId * modifier: Modifier
    | RemoveModifier of target: EntityId * modifier: ModifierId
    | DrawCard of player: PlayerId
    | StartTurn of player: PlayerId
    | Refresh of player: PlayerId          // exhausted/hasMoved/summonedThisTurn reset
```

**Rationale**: consolidates the spec's named action vocabulary into one DU with an explicit
player-initiated/system-derived split (as a doc-comment grouping — the pipeline treats the split as "does
`validate` run real legality checks, or does this action, only ever produced internally, get a trivial
pass") rather than two separate types, since `apply`/`triggers` need to treat both uniformly as "the next
thing to fold into `GameState`" (they share the `apply : GameState -> Action -> GameState * Event list`
signature — see Pipeline contract).

### 9. Event (ordered record of what happened)

```fsharp
type Event =
    | ManaChanged of player: PlayerId * mana: int * cap: int
    | CardDrawn of player: PlayerId * card: CardId
    | CardBurned of player: PlayerId * card: CardId        // hand-cap discard, R3
    | CardMulliganed of player: PlayerId * replaced: CardId list
    | UnitSummoned of entity: EntityId * card: CardId * owner: PlayerId * at: Position
    | UnitMoved of entity: EntityId * from_: Position * to_: Position
    | DamageDealt of target: EntityId * amount: int * source: EntityId option * remainingHp: int
    | UnitHealed of target: EntityId * amount: int * newHp: int
    | UnitDied of entity: EntityId
    | ModifierApplied of target: EntityId * modifier: Modifier
    | ModifierRemoved of target: EntityId * modifierId: ModifierId
    | TurnStarted of player: PlayerId * turnNumber: int
    | TurnEnded of player: PlayerId
    | MatchEnded of outcome: Outcome
```

**Rationale**: `Event` is the contract the (future, M3) client animates and what the harness/tests assert
against (per spec Key Entities). One case per observable change keeps assertions in tests precise (e.g. a
test for counterattack can assert exactly two `DamageDealt` events in order) — this is the "typed event
stream" Constitution III's rationale calls for.

### 10. InvalidReason

```fsharp
type InvalidReason =
    | NotYourTurn
    | InsufficientMana
    | TileOccupied of Position
    | TileOutOfBounds of Position
    | NoFriendlyAdjacent of Position          // summon-near-friendly violation
    | UnreachableWithinMovementRange of EntityId * Position
    | AlreadyMoved of EntityId
    | AlreadyActed of EntityId                // exhausted
    | SummoningSickness of EntityId
    | NotInAttackRange of attacker: EntityId * defender: EntityId
    | TooManyMulligans of requested: int * allowed: int
    | GameAlreadyEnded
    | UnknownEntity of EntityId
    | UnknownCard of CardId
```

**Rationale**: one case per rejection reason (not a bare `string`) so tests can assert *which* rule rejected
an action (FR-009's "specific, inspectable reason") and so `legalActions` (US2) can be implemented by
attempting candidates and filtering on `Ok`/`Error` without stringly-typed comparisons.

## Relationships

```
GameState.Players[PlayerId].GeneralId ──► GameState.Entities[EntityId]   (the general is just an Entity)
GameState.Board[Position] ──► EntityId ──► GameState.Entities[EntityId]  (occupancy index)
Action ──(validate)──► Result<unit, InvalidReason>
Action ──(apply)──► GameState * Event list
Event list ──(triggers, M1: always [])──► Action list (follow-ups, drained by `step`)
```

## Pipeline contract (function signatures — detailed further in `contracts/`)

```fsharp
module Pipeline =
    val validate            : GameState -> Action -> Result<unit, InvalidReason>
    val modifyForExecution   : GameState -> Action -> Action     // M1: identity (no modifiers alter actions yet)
    val apply                : GameState -> Action -> GameState * Event list
    val triggers             : GameState -> Event list -> Action list   // M1: always []
    val step                 : GameState -> Action -> Result<GameState * Event list, InvalidReason>
    val legalActions         : GameState -> Action list
```

**`step` behavior**: `validate` first; on `Error`, return it unchanged with no state mutation (FR-009). On
`Ok`, run `modifyForExecution` then `apply` to get `(state', events)`; run `triggers state' events` to get
follow-up `Action list`; recursively `apply` (not `step` — follow-ups are system-derived and skip
`validate`) each follow-up in order, accumulating events, until the queue is empty; return
`Ok (finalState, allEvents)`. After every `apply` (including follow-ups), check both generals' HP and set
`Outcome` accordingly before returning (R4).

## State transitions

- **Turn cycle**: `EndTurn` (current player) → `apply` produces `TurnEnded`, then `StartTurn` (next player,
  system-derived) → `Refresh` (mana ramp per FR-002, `Exhausted`/`HasMoved`/`SummonedThisTurn` reset per
  FR-006) → `DrawCard`.
- **Entity lifecycle**: created by `Summon` (from `PlayCard` or, for generals, match init) → mutated in
  place (position/HP/flags) by `MoveUnit`/`Damage`/`Heal`/`ApplyModifier` → removed from `Board`/`Entities`
  by `Kill` when `CurHp` reaches 0.
- **Match lifecycle**: `Outcome` starts `InProgress`; transitions once, monotonically, to `Win _` or `Draw`
  on the resolution where a general's `CurHp` reaches 0 (R4); once non-`InProgress`, `validate` rejects all
  further player-initiated actions with `GameAlreadyEnded` (FR-008).
