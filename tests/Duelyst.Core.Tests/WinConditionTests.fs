module Duelyst.Core.Tests.WinConditionTests

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
        "General-death win/draw, post-game rejection, fatigue/burn"
        [ testCase "a general's death ends the match with a win for the opponent"
          <| fun _ ->
              let gs = freshGame ()
              let p2 = gs.Players.[PlayerId 1]
              let general2 = gs.Entities.[p2.GeneralId]

              // step accepts a system-derived action directly (validate gives it a trivial pass) and
              // still runs the full pipeline including the post-resolution Outcome check.
              match step gs (Damage(general2.Id, general2.CurHp, None)) with
              | Error e -> failtestf "expected Ok, got Error %A" e
              | Ok(gs', events) ->
                  Expect.equal gs'.Outcome (Win(PlayerId 0)) "player 0 should win when player 1's general dies"

                  Expect.exists
                      events
                      (function
                      | MatchEnded(Win(PlayerId 0)) -> true
                      | _ -> false)
                      "expected MatchEnded (Win player 0)"

          testCase "both generals dead in the same resolution ends the match in a draw"
          <| fun _ ->
              let gs = freshGame ()
              let p1 = gs.Players.[PlayerId 0]
              let p2 = gs.Players.[PlayerId 1]
              let general1 = gs.Entities.[p1.GeneralId]
              let general2 = gs.Entities.[p2.GeneralId]

              // Simulate "both generals died in the same resolution" directly -- reachable in a
              // future milestone via a mutual-damage effect, not by any M1 player action
              // (research.md R4) -- by removing both entities and letting the next step's outcome
              // check observe it.
              let gsBothDead =
                  { gs with
                      Entities = gs.Entities |> Map.remove general1.Id |> Map.remove general2.Id
                      Board = gs.Board |> Map.remove general1.Position |> Map.remove general2.Position }

              match step gsBothDead (EndTurn(PlayerId 0)) with
              | Error e -> failtestf "expected Ok, got Error %A" e
              | Ok(gs', events) ->
                  Expect.equal gs'.Outcome Draw "both generals dead should resolve to a draw"
                  Expect.exists events (function MatchEnded Draw -> true | _ -> false) "expected MatchEnded Draw"

          testCase "once a match has ended, further player-initiated actions are rejected with GameAlreadyEnded"
          <| fun _ ->
              let gs = freshGame ()
              let p2 = gs.Players.[PlayerId 1]
              let general2 = gs.Entities.[p2.GeneralId]

              match step gs (Damage(general2.Id, general2.CurHp, None)) with
              | Error e -> failtestf "expected Ok ending the match, got Error %A" e
              | Ok(gsEnded, _) ->
                  match step gsEnded (EndTurn gsEnded.ActivePlayer) with
                  | Error GameAlreadyEnded -> ()
                  | other -> failtestf "expected Error GameAlreadyEnded, got %A" other

          testCase "drawing from an empty deck deals fatigue damage to the general instead of drawing"
          <| fun _ ->
              let gs0 = init 1UL (p1Setup []) (p2Setup (sampleDeck 20))
              let p1 = gs0.Players.[PlayerId 0]
              let general1 = gs0.Entities.[p1.GeneralId]
              let hpBefore = general1.CurHp

              let gs', events = apply gs0 (DrawCard(PlayerId 0))

              Expect.equal
                  gs'.Entities.[p1.GeneralId].CurHp
                  (hpBefore - Duelyst.Core.Rules.FatigueDamage)
                  "general should take fatigue damage"

              Expect.exists
                  events
                  (function
                  | DamageDealt(target, _, _, _) -> target = p1.GeneralId
                  | _ -> false)
                  "expected DamageDealt to the general"

          testCase "drawing at max hand size burns the drawn card instead of growing the hand"
          <| fun _ ->
              let bigDeck = sampleDeck 20
              let gs0 = init 1UL (p1Setup bigDeck) (p2Setup (sampleDeck 20))

              let rec fillHand (gs: GameState) : GameState =
                  let ps = gs.Players.[PlayerId 0]

                  if List.length ps.Hand >= Duelyst.Core.Rules.MaxHandSize then
                      gs
                  else
                      let gs', _ = apply gs (DrawCard(PlayerId 0))
                      fillHand gs'

              let gsFull = fillHand gs0
              let psFull = gsFull.Players.[PlayerId 0]
              Expect.equal (List.length psFull.Hand) Duelyst.Core.Rules.MaxHandSize "hand should be at max size"

              let gs', events = apply gsFull (DrawCard(PlayerId 0))
              let psAfter = gs'.Players.[PlayerId 0]

              Expect.equal
                  (List.length psAfter.Hand)
                  Duelyst.Core.Rules.MaxHandSize
                  "hand size should not grow past MaxHandSize"

              Expect.exists
                  events
                  (function
                  | CardBurned(player, _) -> player = PlayerId 0
                  | _ -> false)
                  "expected CardBurned" ]
