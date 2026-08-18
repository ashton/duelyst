module Duelyst.Core.Tests.SummonTests

open Expecto
open Duelyst.Core.Types
open Duelyst.Core.GameState
open Duelyst.Core.Actions
open Duelyst.Core.Pipeline

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

[<Tests>]
let tests =
    testList
        "Summon-near-friendly legality"
        [ testCase "summoning adjacent to a friendly general is accepted and reports UnitSummoned"
          <| fun _ ->
              let gs = freshGame ()
              let p1 = gs.Players.[PlayerId 0]
              let general = gs.Entities.[p1.GeneralId]
              let target = { X = general.Position.X + 1; Y = general.Position.Y }

              match step gs (PlayCard(PlayerId 0, CardId 100, target)) with
              | Error e -> failtestf "expected Ok, got Error %A" e
              | Ok(_, events) ->
                  let summoned =
                      events
                      |> List.exists (function
                          | UnitSummoned(_, CardId 100, PlayerId 0, pos) -> pos = target
                          | _ -> false)

                  Expect.isTrue summoned "expected a UnitSummoned event at the target position"

          testCase "summoning onto an occupied tile is rejected with TileOccupied"
          <| fun _ ->
              let gs = freshGame ()
              let p1 = gs.Players.[PlayerId 0]
              let general = gs.Entities.[p1.GeneralId]

              match step gs (PlayCard(PlayerId 0, CardId 100, general.Position)) with
              | Error(TileOccupied pos) -> Expect.equal pos general.Position "should name the occupied tile"
              | other -> failtestf "expected Error (TileOccupied _), got %A" other

          testCase "summoning out of bounds is rejected with TileOutOfBounds"
          <| fun _ ->
              let gs = freshGame ()
              let outOfBounds = { X = -1; Y = 0 }

              match step gs (PlayCard(PlayerId 0, CardId 100, outOfBounds)) with
              | Error(TileOutOfBounds pos) -> Expect.equal pos outOfBounds "should name the out-of-bounds tile"
              | other -> failtestf "expected Error (TileOutOfBounds _), got %A" other

          testCase "summoning with no friendly unit adjacent is rejected with NoFriendlyAdjacent"
          <| fun _ ->
              let gs = freshGame ()
              // Far from both generals (placed at opposite ends of the middle row) and unoccupied.
              let isolated = { X = 4; Y = 0 }

              match step gs (PlayCard(PlayerId 0, CardId 100, isolated)) with
              | Error(NoFriendlyAdjacent pos) -> Expect.equal pos isolated "should name the isolated tile"
              | other -> failtestf "expected Error (NoFriendlyAdjacent _), got %A" other

          testCase "summoning without enough mana is rejected with InsufficientMana"
          <| fun _ ->
              let gs = freshGame ()
              let p1 = gs.Players.[PlayerId 0]
              let general = gs.Entities.[p1.GeneralId]
              let target = { X = general.Position.X + 1; Y = general.Position.Y }
              // CardId 103 costs 4 in the fixture table; fresh game only has StartingMana (2).
              match step gs (PlayCard(PlayerId 0, CardId 103, target)) with
              | Error InsufficientMana -> ()
              | other -> failtestf "expected Error InsufficientMana, got %A" other ]
