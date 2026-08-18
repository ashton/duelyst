/// Pure seeded PRNG (Constitution III: no ambient System.Random anywhere in the core).
/// splitmix64-based: cheap, well-distributed, and trivially threaded as immutable state.
module Duelyst.Core.Rng

type Rng = { Seed: uint64; State: uint64 }

let create (seed: uint64) : Rng = { Seed = seed; State = seed }

let private splitmix64Step (state: uint64) : uint64 * uint64 =
    let state' = state + 0x9E3779B97F4A7C15UL
    let mutable z = state'
    z <- (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
    z <- (z ^^^ (z >>> 27)) * 0x94D049BB133111EBUL
    z <- z ^^^ (z >>> 31)
    z, state'

/// Advances the Rng and returns a non-negative int alongside the new Rng state.
let next (rng: Rng) : int * Rng =
    let output, state' = splitmix64Step rng.State
    let value = int (output % uint64 System.Int32.MaxValue)
    value, { rng with State = state' }

/// Fisher-Yates shuffle, threading the Rng purely (no ambient randomness).
let shuffle (rng: Rng) (items: 'a list) : 'a list * Rng =
    let arr = List.toArray items
    let mutable rng' = rng

    for i = arr.Length - 1 downto 1 do
        let roll, r = next rng'
        rng' <- r
        let j = roll % (i + 1)
        let tmp = arr.[i]
        arr.[i] <- arr.[j]
        arr.[j] <- tmp

    List.ofArray arr, rng'
