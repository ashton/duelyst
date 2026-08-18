/// The resolution pipeline: validate -> modifyForExecution -> apply -> triggers, behind the public
/// `step` entry point (Constitution III: no IO, no Raylib, no ambient Random -- every random draw
/// flows through the seeded Rng carried in GameState).
module Duelyst.Core.Pipeline

open Duelyst.Core.Types
open Duelyst.Core.Actions
open Duelyst.Core.GameState

/// Minimal in-core card stat lookup (Constitution III: Duelyst.Core must stay IO-free, so this
/// cannot read assets/cards.json -- that's Duelyst.Content's job in a later milestone; explicitly
/// permitted by contracts/core-pipeline.md's Non-goals: "a minimal in-core lookup"). Just enough
/// fixture entries for M1's scripted-match harness and tests; a real CardCatalog replaces this in
/// M2 without changing Action/step's shape.
module private CardStats =
    type Stats = { Cost: int; Atk: int; Hp: int }

    let private table: Map<CardId, Stats> =
        Map.ofList
            [ CardId 1, { Cost = 0; Atk = 2; Hp = 25 } // general fixture
              CardId 2, { Cost = 0; Atk = 2; Hp = 25 } // general fixture
              CardId 100, { Cost = 2; Atk = 2; Hp = 3 }
              CardId 101, { Cost = 1; Atk = 1; Hp = 1 }
              CardId 102, { Cost = 3; Atk = 3; Hp = 4 }
              CardId 103, { Cost = 4; Atk = 4; Hp = 5 } ]

    let tryFind (card: CardId) : Stats option = Map.tryFind card table

// ---- validate ----------------------------------------------------------------------------------

let private validatePlayCard (state: GameState) (player: PlayerId) (card: CardId) (target: Position) =
    if state.ActivePlayer <> player then
        Error NotYourTurn
    else
        match CardStats.tryFind card with
        | None -> Error(UnknownCard card)
        | Some stats ->
            let ps = state.Players.[player]

            if ps.Mana < stats.Cost then Error InsufficientMana
            elif not (Board.inBounds target) then Error(TileOutOfBounds target)
            elif Map.containsKey target state.Board then Error(TileOccupied target)
            elif not (Board.hasFriendlyAdjacent state player target) then Error(NoFriendlyAdjacent target)
            else Ok()

let private validateMoveUnit (state: GameState) (entityId: EntityId) (destination: Position) =
    match Map.tryFind entityId state.Entities with
    | None -> Error(UnknownEntity entityId)
    | Some e ->
        if state.ActivePlayer <> e.Owner then Error NotYourTurn
        elif e.HasMoved then Error(AlreadyMoved entityId)
        elif not (Board.inBounds destination) then Error(TileOutOfBounds destination)
        elif Map.containsKey destination state.Board then Error(TileOccupied destination)
        elif not (Board.isReachable state e.Position destination Rules.MovementRange) then
            Error(UnreachableWithinMovementRange(entityId, destination))
        else
            Ok()

let private validateAttack (state: GameState) (attackerId: EntityId) (defenderId: EntityId) =
    match Map.tryFind attackerId state.Entities, Map.tryFind defenderId state.Entities with
    | None, _ -> Error(UnknownEntity attackerId)
    | _, None -> Error(UnknownEntity defenderId)
    | Some attacker, Some defender ->
        if state.ActivePlayer <> attacker.Owner then Error NotYourTurn
        elif attacker.Exhausted then Error(AlreadyActed attackerId)
        elif attacker.SummonedThisTurn then Error(SummoningSickness attackerId)
        elif defender.Owner = attacker.Owner then Error(NotInAttackRange(attackerId, defenderId))
        elif not (Board.neighbors attacker.Position |> List.contains defender.Position) then
            Error(NotInAttackRange(attackerId, defenderId))
        else
            Ok()

let private validateMulligan (state: GameState) (player: PlayerId) (cardsToReplace: CardId list) =
    let ps = state.Players.[player]

    if List.length cardsToReplace > Rules.MulliganReplaceCount then
        Error(TooManyMulligans(List.length cardsToReplace, Rules.MulliganReplaceCount))
    else
        match cardsToReplace |> List.tryFind (fun c -> not (List.contains c ps.Hand)) with
        | Some badCard -> Error(UnknownCard badCard)
        | None -> Ok()

