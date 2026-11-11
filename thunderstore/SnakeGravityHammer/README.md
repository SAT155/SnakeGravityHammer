# SnakeGravityHammer

Vanilla building keeps every piece perfectly level: you can turn it, but you can't tilt it. On a
hillside, a lumpy rock or a pile of other pieces, that leaves floating corners and pieces sunk
halfway into the slope.

With this mod, **hold Left Alt while placing with the hammer** and the preview drops onto what's
underneath it: it **tilts forwards/backwards and sideways**, balancing over its center of mass, to match the ground, other pieces or
rocks below, and **settles down** until it rests on them. A small, configurable amount of sinking
in hides the little gaps under a piece on uneven ground. Let go of the key for normal placement.

- Works on terrain, rocks and other built pieces. To rest a piece on top of another, aim at
  the one underneath; a piece standing next to it won't lift it up.
- Keeps the direction you turned the piece to; only the tilt and height change.
- The placed piece is saved with its tilt, so everyone sees it, with or without the mod.
- When a piece snaps onto another piece's snap points, the snap wins and the piece stays as
  snapped, so walls and floors still line up.
- Hoe and cultivator tools are left alone.
- Plays nicely with other building mods (for example Valheim Plus's building grid): it starts
  from wherever they put the preview.
- Unfortunately, it can make a log rest naturally on a hillside, but it does not let you dual wield
  spears. Gravity has its limits.

## Config

`BepInEx/config/com.snakesquad.snakegravityhammer.cfg` (or Configuration Manager):

| Setting | Default | What it does |
| --- | --- | --- |
| General / Enabled | `true` | Turns the whole mod on or off. |
| General / Gravity Key | `LeftAlt` | The key for gravity placement. |
| General / Key Mode | `Hold` | **Hold:** only while the key is held. **Toggle:** press once for on, again for off. |
| Settling / Fit | `CenterOfMass` | **CenterOfMass:** the piece tips over its middle until it rests on the points that hold it up, like a real object (it can rock onto a bump instead of following the average slope). **Average:** tilt to the average slope underneath. |
| Settling / Max Step Up | `0.5` | How far (metres) above where the game would put the piece it may rise to rest on something. Keeps the top of a piece next to it from lifting it into the air. To stack onto a piece, aim at that piece. |
| Settling / Sink Depth | `0.1` | How far (metres) a piece may sink into the ground or objects to close gaps. 0 = rest exactly on the highest point. |
| Settling / Max Tilt | `45` | Steepest tilt in degrees. |
| Settling / Samples Per Side | `5` | Ground checks along each side of the piece's base (5 = 5x5, up to 7). More fits rough ground more closely. |
| Settling / Rest On Pieces | `true` | Let pieces rest on other built pieces, not only terrain and rocks. |
| Debug / Log Fit | `false` | Log the tilt and height each time you place with gravity placement. |

## Multiplayer

Client-side only. Servers and other players don't need it; tilted pieces look the same to
everyone.

## Feedback

Feedback, bug reports and requests are welcome at [feedback@snakesquad.net](mailto:feedback@snakesquad.net).
