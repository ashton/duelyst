module Duelyst.Core.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    let allTests =
        testList
            "Duelyst.Core"
            [ RngTests.tests
              GameStateTests.tests
              GameStateTests.turnCycleTests
              SummonTests.tests
              MoveTests.tests
              AttackTests.tests
              WinConditionTests.tests
              ScriptedMatchHarness.tests ]

    runTestsWithCLIArgs [] argv allTests
