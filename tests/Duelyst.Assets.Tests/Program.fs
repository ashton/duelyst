module Duelyst.Assets.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    let allTests =
        testList
            "Duelyst.Assets"
            [ ManifestTests.tests
              ResolveTests.tests
              AtlasFrameTests.tests ]

    runTestsWithCLIArgs [] argv allTests
