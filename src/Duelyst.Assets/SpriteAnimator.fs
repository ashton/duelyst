/// Frame timing (pure) + draw (Raylib). No `open Raylib_cs` in this file: `frameAt` stays
/// provably Raylib-free even though `draw` (fully-qualified Raylib_cs references) shares the module.
module Duelyst.Assets.SpriteAnimator

open Duelyst.Assets.AtlasManifest
open Duelyst.Assets.Manifest

/// PURE, total: index = floor(elapsed / frameDelay) mod frameCount; frameDelay <= 0 => frame 0.
/// For a non-animated resolution (Frames = []) this synthesizes a single frame from Source/Origin.
let frameAt (sprite: ResolvedSprite) (elapsedSeconds: float) : Frame =
    match sprite.Frames with
    | [] ->
        { Name = sprite.Image
          X = int sprite.Source.X
          Y = int sprite.Source.Y
          W = int sprite.Source.W
          H = int sprite.Source.H
          Rotated = sprite.Rotated
          OffsetX = float sprite.Origin.X
          OffsetY = float sprite.Origin.Y
          SrcW = int sprite.Source.W
          SrcH = int sprite.Source.H }
    | frames ->
        let n = List.length frames

        if sprite.FrameDelay <= 0.0 then
            List.item 0 frames
        else
            let index = int (floor (elapsedSeconds / sprite.FrameDelay)) % n
            List.item index frames

/// IO/Raylib: DrawTexturePro. Rotated frames rotate 90 degrees about `origin`, which is derived
/// from the frame's trim offset so trimmed sprites still position correctly.
let draw
    (texture: Raylib_cs.Texture2D)
    (frame: Frame)
    (dest: Raylib_cs.Rectangle)
    (rotated: bool)
    : unit =
    let sourceRect =
        Raylib_cs.Rectangle(float32 frame.X, float32 frame.Y, float32 frame.W, float32 frame.H)

    let origin = System.Numerics.Vector2(float32 frame.OffsetX, float32 frame.OffsetY)
    let rotation = if rotated then 90.0f else 0.0f
    Raylib_cs.Raylib.DrawTexturePro(texture, sourceRect, dest, origin, rotation, Raylib_cs.Color.White)
