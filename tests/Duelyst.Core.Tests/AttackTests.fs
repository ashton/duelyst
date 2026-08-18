module Duelyst.Core.Tests.AttackTests

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

/// Directly places a unit already free of summoning sickness (bypassing the pipeline, since the
/// test needs to attack the same turn it's "summoned" -- summoning sickness itself is covered by a
/// dedicated test below that goes through the real PlayCard pipeline).
let private placeReadyUnit (gs: GameState) (id: EntityId) (owner: PlayerId) (pos: Position) (atk: int) (hp: int) : GameState =
    let entity =
        { Id = id
          CardId = CardId 500
          Owner = owner
          Position = pos
          Atk = atk
          CurHp = hp
          MaxHp = hp
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
        "Attack + counterattack, exhaustion, summoning sickness"
        [ testCase "attacking deals damage to the defender and counterattack damage to the attacker"
          <| fun _ ->
              let gs0 = freshGame ()
              let attackerId = EntityId 900
              let defenderId = EntityId 901
              let gs1 = placeReadyUnit gs0 attackerId (PlayerId 0) { X = 4; Y = 2 } 3 10
              let gs2 = placeReadyUnit gs1 defenderId (PlayerId 1) { X = 5; Y = 2 } 2 10

              match step gs2 (Attack(attackerId, defenderId)) with
              | Error e -> failtestf "expected Ok, got Error %A" e
              | Ok(gs3, events) ->
                  Expect.equal gs3.Entities.[defenderId].CurHp 7 "defender should take 3 damage"
                  Expect.equal gs3.Entities.[attackerId].CurHp 8 "attacker should take 2 counterattack damage"
                  Expect.isTrue gs3.Entities.[attackerId].Exhausted "attacker should be exhausted after attacking"

                  let damageEvents =
                      events
                      |> List.choose (function
                          | DamageDealt(target, amount, _, _) -> Some(target, amount)
                          | _ -> None)

                  Expect.contains damageEvents (defenderId, 3) "expected DamageDealt to the defender"
                  Expect.contains damageEvents (attackerId, 2) "expected DamageDealt (counterattack) to the attacker"

          testCase "a defender destroyed by the attack does not counterattack"
          <| fun _ ->
              let gs0 = freshGame ()
              let attackerId = EntityId 902
              let defenderId = EntityId 903
              let gs1 = placeReadyUnit gs0 attackerId (PlayerId 0) { X = 4; Y = 2 } 10 10
              let gs2 = placeReadyUnit gs1 defenderId (PlayerId 1) { X = 5; Y = 2 } 2 1

              match step gs2 (Attack(attackerId, defenderId)) with
              | Error e -> failtestf "expected Ok, got Error %A" e
              | Ok(gs3, events) ->
                  Expect.isFalse (Map.containsKey defenderId gs3.Entities) "defender should be dead and removed"
                  Expect.equal gs3.Entities.[attackerId].CurHp 10 "attacker should take no counterattack damage"
                  Expect.exists events (function UnitDied id -> id = defenderId | _ -> false) "expected UnitDied for the defender"

          testCase "attacking twice with the same unit is rejected with AlreadyActed"
          <| fun _ ->
              let gs0 = freshGame ()
              let attackerId = EntityId 904
              let defenderId = EntityId 905
              let gs1 = placeReadyUnit gs0 attackerId (PlayerId 0) { X = 4; Y = 2 } 1 10
              let gs2 = placeReadyUnit gs1 defenderId (PlayerId 1) { X = 5; Y = 2 } 1 10

              match step gs2 (Attack(attackerId, defenderId)) with
              | Error e -> failtestf "expected Ok on first attack, got Error %A" e
              | Ok(gs3, _) ->
                  match step gs3 (Attack(attackerId, defenderId)) with
                  | Error(AlreadyActed id) -> Expect.equal id attackerId "should name the exhausted attacker"
                  | other -> failtestf "expected Error (AlreadyActed _), got %A" other

          testCase "attacking a non-adjacent unit is rejected with NotInAttackRange"
          <| fun _ ->
              let gs0 = freshGame ()
              let attackerId = EntityId 906
              let defenderId = EntityId 907
              let gs1 = placeReadyUnit gs0 attackerId (PlayerId 0) { X = 0; Y = 0 } 1 10
              let gs2 = placeReadyUnit gs1 defenderId (PlayerId 1) { X = 4; Y = 4 } 1 10

              match step gs2 (Attack(attackerId, defenderId)) with
              | Error(NotInAttackRange(a, d)) ->
                  Expect.equal a attackerId "should name the attacker"
                  Expect.equal d defenderId "should name the defender"
              | other -> failtestf "expected Error (NotInAttackRange _), got %A" other

          testCase "a unit summoned this turn cannot attack (summoning sickness)"
          <| fun _ ->
              let gs = freshGame ()
              let p1 = gs.Players.[PlayerId 0]
              let general1 = gs.Entities.[p1.GeneralId]
              let summonPos = { X = general1.Position.X + 1; Y = general1.Position.Y }

              match step gs (PlayCard(PlayerId 0, CardId 100, summonPos)) with
              | Error e -> failtestf "expected Ok summoning, got Error %A" e
              | Ok(gs', events) ->
                  let newUnitId =
                      events
                      |> List.pick (function
                          | UnitSummoned(id, _, _, _) -> Some id
                          | _ -> None)

                  let p2 = gs'.Players.[PlayerId 1]
                  let general2 = gs'.Entities.[p2.GeneralId]

                  match step gs' (Attack(newUnitId, general2.Id)) with
                  | Error(SummoningSickness id) -> Expect.equal id newUnitId "should name the freshly summoned unit"
                  | other -> failtestf "expected Error (SummoningSickness _), got %A" other ]
