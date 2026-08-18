/// Board queries: adjacency (summon-near-friendly, attack range) and BFS movement reachability
/// (research.md R1 -- pathing-based, not raw distance, so a boxed-in unit has fewer reachable
/// tiles than distance alone would suggest).
module Duelyst.Core.Board

open Duelyst.Core.Types
open Duelyst.Core.GameState

let inBounds (pos: Position) : bool =
    pos.X >= 0 && pos.X < Rules.BoardWidth && pos.Y >= 0 && pos.Y < Rules.BoardHeight

/// The (up to) 8 orthogonal+diagonal in-bounds neighbors -- used for summon-near-friendly adjacency
/// and attack range (research.md R2: melee, Chebyshev distance 1).
let neighbors (pos: Position) : Position list =
    [ for dx in -1..1 do
          for dy in -1..1 do
              if not (dx = 0 && dy = 0) then
                  yield { X = pos.X + dx; Y = pos.Y + dy } ]
    |> List.filter inBounds

let private orthogonalNeighbors (pos: Position) : Position list =
    [ { pos with X = pos.X - 1 }
      { pos with X = pos.X + 1 }
      { pos with Y = pos.Y - 1 }
      { pos with Y = pos.Y + 1 } ]
    |> List.filter inBounds

let hasFriendlyAdjacent (state: GameState) (player: PlayerId) (pos: Position) : bool =
    neighbors pos
    |> List.exists (fun n ->
        match Map.tryFind n state.Board with
        | Some eid ->
            match Map.tryFind eid state.Entities with
            | Some e -> e.Owner = player
            | None -> false
        | None -> false)

/// BFS over orthogonally-adjacent, unoccupied, in-bounds tiles, capped at `range` steps. The
/// destination itself is always treated as passable regardless of occupancy -- whether it's
/// actually free is a separate concern (validate's TileOccupied check); this only answers "is
/// there a path of length <= range through unobstructed tiles".
let isReachable (state: GameState) (from_: Position) (to_: Position) (range: int) : bool =
    if from_ = to_ then
        false
    else
        let isOccupied pos = Map.containsKey pos state.Board
        let isPassable pos = pos = to_ || not (isOccupied pos)

        let rec bfs (visited: Set<Position>) (queue: (Position * int) list) : bool =
            match queue with
            | [] -> false
            | (pos, dist) :: rest ->
                if pos = to_ then
                    true
                elif dist >= range then
                    bfs visited rest
                else
                    let next =
                        orthogonalNeighbors pos
                        |> List.filter (fun p -> isPassable p && not (Set.contains p visited))

                    let visited' = next |> List.fold (fun acc p -> Set.add p acc) visited
                    bfs visited' (rest @ (next |> List.map (fun p -> p, dist + 1)))

        bfs (Set.singleton from_) [ from_, 0 ]
