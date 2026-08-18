/// The atomic unit of intent/change (Action), the ordered record of what happened (Event), and the
/// typed rejection reason (InvalidReason) -- one case per concern rather than bare strings/bools, so
/// tests and legalActions can pattern-match instead of doing stringly-typed comparisons.
module Duelyst.Core.Actions

open Duelyst.Core.Types

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
    | Refresh of player: PlayerId

type Event =
    | ManaChanged of player: PlayerId * mana: int * cap: int
    | CardDrawn of player: PlayerId * card: CardId
    | CardBurned of player: PlayerId * card: CardId
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

type InvalidReason =
    | NotYourTurn
    | InsufficientMana
    | TileOccupied of Position
    | TileOutOfBounds of Position
    | NoFriendlyAdjacent of Position
    | UnreachableWithinMovementRange of EntityId * Position
    | AlreadyMoved of EntityId
    | AlreadyActed of EntityId
    | SummoningSickness of EntityId
    | NotInAttackRange of attacker: EntityId * defender: EntityId
    | TooManyMulligans of requested: int * allowed: int
    | GameAlreadyEnded
    | UnknownEntity of EntityId
    | UnknownCard of CardId
