module Duelyst.Assets.Tests.AtlasFrameTests

open Expecto
open FsCheck
open FsCheck.FSharp
open Duelyst.Assets.AtlasManifest
open Duelyst.Assets.Manifest
open Duelyst.Assets.SpriteAnimator

let private mkFrame name w h rotated : Frame =
    { Name = name
      X = 0
      Y = 0
      W = w
      H = h
      Rotated = rotated
      OffsetX = 0
      OffsetY = 0
      SrcW = w
      SrcH = h }

let private sampleCount = 100

/// FsCheck's `Gen` combinators generate the samples; we drive them through Expecto's `testCase`
/// (Expecto 11 dropped the built-in `testProperty` combinator) and assert manually.
let private checkAll (samples: 'a[]) (describe: 'a -> string) (prop: 'a -> bool) =
    samples
    |> Array.iter (fun s -> if not (prop s) then failtestf "property failed for sample: %s" (describe s))

[<Tests>]
let tests =
    testList
        "AtlasFrameTests"
        [ testCase "rotated sprite frames swap w/h in the resolved source rect"
          <| fun () ->
              let gen: Gen<int * int * bool> =
                  Gen.map3
                      (fun w h rotated -> (w, h, rotated))
                      (Gen.choose (1, 200))
                      (Gen.choose (1, 200))
                      (Gen.elements [ true; false ])

              let samples = Gen.sample sampleCount gen

              checkAll samples (fun (w, h, r) -> sprintf "w=%d h=%d rotated=%b" w h r) (fun (w, h, rotated) ->
                  let f = mkFrame "f.png" w h rotated

                  let atlases =
                      Map.ofList [ "atlas.png", { Image = "atlas.png"; Frames = Map.ofList [ "f.png", f ] } ]

                  let resources: ResourcesManifest = Map.ofList [ "alias", Sprite("atlas.png", "f.png") ]

                  match resolve resources atlases "alias" with
                  | Ok sprite ->
                      if rotated then
                          sprite.Source.W = float h && sprite.Source.H = float w
                      else
                          sprite.Source.W = float w && sprite.Source.H = float h
                  | Error _ -> false)

          testCase "animation frames resolve in ascending numeric order regardless of atlas insertion order"
          <| fun () ->
              let gen: Gen<int[]> = Gen.arrayOf (Gen.choose (0, 999))
              let samples = Gen.sample sampleCount gen |> Array.filter (fun a -> a.Length > 0)

              checkAll samples (sprintf "%A") (fun indices ->
                  let boundedIndices = indices |> Array.distinct

                  let frames =
                      boundedIndices
                      |> Array.map (fun i ->
                          let name = sprintf "walk_%03d.png" i
                          name, mkFrame name 1 1 false)
                      |> Map.ofArray

                  let atlases = Map.ofList [ "atlas.png", { Image = "atlas.png"; Frames = frames } ]

                  let resources: ResourcesManifest =
                      Map.ofList [ "alias", Animation("atlas.png", "walk_", 0.1) ]

                  match resolve resources atlases "alias" with
                  | Ok sprite ->
                      let expected = boundedIndices |> Array.sort |> Array.toList
                      let actual = sprite.Frames |> List.map (fun f -> int (f.Name.Substring(5, 3)))
                      actual = expected
                  | Error _ -> false)

          testCase "frameAt index = floor(elapsed / delay) mod frameCount"
          <| fun () ->
              let gen: Gen<int * int * int> =
                  Gen.map3
                      (fun frameCount delayMs elapsedMs -> (frameCount, delayMs, elapsedMs))
                      (Gen.choose (1, 200))
                      (Gen.choose (0, 5000))
                      (Gen.choose (0, 20000))

              let samples = Gen.sample sampleCount gen

              checkAll
                  samples
                  (fun (fc, d, e) -> sprintf "frameCount=%d delayMs=%d elapsedMs=%d" fc d e)
                  (fun (frameCount, delayMs, elapsedMs) ->
                      let delay = float delayMs / 1000.0 + 0.01
                      let elapsed = float elapsedMs / 1000.0

                      let frames =
                          [ 0 .. frameCount - 1 ] |> List.map (fun i -> mkFrame (sprintf "f%d.png" i) 1 1 false)

                      let sprite: ResolvedSprite =
                          { Image = "atlas.png"
                            Source = { X = 0.0; Y = 0.0; W = 1.0; H = 1.0 }
                            Rotated = false
                            Origin = System.Numerics.Vector2.Zero
                            Frames = frames
                            FrameDelay = delay }

                      let expectedIndex = int (floor (elapsed / delay)) % frameCount
                      let expected = List.item expectedIndex frames
                      frameAt sprite elapsed = expected)

          testCase "frameAt on a non-animated (static) resolution always returns frame 0, elapsed-independent"
          <| fun () ->
              let samples = Gen.sample sampleCount (Gen.choose (0, 20000))

              checkAll samples (sprintf "elapsedMs=%d") (fun elapsedMs ->
                  let elapsed = float elapsedMs / 1000.0

                  let sprite: ResolvedSprite =
                      { Image = "atlas.png"
                        Source = { X = 1.0; Y = 2.0; W = 32.0; H = 32.0 }
                        Rotated = false
                        Origin = System.Numerics.Vector2.Zero
                        Frames = []
                        FrameDelay = 0.0 }

                  let f = frameAt sprite elapsed
                  f.W = 32 && f.H = 32) ]
