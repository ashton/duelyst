module Duelyst.Core.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    let allTests = testList "Duelyst.Core" [ RngTests.tests ]

    runTestsWithCLIArgs [] argv allTests
