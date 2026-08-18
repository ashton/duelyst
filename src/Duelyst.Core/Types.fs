/// Strongly-typed identifiers and board coordinate (Constitution I: illegal states unrepresentable).
module Duelyst.Core.Types

type PlayerId = PlayerId of int
type EntityId = EntityId of int
type CardId = CardId of int

/// A board coordinate within the 9x5 grid (X in [0,8], Y in [0,4]).
/// In-bounds-ness is a predicate checked by Board.fs, not baked into the type.
type Position = { X: int; Y: int }

type ModifierId = ModifierId of int

/// Minimal this milestone (stat deltas only) -- M2's Effect DSL adds keywords/duration/triggers.
/// Lives here (not GameState.fs, despite data-model.md grouping it with Entity) because both
/// Actions.fs (ApplyModifier/Event cases) and GameState.fs (Entity.Modifiers) need it, and
/// GameState.fs also needs Action for its History field -- putting Modifier in GameState.fs would
/// make GameState.fs and Actions.fs depend on each other, which F#'s file-order compilation can't
/// express. Same reasoning applies to Outcome below.
type Modifier =
    { Id: ModifierId
      AtkDelta: int
      HpDelta: int }

/// A DU, not a bool+nullable-winner, so "ended with no winner recorded" is unrepresentable.
type Outcome =
    | InProgress
    | Win of PlayerId
    | Draw