let private validateEndTurn (state: GameState) (player: PlayerId) =
    if state.ActivePlayer <> player then Error NotYourTurn else Ok()

let private isPlayerInitiated =
    function
    | PlayCard _
    | MoveUnit _
    | Attack _
    | Mulligan _
    | EndTurn _ -> true
    | _ -> false

/// Player-initiated actions are checked for real legality; system-derived actions (produced
/// internally by `apply`, never directly player-callable) get a trivial pass -- `apply` is never
/// invoked on a player-initiated action without this succeeding first (contracts/core-pipeline.md
/// Guarantee 2).
let validate (state: GameState) (action: Action) : Result<unit, InvalidReason> =
    if isPlayerInitiated action && state.Outcome <> InProgress then
        Error GameAlreadyEnded
    else
        match action with
        | PlayCard(player, card, target) -> validatePlayCard state player card target
        | MoveUnit(entity, destination) -> validateMoveUnit state entity destination
        | Attack(attacker, defender) -> validateAttack state attacker defender
        | Mulligan(player, cardsToReplace) -> validateMulligan state player cardsToReplace
        | EndTurn player -> validateEndTurn state player
        | Damage _
        | Heal _
        | Summon _
        | Kill _
        | ApplyModifier _
        | RemoveModifier _
        | DrawCard _
        | StartTurn _
        | Refresh _ -> Ok()

// ---- modifyForExecution -------------------------------------------------------------------------

/// Identity this milestone -- no modifiers alter actions yet (M2's Effect DSL changes this).
let modifyForExecution (_state: GameState) (action: Action) : Action = action

// ---- apply ---------------------------------------------------------------------------------------

