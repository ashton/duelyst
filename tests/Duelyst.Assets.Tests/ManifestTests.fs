module Duelyst.Assets.Tests.ManifestTests

open Expecto
open Duelyst.Assets.AtlasManifest

let private validResourcesJson =
    """
{
  "heroTexture": { "kind": "texture", "img": "heroes/hero.png" },
  "heroIdle": { "kind": "sprite", "img": "atlas.png", "frame": "hero_idle.png" },
  "heroWalk": { "kind": "animation", "img": "atlas.png", "framePrefix": "hero_walk_", "frameDelay": 0.1 },
  "sfxHit": { "kind": "audio", "audio": "sfx/hit.wav" }
}
"""

let private validAtlasesJson =
    """
{
  "atlas.png": {
    "image": "atlas.png",
    "frames": [
      { "name": "hero_idle.png", "x": 0, "y": 0, "w": 32, "h": 32, "rotated": false, "offsetX": 0, "offsetY": 0, "srcW": 32, "srcH": 32 },
      { "name": "hero_walk_000.png", "x": 32, "y": 0, "w": 32, "h": 32, "rotated": true, "offsetX": 1, "offsetY": 2, "srcW": 40, "srcH": 40 }
    ]
  }
}
"""

[<Tests>]
let tests =
    testList
        "AtlasManifest"
        [ testCase "parseResources succeeds on valid JSON with all descriptor kinds"
          <| fun () ->
              match parseResources validResourcesJson with
              | Ok manifest ->
                  Expect.equal manifest.Count 4 "should parse 4 aliases"
                  Expect.equal manifest.["heroTexture"] (Texture "heroes/hero.png") "texture descriptor"
                  Expect.equal manifest.["heroIdle"] (Sprite("atlas.png", "hero_idle.png")) "sprite descriptor"

                  Expect.equal
                      manifest.["heroWalk"]
                      (Animation("atlas.png", "hero_walk_", 0.1))
                      "animation descriptor"

                  Expect.equal manifest.["sfxHit"] (Audio "sfx/hit.wav") "audio descriptor"
              | Error e -> failtestf "expected Ok, got %A" e

          testCase "parseResources returns Error on syntactically malformed JSON"
          <| fun () ->
              match parseResources "{ not valid json " with
              | Error _ -> ()
              | Ok _ -> failtest "expected Error on malformed JSON"

          testCase "parseResources returns Error when the root is not an object"
          <| fun () ->
              match parseResources "[]" with
              | Error(InvalidShape _) -> ()
              | other -> failtestf "expected InvalidShape, got %A" other

          testCase "parseResources returns Error when a descriptor is missing required fields"
          <| fun () ->
              match parseResources """{ "bad": { "kind": "sprite", "img": "x.png" } }""" with
              | Error(InvalidShape _) -> ()
              | other -> failtestf "expected InvalidShape (missing 'frame'), got %A" other

          testCase "parseAtlases succeeds on valid JSON"
          <| fun () ->
              match parseAtlases validAtlasesJson with
              | Ok table ->
                  Expect.equal table.Count 1 "one atlas"
                  let atlas = table.["atlas.png"]
                  Expect.equal atlas.Frames.Count 2 "two frames"
                  Expect.isTrue atlas.Frames.["hero_walk_000.png"].Rotated "walk frame is rotated"
                  Expect.equal atlas.Frames.["hero_walk_000.png"].OffsetX 1 "trim offsetX preserved"
              | Error e -> failtestf "expected Ok, got %A" e

          testCase "parseAtlases returns Error on syntactically malformed JSON"
          <| fun () ->
              match parseAtlases "not json at all" with
              | Error _ -> ()
              | Ok _ -> failtest "expected Error on malformed JSON"

          testCase "parseAtlases returns Error when the root is not an object"
          <| fun () ->
              match parseAtlases "[]" with
              | Error(InvalidShape _) -> ()
              | other -> failtestf "expected InvalidShape, got %A" other ]
