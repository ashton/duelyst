module Duelyst.Core.Tests.MoveTests

open Expecto
open Duelyst.Core.Types
open Duelyst.Core.GameState
open Duelyst.Core.Actions
open Duelyst.Core.Pipeline
open Duelyst.Core.Board

let private sampleDeck (n: int) : CardId list = [ for i in 1..n -> CardId i ]

let private p1Setup deck : PlayerSetup =
    { GeneralCard = CardId 1
      GeneralAtk = 2
      GeneralHp = 25
      Deck = deck }

let private p2Setup deck : PlayerSetup =
    { GeneralCard = CardId 2
      GeneralAtk = 2
      GeneralHp = 25
      Deck = deck }

let private freshGame () =
    init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))

/// Directly places a dummy blocking entity on the board (bypassing the pipeline) to test BFS
/// obstruction -- isReachable is a pure query over GameState, no need to route through step.
let private withBlocker (gs: GameState) (pos: Position) (owner: PlayerId) : GameState =
    let id = EntityId(1000 + pos.X * 100 + pos.Y)

    let entity =
        { Id = id
          CardId = CardId 999
          Owner = owner
          Position = pos
          Atk = 1
          CurHp = 1
          MaxHp = 1
          Modifiers = []
          Exhausted = false
          HasMoved = false
          SummonedThisTurn = false }

    { gs with
        Board = Map.add pos id gs.Board
        Entities = Map.add id entity gs.Entities }

[<Tests>]
let tests =
    testList
        "isReachable / MoveUnit"
        [ testCase "a tile within range 2 through unoccupied tiles is reachable"
          <| fun _ ->
              let gs = freshGame ()
              let from_ = { X = 4; Y = 2 }
              let to_ = { X = 6; Y = 2 }
              Expect.isTrue (isReachable gs from_ to_ 2) "6,2 should be reachable from 4,2 within range 2"

          testCase "a tile beyond range 2 is not reachable"
          <| fun _ ->
              let gs = freshGame ()
              let from_ = { X = 4; Y = 2 }
              let to_ = { X = 7; Y = 2 }
              Expect.isFalse (isReachable gs from_ to_ 2) "7,2 is 3 steps from 4,2, beyond range 2"

          testCase "a blocked path is not reachable even within raw distance"
          <| fun _ ->
              let gs = freshGame ()
              let from_ = { X = 4; Y = 2 }
              let to_ = { X = 4; Y = 4 }
              // Block both orthogonal routes from (4,2) to (4,4) within range 2: going straight down
              // through (4,3), or detouring through (3,3)/(5,3) would exceed range 2 anyway, so
              // blocking (4,3) alone is enough to make (4,4) unreachable within range 2.
              let gsBlocked = withBlocker gs { X = 4; Y = 3 } (PlayerId 0)
              Expect.isFalse (isReachable gsBlocked from_ to_ 2) "path through the only route within range is blocked"

          testCase "the current tile is never reachable from itself"
          <| fun _ ->
              let gs = freshGame ()
              let pos = { X = 4; Y = 2 }
              Expect.isFalse (isReachable gs pos pos 2) "a unit's own tile isn't a legal move target"

          testCase "MoveUnit relocates the entity, sets HasMoved, and reports UnitMoved"
          <| fun _ ->
              let gs = freshGame ()
              let p1 = gs.Players.[PlayerId 0]
              let general = gs.Entities.[p1.GeneralId]
              let dest = { X = general.Position.X + 1; Y = general.Position.Y }

              match step gs (MoveUnit(general.Id, dest)) with
              | Error e -> failtestf "expected Ok, got Error %A" e
              | Ok(gs', events) ->
                  let moved = gs'.Entities.[general.Id]
                  Expect.equal moved.Position dest "entity should be at the destination"
                  Expect.isTrue moved.HasMoved "HasMoved should be set"
                  Expect.isFalse (Map.containsKey general.Position gs'.Board) "old tile should be vacated"
                  Expect.equal (Map.tryFind dest gs'.Board) (Some general.Id) "new tile should be occupied by the entity"

                  let hasMovedEvent =
                      events
                      |> List.exists (function
                          | UnitMoved(id, from_, to_) -> id = general.Id && from_ = general.Position && to_ = dest
                          | _ -> false)

                  Expect.isTrue hasMovedEvent "expected a UnitMoved event"

          testCase "moving the same unit twice in one turn is rejected with AlreadyMoved"
          <| fun _ ->
              let gs = freshGame ()
              let p1 = gs.Players.[PlayerId 0]
              let general = gs.Entities.[p1.GeneralId]
              let dest1 = { X = general.Position.X + 1; Y = general.Position.Y }
              let dest2 = { X = general.Position.X + 2; Y = general.Position.Y }

              match step gs (MoveUnit(general.Id, dest1)) with
              | Error e -> failtestf "expected Ok on first move, got Error %A" e
              | Ok(gs', _) ->
                  match step gs' (MoveUnit(general.Id, dest2)) with
                  | Error(AlreadyMoved id) -> Expect.equal id general.Id "should name the already-moved entity"
                  | other -> failtestf "expected Error (AlreadyMoved _), got %A" other

          testCase "moving onto an occupied tile is rejected with TileOccupied"
          <| fun _ ->
              let gs = freshGame ()
              let p1 = gs.Players.[PlayerId 0]
              let p2 = gs.Players.[PlayerId 1]
              let general1 = gs.Entities.[p1.GeneralId]
              let general2 = gs.Entities.[p2.GeneralId]

              match step gs (MoveUnit(general1.Id, general2.Position)) with
              | Error(TileOccupied pos) -> Expect.equal pos general2.Position "should name the occupied tile"
              | other -> failtestf "expected Error (TileOccupied _), got %A" other ]
