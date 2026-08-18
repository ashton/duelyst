/// Rules constants, kept as named data rather than scattered literals (constitution's Technology &
/// Architecture Constraints).
module Duelyst.Core.Rules

let BoardWidth = 9
let BoardHeight = 5

let MaxMana = 9
let StartingMana = 2

let MaxHandSize = 6
let StartingHandSize = 5

let MulliganReplaceCount = 2

/// Fixed movement range for every unit this milestone (research.md R1 -- BFS-resolved, not raw distance).
let MovementRange = 2

/// Fixed (not escalating) fatigue damage per empty-deck draw attempt (research.md R3).
let FatigueDamage = 1
