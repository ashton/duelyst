# Quickstart: Validating the Headless Core Rules Engine

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) (already required by M0). No git-LFS,
no Node, no display — this milestone is a pure library + test suite.

```bash
dotnet build Duelyst.sln
dotnet test tests/Duelyst.Core.Tests
```

## Scenarios

1. **Build succeeds** (SC-005 precondition): `dotnet build Duelyst.sln` succeeds with `Duelyst.Core` no
   longer an empty stub, and it references no Raylib/IO package — confirm via
   `dotnet list src/Duelyst.Core/Duelyst.Core.fsproj package` showing no Raylib-cs entry.

2. **Full test suite green**: `dotnet test tests/Duelyst.Core.Tests` — all example tests and FsCheck
   property tests pass.

3. **Scripted full match (US1/SC-001)**: run the `ScriptedMatchHarness` test — it plays a complete headless
   match from `GameState.init` through mulligan, several turns of mana ramp/summon/move/attack, to a
   general's death, asserting the final `Outcome` is `Win _` and that no invariant violation occurred along
   the way. Read `tests/Duelyst.Core.Tests/ScriptedMatchHarness.fs` top-to-bottom to follow the match's
   turn-by-turn outcome (SC-006: should take a new contributor under 15 minutes).

4. **legalActions consistency (US2/SC-002)**: run `LegalActionsTests` — for a battery of mid-match states,
   every action `legalActions` returns is confirmed accepted by `step`, and at least one known-illegal
   action per state is confirmed absent from the list.

5. **Determinism (US3/SC-003)**: run `DeterminismTests` — the same seed + action list replayed twice
   produces identical event lists; two different seeds with the same action list produce diverging
   randomness-dependent outcomes (confirming the seed is actually load-bearing, not ignored).

6. **Invariant properties (SC-004)**: run `InvariantPropertyTests` — FsCheck generates randomized legal
   action sequences and asserts mana never goes negative, HP never goes below 0, no two entities ever share
   a board tile, and no entity acts (moves or attacks) twice in one turn, across all generated cases.

7. **Edge cases**: dedicated test cases (in the relevant `*Tests.fs` file per rule) cover: drawing from an
   empty deck (fatigue damage, R3), drawing at max hand size (burn, R3), illegal summon targets (occupied /
   out of bounds / no friendly adjacent), illegal move targets (occupied / out of bounds / unreachable
   within range), acting twice with one unit, a simultaneous double general-kill (draw, R4), any action
   attempted after the match has ended, and over-mulligan.

## Expected outcome

All of the above are exercised by `dotnet test tests/Duelyst.Core.Tests` — a single green test run is the
end-to-end proof this milestone works, since there is no UI/client component to visually inspect (that's
M3). No manual/visual verification step is required or possible this milestone.
