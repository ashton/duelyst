module Duelyst.Core.Tests.RngTests

open Expecto
open Duelyst.Core.Rng

[<Tests>]
let tests =
    testList
        "Rng"
        [ testCase "next produces non-negative values"
          <| fun _ ->
              let v1, r1 = next (create 42UL)
              let v2, _ = next r1
              Expect.isGreaterThanOrEqual v1 0 "v1 should be non-negative"
              Expect.isGreaterThanOrEqual v2 0 "v2 should be non-negative"

          testCase "next is deterministic for the same seed"
          <| fun _ ->
              let a, _ = next (create 7UL)
              let b, _ = next (create 7UL)
              Expect.equal a b "same seed must produce the same first value"

          testCase "next is seed-sensitive"
          <| fun _ ->
              let a, _ = next (create 7UL)
              let b, _ = next (create 8UL)
              Expect.notEqual a b "different seeds should (almost certainly) diverge"

          testCase "next advances state (repeated calls diverge)"
          <| fun _ ->
              let r0 = create 42UL
              let v1, r1 = next r0
              let v2, _ = next r1
              Expect.notEqual v1 v2 "successive draws from the same Rng should differ"

          testCase "shuffle preserves the element multiset and length"
          <| fun _ ->
              let items = [ 1..10 ]
              let shuffled, _ = shuffle (create 1UL) items
              Expect.equal (List.length shuffled) (List.length items) "length must be preserved"
              Expect.equal (List.sort shuffled) (List.sort items) "elements must be preserved"

          testCase "shuffle is deterministic for the same seed"
          <| fun _ ->
              let items = [ 1..20 ]
              let a, _ = shuffle (create 99UL) items
              let b, _ = shuffle (create 99UL) items
              Expect.equal a b "same seed must produce the same shuffle order"

          testCase "shuffle of an empty list is empty"
          <| fun _ ->
              let shuffled, _ = shuffle (create 1UL) ([]: int list)
              Expect.isEmpty shuffled "shuffling [] must yield []" ]