/// `rec` because several cases resolve by recursively applying a lower-level system-derived action
/// (PlayCard -> Summon, Attack -> Damage -> Kill, EndTurn -> StartTurn -> Refresh/DrawCard) --
/// plain function calls within one `apply` invocation, not the triggers-driven follow-up queue
/// `step` drains (there is no other channel for one action's resolution to compose another this
/// milestone, since `triggers` always returns `[]`, per research.md R5).
let rec apply (state: GameState) (action: Action) : GameState * Event list =
    match action with
    | PlayCard(player, card, target) ->
        let stats = CardStats.tryFind card |> Option.get // validate guaranteed this exists
        let ps = state.Players.[player]
        let ps' = { ps with Mana = ps.Mana - stats.Cost }
        let stateAfterMana = { state with Players = Map.add player ps' state.Players }
        let manaEvent = ManaChanged(player, ps'.Mana, ps'.ManaCap)
        let stateAfterSummon, summonEvents = apply stateAfterMana (Summon(player, card, target))
        stateAfterSummon, manaEvent :: summonEvents

    | Summon(player, card, at) ->
        let stats = CardStats.tryFind card |> Option.get

        let nextIdValue =
            state.Entities
            |> Map.toList
            |> List.map (fun (EntityId i, _) -> i)
            |> function
                | [] -> 0
                | xs -> List.max xs + 1

        let entityId = EntityId nextIdValue

        let entity =
            { Id = entityId
              CardId = card
              Owner = player
              Position = at
              Atk = stats.Atk
              CurHp = stats.Hp
              MaxHp = stats.Hp
              Modifiers = []
              Exhausted = false
              HasMoved = false
              SummonedThisTurn = true }

        let state' =
            { state with
                Board = Map.add at entityId state.Board
                Entities = Map.add entityId entity state.Entities }

        state', [ UnitSummoned(entityId, card, player, at) ]

    | MoveUnit(entityId, destination) ->
        let e = state.Entities.[entityId]
        let board' = state.Board |> Map.remove e.Position |> Map.add destination entityId
        let e' = { e with Position = destination; HasMoved = true }
        let state' = { state with Board = board'; Entities = Map.add entityId e' state.Entities }
        state', [ UnitMoved(entityId, e.Position, destination) ]

    | Attack(attackerId, defenderId) ->
        let attacker = state.Entities.[attackerId]
        let exhausted = { attacker with Exhausted = true }
        let stateExhausted = { state with Entities = Map.add attackerId exhausted state.Entities }

        let stateAfterDamage, damageEvents =
            apply stateExhausted (Damage(defenderId, attacker.Atk, Some attackerId))

        let defenderSurvived =
            match Map.tryFind defenderId stateAfterDamage.Entities with
            | Some d -> d.CurHp > 0
            | None -> false

        if defenderSurvived then
            let defender = state.Entities.[defenderId]

            let stateAfterCounter, counterEvents =
                apply stateAfterDamage (Damage(attackerId, defender.Atk, Some defenderId))

            stateAfterCounter, damageEvents @ counterEvents
        else
            stateAfterDamage, damageEvents

    | Mulligan(player, cardsToReplace) ->
        let ps = state.Players.[player]
        let keptHand = ps.Hand |> List.filter (fun c -> not (List.contains c cardsToReplace))
        let deckWithReturned = ps.Deck @ cardsToReplace
        let shuffledDeck, rng' = Rng.shuffle state.Rng deckWithReturned
        let n = List.length cardsToReplace
        let newCards, restDeck = List.splitAt (min n (List.length shuffledDeck)) shuffledDeck
        let ps' = { ps with Hand = keptHand @ newCards; Deck = restDeck }
        let state' = { state with Players = Map.add player ps' state.Players; Rng = rng' }
        state', [ CardMulliganed(player, cardsToReplace) ]

    | EndTurn player ->
        let nextPlayer = if player = PlayerId 0 then PlayerId 1 else PlayerId 0
        let state', startEvents = apply state (StartTurn nextPlayer)
        state', TurnEnded player :: startEvents

    | Damage(targetId, amount, source) ->
        match Map.tryFind targetId state.Entities with
        | None -> state, [] // already gone (e.g. died earlier this resolution) -- no-op
        | Some target ->
            let newHp = max 0 (target.CurHp - amount)
            let updated = { target with CurHp = newHp }
            let state' = { state with Entities = Map.add targetId updated state.Entities }
            let damageEvent = DamageDealt(targetId, amount, source, newHp)

            if newHp = 0 then
                let stateAfterKill, killEvents = apply state' (Kill targetId)
                stateAfterKill, damageEvent :: killEvents
            else
                state', [ damageEvent ]

    | Heal(targetId, amount) ->
        match Map.tryFind targetId state.Entities with
        | None -> state, []
        | Some target ->
            let newHp = min target.MaxHp (target.CurHp + amount)
            let updated = { target with CurHp = newHp }
            let state' = { state with Entities = Map.add targetId updated state.Entities }
            state', [ UnitHealed(targetId, amount, newHp) ]

    | Kill targetId ->
        match Map.tryFind targetId state.Entities with
        | None -> state, []
        | Some target ->
            let ownerState = state.Players.[target.Owner]
            let ownerState' = { ownerState with Graveyard = target.CardId :: ownerState.Graveyard }

            let state' =
                { state with
                    Entities = Map.remove targetId state.Entities
                    Board = Map.remove target.Position state.Board
                    Players = Map.add target.Owner ownerState' state.Players }

            state', [ UnitDied targetId ]

    | ApplyModifier(targetId, modifier) ->
        match Map.tryFind targetId state.Entities with
        | None -> state, []
        | Some target ->
            let updated =
                { target with
                    Modifiers = modifier :: target.Modifiers
                    Atk = target.Atk + modifier.AtkDelta
                    CurHp = target.CurHp + modifier.HpDelta
                    MaxHp = target.MaxHp + modifier.HpDelta }

            let state' = { state with Entities = Map.add targetId updated state.Entities }
            state', [ ModifierApplied(targetId, modifier) ]

    | RemoveModifier(targetId, modifierId) ->
        match Map.tryFind targetId state.Entities with
        | None -> state, []
        | Some target ->
            match target.Modifiers |> List.tryFind (fun m -> m.Id = modifierId) with
            | None -> state, []
            | Some m ->
                let updated =
                    { target with
                        Modifiers = target.Modifiers |> List.filter (fun x -> x.Id <> modifierId)
                        Atk = target.Atk - m.AtkDelta
                        CurHp = target.CurHp - m.HpDelta
                        MaxHp = target.MaxHp - m.HpDelta }

                let state' = { state with Entities = Map.add targetId updated state.Entities }
                state', [ ModifierRemoved(targetId, modifierId) ]

    | DrawCard player ->
        let ps = state.Players.[player]

        match ps.Deck with
        | [] -> apply state (Damage(ps.GeneralId, Rules.FatigueDamage, None))
        | card :: restDeck ->
            if List.length ps.Hand >= Rules.MaxHandSize then
                let ps' = { ps with Deck = restDeck; Graveyard = card :: ps.Graveyard }
                let state' = { state with Players = Map.add player ps' state.Players }
                state', [ CardBurned(player, card) ]
            else
                let ps' = { ps with Deck = restDeck; Hand = ps.Hand @ [ card ] }
                let state' = { state with Players = Map.add player ps' state.Players }
                state', [ CardDrawn(player, card) ]

    | StartTurn player ->
        let newTurnNumber = state.TurnNumber + 1
        let state1 = { state with ActivePlayer = player; TurnNumber = newTurnNumber }
        let turnStartedEvent = TurnStarted(player, newTurnNumber)
        let state2, refreshEvents = apply state1 (Refresh player)
        let state3, drawEvents = apply state2 (DrawCard player)
        state3, turnStartedEvent :: (refreshEvents @ drawEvents)

    | Refresh player ->
        let ps = state.Players.[player]
        let newCap = min (ps.ManaCap + 1) Rules.MaxMana
        let ps' = { ps with Mana = newCap; ManaCap = newCap }

        let entities' =
            state.Entities
            |> Map.map (fun _ e ->
                if e.Owner = player then
                    { e with
                        Exhausted = false
                        HasMoved = false
                        SummonedThisTurn = false }
                else
                    e)

        let state' =
            { state with
                Players = Map.add player ps' state.Players
                Entities = entities' }

        state', [ ManaChanged(player, ps'.Mana, ps'.ManaCap) ]

// ---- triggers --------------------------------------------------------------------------------

/// Inert this milestone (research.md R5): no content registers with it yet, but `step`'s
/// follow-up-drain shape is exactly what M2's Effect DSL needs, so the seam is built now rather
/// than retrofitted (a breaking signature change) later.
let triggers (_state: GameState) (_events: Event list) : Action list = []

// ---- step ------------------------------------------------------------------------------------

let private checkOutcome (state: GameState) (events: Event list) : GameState * Event list =
    if state.Outcome <> InProgress then
        state, events
    else
        let generalAlive playerId =
            match Map.tryFind playerId state.Players with
            | Some ps -> Map.containsKey ps.GeneralId state.Entities
            | None -> false

        match generalAlive (PlayerId 0), generalAlive (PlayerId 1) with
        | true, true -> state, events
        | false, false -> { state with Outcome = Draw }, events @ [ MatchEnded Draw ]
        | true, false ->
            { state with Outcome = Win(PlayerId 0) }, events @ [ MatchEnded(Win(PlayerId 0)) ]
        | false, true ->
            { state with Outcome = Win(PlayerId 1) }, events @ [ MatchEnded(Win(PlayerId 1)) ]

/// The public entry point: validate -> modifyForExecution -> apply -> triggers, draining any
/// follow-up actions `triggers` enqueues (recursively, via `apply` -- not `step`, since follow-ups
/// are system-derived and skip `validate`) until the queue is empty, then checking both generals'
/// HP once and setting `Outcome` accordingly before returning (research.md R4).
let step (state: GameState) (action: Action) : Result<GameState * Event list, InvalidReason> =
    match validate state action with
    | Error e -> Error e
    | Ok() ->
        let action' = modifyForExecution state action
        let state1, events1 = apply state action'

        let rec drain (st: GameState) (pending: Action list) (accEvents: Event list) : GameState * Event list =
            match pending with
            | [] -> st, accEvents
            | next :: rest ->
                let st', evs' = apply st next
                let more = triggers st' evs'
                drain st' (rest @ more) (accEvents @ evs')

        let initialFollowUps = triggers state1 events1
        let finalState, allEvents = drain state1 initialFollowUps events1
        Ok(checkOutcome finalState allEvents)
