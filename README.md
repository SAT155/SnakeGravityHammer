# SnakeGravityHammer

Valheim BepInEx mod: hold a key while placing with the hammer and the piece tilts and settles
onto the terrain or objects below it. Player-facing docs: `thunderstore/SnakeGravityHammer/README.md`.

## How it works

`SnakeGravityHammer/SnakeGravityHammerPlugin.cs` postfixes `Player.UpdatePlacementGhost`
(`Priority.Last`, after Valheim Plus and SmartBuild). From the pose the game (or another mod) gave
the ghost it keeps the position across the ground and the yaw, works out the ghost's base from its
solid colliders (or meshes), casts a grid of rays down under the base, fits a plane through the
hits (least squares), clamps its tilt, then lowers/raises the piece so it rests on the highest
point minus *Sink Depth*. Placing uses the ghost's transform, so the piece lands as previewed.

## Building

CI (`.github/workflows/build.yml`) downloads the Valheim dedicated server and BepInExPack, builds,
packages a Thunderstore zip and releases `v<MAJOR.MINOR>.<run>` on every default-branch push that
changes the mod. Locally, `dotnet build` finds a Steam install of Valheim.
