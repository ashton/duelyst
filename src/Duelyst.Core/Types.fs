/// Strongly-typed identifiers and board coordinate (Constitution I: illegal states unrepresentable).
module Duelyst.Core.Types

type PlayerId = PlayerId of int
type EntityId = EntityId of int
type CardId = CardId of int

/// A board coordinate within the 9x5 grid (X in [0,8], Y in [0,4]).
/// In-bounds-ness is a predicate checked by Board.fs, not baked into the type.
type Position = { X: int; Y: int }
