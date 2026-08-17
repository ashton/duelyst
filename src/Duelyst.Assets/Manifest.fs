/// PURE: resolve an RSX alias -> descriptor -> source Rectangle. NO Raylib, NO file IO.
module Duelyst.Assets.Manifest

open System.Numerics
open Duelyst.Assets.AtlasManifest

type Rectangle = { X: float; Y: float; W: float; H: float }

/// Output of `resolve`: what Raylib needs to draw, with zero Raylib types involved.
type ResolvedSprite =
    { Image: string
      Source: Rectangle
      Rotated: bool
      Origin: Vector2
      Frames: Frame list
      FrameDelay: float }

type ResolveError =
    | UnknownAlias of alias: string
    | MissingAtlas of image: string
    | UnknownFrame of image: string * frame: string

/// Frames named `framePrefix + index + ".png"` (or any extension), ordered ascending by index.
let private orderedAnimationFrames (framePrefix: string) (atlas: Atlas) : Frame list =
    atlas.Frames
    |> Map.toList
    |> List.map snd
    |> List.choose (fun f ->
        if f.Name.StartsWith(framePrefix) then
            let suffix = f.Name.Substring(framePrefix.Length)
            let numPart = suffix.Split('.') |> Array.head

            match System.Int32.TryParse(numPart) with
            | true, idx -> Some(idx, f)
            | false, _ -> None
        else
            None)
    |> List.sortBy fst
    |> List.map snd

/// Resolve an alias to what the renderer needs. Missing alias/frame/image -> a typed Error,
/// never an exception or a silent default.
let resolve
    (resources: ResourcesManifest)
    (atlases: AtlasTable)
    (alias: string)
    : Result<ResolvedSprite, ResolveError> =
    match Map.tryFind alias resources with
    | None -> Error(UnknownAlias alias)
    | Some descriptor ->
        match descriptor with
        | Texture img ->
            // No file IO here (Constitution III), so the full-image bounds are unknown at this
            // layer. W=H=0 is a sentinel the renderer (AtlasLoader/Program.fs) replaces with the
            // *loaded* Texture2D's actual dimensions once it has done the (impure) LoadTexture.
            Ok
                { Image = img
                  Source = { X = 0.0; Y = 0.0; W = 0.0; H = 0.0 }
                  Rotated = false
                  Origin = Vector2.Zero
                  Frames = []
                  FrameDelay = 0.0 }
        | Audio path ->
            Ok
                { Image = path
                  Source = { X = 0.0; Y = 0.0; W = 0.0; H = 0.0 }
                  Rotated = false
                  Origin = Vector2.Zero
                  Frames = []
                  FrameDelay = 0.0 }
        | Sprite(img, frameName) ->
            match Map.tryFind img atlases with
            | None -> Error(MissingAtlas img)
            | Some atlas ->
                match Map.tryFind frameName atlas.Frames with
                | None -> Error(UnknownFrame(img, frameName))
                | Some frame ->
                    // Rotated frames are packed at 90 degrees; the renderer rotates them back,
                    // so the *drawn* source rect has width/height swapped relative to the packed rect.
                    let w, h =
                        if frame.Rotated then float frame.H, float frame.W else float frame.W, float frame.H

                    Ok
                        { Image = img
                          Source = { X = float frame.X; Y = float frame.Y; W = w; H = h }
                          Rotated = frame.Rotated
                          Origin = Vector2(float32 frame.OffsetX, float32 frame.OffsetY)
                          Frames = [ frame ]
                          FrameDelay = 0.0 }
        | Animation(img, framePrefix, frameDelay) ->
            match Map.tryFind img atlases with
            | None -> Error(MissingAtlas img)
            | Some atlas ->
                match orderedAnimationFrames framePrefix atlas with
                | [] -> Error(UnknownFrame(img, framePrefix))
                | frames ->
                    let first = List.head frames

                    Ok
                        { Image = img
                          Source = { X = 0.0; Y = 0.0; W = float first.W; H = float first.H }
                          Rotated = first.Rotated
                          Origin = Vector2(float32 first.OffsetX, float32 first.OffsetY)
                          Frames = frames
                          FrameDelay = frameDelay }
