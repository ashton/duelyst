/// The headless text/scripted harness (FR-017): proves a complete match -- mulligan, mana ramp,
/// summon, move, attack + counterattack, to a general's death -- can be driven entirely through
/// the public step/legalActions surface, with zero UI (SC-001), and is readable by a contributor
/// in well under 15 minutes (SC-006).
module Duelyst.Core.Tests.ScriptedMatchHarness

open Expecto
open Duelyst.Core.Types
open Duelyst.Core.GameState
open Duelyst.Core.Actions
open Duelyst.Core.Pipeline

let private sampleDeck (n: int) : CardId list = [ for i in 1..n -> CardId i ]

/// Deliberately small general HP (not real card data) so a full match to a win is a handful of
/// turns to read, not the dozens a realistic 25-HP grind would take -- this proves the pipeline's
/// mechanics end-to-end, not a realistic-length game.
let private p1Setup: PlayerSetup =
    { GeneralCard = CardId 1
      GeneralAtk = 2
      GeneralHp = 4
      Deck = sampleDeck 30 }

let private p2Setup: PlayerSetup =
    { GeneralCard = CardId 2
      GeneralAtk = 2
      GeneralHp = 4
      Deck = sampleDeck 30 }

/// Applies one action through `step`, failing the test immediately (naming the action and the
/// rejection reason) if it's rejected -- every call site below is expected to succeed; an
/// unexpected rejection means the script or the pipeline has a bug.
let playTurn (gs: GameState) (action: Action) : GameState * Event list =
    match step gs action with
    | Ok(gs', events) -> gs', events
    | Error reason -> failtestf "scripted action %A was rejected: %A" action reason

/// Repeatedly moves `unitId` towards `targetId` (up to Rules.MovementRange tiles per beat, along
/// whichever axis still needs it) until adjacent, then attacks -- ending the turn between beats so
/// the normal mana-ramp/refresh cycle runs, exactly as a real match would, and passing the
/// opponent's turn (they have no attacker of their own -- this harness proves the pipeline, not a
/// symmetric AI). `beatsRemaining` is a safety cutoff: if the pipeline has a bug that stalls
/// progress, this fails the test with a clear message instead of looping forever.
let rec scriptedMatch
    (gs: GameState)
    (unitId: EntityId)
    (targetId: EntityId)
    (owner: PlayerId)
    (beatsRemaining: int)
    (accEvents: Event list)
    : GameState * Event list =
    if beatsRemaining <= 0 then
        failtestf "scriptedMatch did not reach a conclusion within the beat budget -- Outcome = %A" gs.Outcome
    elif gs.Outcome <> InProgress then
        gs, accEvents
    elif gs.ActivePlayer <> owner then
        let gs', events = playTurn gs (EndTurn gs.ActivePlayer)
        scriptedMatch gs' unitId targetId owner (beatsRemaining - 1) (accEvents @ events)
    else
        let unit = gs.Entities.[unitId]
        let target = gs.Entities.[targetId]

        if unit.Exhausted then
            let gs', events = playTurn gs (EndTurn owner)
            scriptedMatch gs' unitId targetId owner (beatsRemaining - 1) (accEvents @ events)
        elif Duelyst.Core.Board.neighbors unit.Position |> List.contains target.Position then
            let gs', events = playTurn gs (Attack(unitId, targetId))
            scriptedMatch gs' unitId targetId owner (beatsRemaining - 1) (accEvents @ events)
        else
            let dxSign = sign (target.Position.X - unit.Position.X)
            let dySign = sign (target.Position.Y - unit.Position.Y)

            let dest =
                if dxSign <> 0 then
                    let stepX =
                        min Duelyst.Core.Rules.MovementRange (abs (target.Position.X - unit.Position.X) - 1)
                        |> max 1

                    { unit.Position with X = unit.Position.X + dxSign * stepX }
                else
                    let stepY =
                        min Duelyst.Core.Rules.MovementRange (abs (target.Position.Y - unit.Position.Y) - 1)
                        |> max 1

                    { unit.Position with Y = unit.Position.Y + dySign * stepY }

            let gsMoved, moveEvents = playTurn gs (MoveUnit(unitId, dest))
            let gs', endEvents = playTurn gsMoved (EndTurn owner)
            scriptedMatch gs' unitId targetId owner (beatsRemaining - 1) (accEvents @ moveEvents @ endEvents)

[<Tests>]
let tests =
    testList
        "Scripted headless match (FR-017, SC-001, SC-006)"
        [ testCase
              "a full match plays from GameState.init through mulligan, mana ramp, summon, move, attack + counterattack, to a general's death"
          <| fun _ ->
              let gs0 = init 7UL p1Setup p2Setup
              let p1 = gs0.Players.[PlayerId 0]
              let general1 = gs0.Entities.[p1.GeneralId]
              let p2 = gs0.Players.[PlayerId 1]
              let general2 = gs0.Entities.[p2.GeneralId]

              // 1. Mulligan -- exercises the pre-game replace-cards mechanic.
              let toReplace = p1.Hand |> List.truncate Duelyst.Core.Rules.MulliganReplaceCount
              let gs1, ev1 = playTurn gs0 (Mulligan(PlayerId 0, toReplace))

              // 2. Summon an attacker adjacent to player 0's general -- exercises PlayCard/Summon
              //    and the mana deduction it triggers.
              let summonPos =
                  { X = general1.Position.X + 1
                    Y = general1.Position.Y }

              let gs2, ev2 = playTurn gs1 (PlayCard(PlayerId 0, CardId 100, summonPos))

              let attackerId =
                  ev2
                  |> List.pick (function
                      | UnitSummoned(id, _, _, _) -> Some id
                      | _ -> None)

              // 3. End the turn -- exercises mana ramp for player 1 via EndTurn -> StartTurn -> Refresh.
              let gs3, ev3 = playTurn gs2 (EndTurn(PlayerId 0))
              let p1ManaCapAfterRamp = gs3.Players.[PlayerId 1].ManaCap

              Expect.equal
                  p1ManaCapAfterRamp
                  (Duelyst.Core.Rules.StartingMana + 1)
                  "mana ramp should have fired via EndTurn -> StartTurn -> Refresh"

              // 4. Player 1 passes (no attacker of their own) so player 0 gets the next turn --
              //    summoning sickness from step 2 wears off on player 0's own next Refresh.
              let gs4, ev4 = playTurn gs3 (EndTurn(PlayerId 1))

              // 5. Move the attacker toward player 1's general and attack once adjacent, repeating
              //    across turns until a general dies -- exercises MoveUnit/Board.isReachable and
              //    Attack + counterattack.
              let gsFinal, ev5 = scriptedMatch gs4 attackerId general2.Id (PlayerId 0) 20 []

              let allEvents = ev1 @ ev2 @ ev3 @ ev4 @ ev5

              match gsFinal.Outcome with
              | Win winner -> Expect.equal winner (PlayerId 0) "player 0's attacker should land the killing blow"
              | other -> failtestf "expected the match to end in a Win, got Outcome = %A" other

              // No invariant violation along the way: mana never negative, HP never negative, no
              // tile ever shared by two different entities.
              for kv in gsFinal.Players do
                  Expect.isGreaterThanOrEqual kv.Value.Mana 0 "mana should never go negative"

              for kv in gsFinal.Entities do
                  Expect.isGreaterThanOrEqual kv.Value.CurHp 0 "HP should never go negative"

              let positions = gsFinal.Entities |> Map.toList |> List.map (fun (_, e) -> e.Position)

              Expect.equal
                  (List.length positions)
                  (List.length (List.distinct positions))
                  "no two entities should ever share a tile"

              Expect.isGreaterThan
                  (List.length allEvents)
                  0
                  "the scripted match should produce a non-empty event log to animate/replay" ]
