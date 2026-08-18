module Duelyst.Core.Tests.GameStateTests

open Expecto
open Duelyst.Core.Types
open Duelyst.Core.GameState
open Duelyst.Core.Actions
open Duelyst.Core.Pipeline

let private sampleDeck (n: int) : CardId list = [ for i in 1..n -> CardId i ]

let private p1Setup deck : PlayerSetup =
    { GeneralCard = CardId 1001
      GeneralAtk = 2
      GeneralHp = 25
      Deck = deck }

let private p2Setup deck : PlayerSetup =
    { GeneralCard = CardId 1002
      GeneralAtk = 2
      GeneralHp = 25
      Deck = deck }

[<Tests>]
let tests =
    testList
        "GameState.init"
        [ testCase "places both generals on the board at opposite ends"
          <| fun _ ->
              let gs = init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))
              Expect.equal (Map.count gs.Board) 2 "board should have exactly 2 occupied tiles"
              Expect.equal (Map.count gs.Entities) 2 "should have exactly 2 entities (the generals)"

          testCase "each player's general is owned correctly and linked from PlayerState"
          <| fun _ ->
              let gs = init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))
              let p1 = gs.Players.[PlayerId 0]
              let p2 = gs.Players.[PlayerId 1]
              let g1 = gs.Entities.[p1.GeneralId]
              let g2 = gs.Entities.[p2.GeneralId]
              Expect.equal g1.Owner (PlayerId 0) "player 0's general should be owned by player 0"
              Expect.equal g2.Owner (PlayerId 1) "player 1's general should be owned by player 1"
              Expect.notEqual g1.Id g2.Id "generals must have distinct entity ids"
              Expect.equal g1.CurHp g1.MaxHp "a fresh general should be at full health"

          testCase "starting mana and mana cap match Rules.StartingMana for both players"
          <| fun _ ->
              let gs = init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))

              for kv in gs.Players do
                  Expect.equal kv.Value.Mana Duelyst.Core.Rules.StartingMana "mana should start at StartingMana"
                  Expect.equal kv.Value.ManaCap Duelyst.Core.Rules.StartingMana "manaCap should start at StartingMana"

          testCase "starting hand size matches Rules.StartingHandSize when the deck is large enough"
          <| fun _ ->
              let gs = init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))

              for kv in gs.Players do
                  Expect.equal
                      (List.length kv.Value.Hand)
                      Duelyst.Core.Rules.StartingHandSize
                      "hand should be dealt StartingHandSize cards"

                  Expect.equal
                      (List.length kv.Value.Hand + List.length kv.Value.Deck)
                      20
                      "no cards should be lost or duplicated between hand and deck"

          testCase "match starts in progress, turn 1, player 0 active, empty history"
          <| fun _ ->
              let gs = init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))
              Expect.equal gs.Outcome InProgress "match should start InProgress"
              Expect.equal gs.TurnNumber 1 "match should start on turn 1"
              Expect.equal gs.ActivePlayer (PlayerId 0) "player 0 should be active first"
              Expect.isEmpty gs.History "history should start empty"

          testCase "init is deterministic for the same seed"
          <| fun _ ->
              let gsA = init 42UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))
              let gsB = init 42UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))
              let handsA = gsA.Players |> Map.map (fun _ p -> p.Hand)
              let handsB = gsB.Players |> Map.map (fun _ p -> p.Hand)
              Expect.equal handsA handsB "same seed should deal identical starting hands"

          testCase "init is seed-sensitive (different seeds shuffle differently)"
          <| fun _ ->
              let gsA = init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))
              let gsB = init 2UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))
              let handA = gsA.Players.[PlayerId 0].Hand
              let handB = gsB.Players.[PlayerId 0].Hand
              Expect.notEqual handA handB "different seeds should (almost certainly) deal different hands" ]

[<Tests>]
let turnCycleTests =
    testList
        "Turn cycle (mana ramp via StartTurn/Refresh, mulligan)"
        [ testCase "ending a turn ramps the next player's mana cap by 1 and refills mana to cap"
          <| fun _ ->
              let gs = init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))

              match step gs (EndTurn(PlayerId 0)) with
              | Error e -> failtestf "expected Ok, got Error %A" e
              | Ok(gs', _) ->
                  let p1 = gs'.Players.[PlayerId 1]
                  Expect.equal p1.ManaCap (Duelyst.Core.Rules.StartingMana + 1) "manaCap should ramp by 1"
                  Expect.equal p1.Mana p1.ManaCap "mana should refill to the new cap"

          testCase "mana cap never exceeds Rules.MaxMana even after many turns"
          <| fun _ ->
              let gs0 = init 1UL (p1Setup (sampleDeck 40)) (p2Setup (sampleDeck 40))

              let rec playTurns (gs: GameState) (n: int) : GameState =
                  if n = 0 then
                      gs
                  else
                      match step gs (EndTurn gs.ActivePlayer) with
                      | Ok(gs', _) -> playTurns gs' (n - 1)
                      | Error e -> failtestf "unexpected Error %A on turn %d" e n

              let gsFinal = playTurns gs0 30

              for kv in gsFinal.Players do
                  Expect.isLessThanOrEqual kv.Value.ManaCap Duelyst.Core.Rules.MaxMana "manaCap must not exceed MaxMana"

          testCase "mulligan replaces up to MulliganReplaceCount cards, keeping hand size the same"
          <| fun _ ->
              let gs = init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))
              let p1 = gs.Players.[PlayerId 0]
              let toReplace = p1.Hand |> List.truncate Duelyst.Core.Rules.MulliganReplaceCount

              match step gs (Mulligan(PlayerId 0, toReplace)) with
              | Error e -> failtestf "expected Ok, got Error %A" e
              | Ok(gs', _) ->
                  let p1' = gs'.Players.[PlayerId 0]
                  Expect.equal (List.length p1'.Hand) (List.length p1.Hand) "hand size should stay the same after mulligan"

          testCase "mulligan rejects replacing more than MulliganReplaceCount cards"
          <| fun _ ->
              let gs = init 1UL (p1Setup (sampleDeck 20)) (p2Setup (sampleDeck 20))
              let p1 = gs.Players.[PlayerId 0]
              let tooMany = p1.Hand |> List.truncate (Duelyst.Core.Rules.MulliganReplaceCount + 1)

              match step gs (Mulligan(PlayerId 0, tooMany)) with
              | Error(TooManyMulligans(requested, allowed)) ->
                  Expect.equal requested (List.length tooMany) "requested count should match"
                  Expect.equal allowed Duelyst.Core.Rules.MulliganReplaceCount "allowed count should match Rules"
              | other -> failtestf "expected Error TooManyMulligans, got %A" other ]
