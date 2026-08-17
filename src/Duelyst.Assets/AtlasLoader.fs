/// IO/Raylib: load an atlas PNG -> Texture2D.
module Duelyst.Assets.AtlasLoader

open System.IO
open Raylib_cs

/// Load `imagePath` (relative to `assets/`, as stored in resources.json/atlases.json) from disk.
let loadTexture (assetsRoot: string) (imagePath: string) : Texture2D =
    let fullPath = Path.Combine(assetsRoot, imagePath)
    Raylib.LoadTexture(fullPath)
