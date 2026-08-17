module Duelyst.Assets.Tests.ResolveTests

open Expecto
open Duelyst.Assets.AtlasManifest
open Duelyst.Assets.Manifest

let private frame name x y w h rotated offX offY srcW srcH : Frame =
    { Name = name
      X = x
      Y = y
      W = w
      H = h
      Rotated = rotated
      OffsetX = offX
      OffsetY = offY
      SrcW = srcW
      SrcH = srcH }

let private atlases: AtlasTable =
    Map.ofList
        [ "atlas.png",
          { Image = "atlas.png"
            Frames =
              Map.ofList
                  [ "hero_idle.png", frame "hero_idle.png" 0 0 32 32 false 0 0 32 32
                    // packed rotated at 90deg: stored w=20,h=40 -> rendered swaps to w=40,h=20
                    "hero_rot.png", frame "hero_rot.png" 40 0 20 40 true 0 0 20 40
                    "hero_walk_000.png", frame "hero_walk_000.png" 0 40 16 16 false 0 0 16 16
                    "hero_walk_001.png", frame "hero_walk_001.png" 16 40 16 16 false 0 0 16 16
                    "hero_walk_010.png", frame "hero_walk_010.png" 32 40 16 16 false 0 0 16 16 ] } ]

let private resources: ResourcesManifest =
    Map.ofList
        [ "heroIdle", Sprite("atlas.png", "hero_idle.png")
          "heroRotated", Sprite("atlas.png", "hero_rot.png")
          "heroWalk", Animation("atlas.png", "hero_walk_", 0.1)
          "heroTexture", Texture "hero.png" ]

[<Tests>]
let tests =
    testList
        "Manifest.resolve"
        [ testCase "sprite resolves to its frame's source rect"
          <| fun () ->
              match resolve resources atlases "heroIdle" with
              | Ok sprite ->
                  Expect.equal sprite.Source { X = 0.0; Y = 0.0; W = 32.0; H = 32.0 } "source rect"
                  Expect.isFalse sprite.Rotated "not rotated"
              | Error e -> failtestf "expected Ok, got %A" e

          testCase "rotated sprite swaps w/h in the resolved source rect"
          <| fun () ->
              match resolve resources atlases "heroRotated" with
              | Ok sprite ->
                  Expect.equal sprite.Source.W 40.0 "width swapped from packed height"
                  Expect.equal sprite.Source.H 20.0 "height swapped from packed width"
                  Expect.isTrue sprite.Rotated "rotated flag preserved"
              | Error e -> failtestf "expected Ok, got %A" e

          testCase "animation resolves an ordered frame list by numeric suffix"
          <| fun () ->
              match resolve resources atlases "heroWalk" with
              | Ok sprite ->
                  let names = sprite.Frames |> List.map (fun f -> f.Name)

                  Expect.equal
                      names
                      [ "hero_walk_000.png"; "hero_walk_001.png"; "hero_walk_010.png" ]
                      "ascending numeric order, not lexicographic"

                  Expect.equal sprite.FrameDelay 0.1 "frame delay carried through"
              | Error e -> failtestf "expected Ok, got %A" e

          testCase "unknown alias yields Error"
          <| fun () ->
              match resolve resources atlases "doesNotExist" with
              | Error(UnknownAlias "doesNotExist") -> ()
              | other -> failtestf "expected UnknownAlias, got %A" other

          testCase "unknown frame yields Error"
          <| fun () ->
              let badResources = Map.add "heroMissing" (Sprite("atlas.png", "nope.png")) resources

              match resolve badResources atlases "heroMissing" with
              | Error(UnknownFrame("atlas.png", "nope.png")) -> ()
              | other -> failtestf "expected UnknownFrame, got %A" other

          testCase "missing atlas image yields Error"
          <| fun () ->
              let badResources = Map.add "heroGhost" (Sprite("ghost.png", "x.png")) resources

              match resolve badResources atlases "heroGhost" with
              | Error(MissingAtlas "ghost.png") -> ()
              | other -> failtestf "expected MissingAtlas, got %A" other ]
