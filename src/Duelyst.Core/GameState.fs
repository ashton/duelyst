/// The complete, immutable state of a match at a point in time (Constitution I/III: illegal states
/// unrepresentable, no IO/Raylib/ambient randomness anywhere in this module).
module Duelyst.Core.GameState

open Duelyst.Core.Types
open Duelyst.Core.Actions

type Entity =
    { Id: EntityId
      CardId: CardId
      Owner: PlayerId
      Position: Position
      Atk: int
      CurHp: int
      MaxHp: int
      Modifiers: Modifier list
      Exhausted: bool // has attacked this turn
      HasMoved: bool // has moved this turn
      SummonedThisTurn: bool } // summoning sickness

type PlayerState =
    { Mana: int
      ManaCap: int
      Hand: CardId list
      Deck: CardId list
      Graveyard: CardId list
      GeneralId: EntityId }

type GameState =
    { Board: Map<Position, EntityId> // occupancy index -- no two entities share a tile
      Entities: Map<EntityId, Entity>
      Players: Map<PlayerId, PlayerState>
      ActivePlayer: PlayerId
      TurnNumber: int
      Rng: Rng.Rng
      Outcome: Outcome
      History: Action list } // append-only action log

/// One player's side of a fresh match: their general's card/stats, and their starting deck.
type PlayerSetup =
    { GeneralCard: CardId
      GeneralAtk: int
      GeneralHp: int
      Deck: CardId list }

/// Builds the initial GameState for a fresh match: 9x5 board, both generals placed on opposite ends
/// of the middle row, hands dealt from a seed-shuffled deck (StartingHandSize each), starting mana
/// per Rules constants. Does not perform mulligan -- that's a separate Action applied afterward via
/// step.
let init (seed: uint64) (player1: PlayerSetup) (player2: PlayerSetup) : GameState =
    let rng0 = Rng.create seed
    let deckP1, rng1 = Rng.shuffle rng0 player1.Deck
    let deckP2, rng2 = Rng.shuffle rng1 player2.Deck

    let handP1, restP1 = List.splitAt (min Rules.StartingHandSize (List.length deckP1)) deckP1
    let handP2, restP2 = List.splitAt (min Rules.StartingHandSize (List.length deckP2)) deckP2

    let general1Id = EntityId 0
    let general2Id = EntityId 1
    let general1Pos = { X = 0; Y = Rules.BoardHeight / 2 }
    let general2Pos = { X = Rules.BoardWidth - 1; Y = Rules.BoardHeight / 2 }

    let mkGeneral id owner card atk hp pos =
        { Id = id
          CardId = card
          Owner = owner
          Position = pos
          Atk = atk
          CurHp = hp
          MaxHp = hp
          Modifiers = []
          Exhausted = false
          HasMoved = false
          SummonedThisTurn = false }

    let general1 =
        mkGeneral general1Id (PlayerId 0) player1.GeneralCard player1.GeneralAtk player1.GeneralHp general1Pos

    let general2 =
        mkGeneral general2Id (PlayerId 1) player2.GeneralCard player2.GeneralAtk player2.GeneralHp general2Pos

    let mkPlayerState hand deck generalId =
        { Mana = Rules.StartingMana
          ManaCap = Rules.StartingMana
          Hand = hand
          Deck = deck
          Graveyard = []
          GeneralId = generalId }

    { Board = Map.ofList [ general1Pos, general1Id; general2Pos, general2Id ]
      Entities = Map.ofList [ general1Id, general1; general2Id, general2 ]
      Players =
        Map.ofList
            [ PlayerId 0, mkPlayerState handP1 restP1 general1Id
              PlayerId 1, mkPlayerState handP2 restP2 general2Id ]
      ActivePlayer = PlayerId 0
      TurnNumber = 1
      Rng = rng2
      Outcome = InProgress
      History = [] }
