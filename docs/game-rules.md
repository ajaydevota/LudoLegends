# Ludo Legends - rules and board model

Everything here is implemented in `LudoLegends.Core` and covered by automated tests
(`server/tests/LudoLegends.Core.Tests`). The same assembly runs on the server and inside
the Unity client, so online and offline play cannot diverge.

## Board

* 15 x 15 grid, four arms, centre home square.
* A single ring of **52 cells**, indexed 0-51 clockwise.
* Four yards (bases), 6 x 6 cells each, one per colour.
* A five-cell coloured **home column** per colour leading into the centre.

### Position model

A token's position is a *progress* value relative to its own start cell:

| progress | meaning |
|---------:|---------|
| `-1` | in the yard |
| `0..51` | on the ring; absolute cell = `(startOffset + progress) % 52` |
| `52..56` | inside the player's home column (five cells, walking inward) |
| `57` | home (centre) |

From its start cell a token needs exactly **57 steps** to reach home. Progress 51 is the
cell immediately *before* its own start cell, which is the entry square of its home
column, so a token makes one full lap plus the column.

Start offsets: Red `41`, Blue `2`, Green `15`, Yellow `28`.
Two-player games use Red and Green (opposite corners).

### Safe cells

Eight, matching the printed board: the four start squares plus the star square eight
cells after each start.

`{2, 10, 15, 23, 28, 36, 41, 49}`

A token standing on a safe cell can never be captured, including its own start square.

## Turn rules

1. Seat 0 moves first; play rotates through unfinished seats.
2. Roll 1-6. Leave the yard only on a **6**.
3. Move one legal token forward by the die value.
4. A token must land **exactly** on home; overshooting is not a legal move.
5. Landing on a ring cell occupied by exactly one opponent token sends that token back to
   its yard - unless the cell is safe.
6. Two or more tokens of the same player on one ring cell form a **block**: opponents may
   neither land on nor pass it. (Configurable via `GameRules.BlockingEnabled`.)
7. Extra turn after: rolling a 6, capturing a token, or bringing a token home.
8. **Three sixes in a row** forfeits the turn immediately.
9. If no token can move, the turn passes automatically.
10. A player who brings all four tokens home finishes; the first to finish wins. The match
    ends when only one player is left unfinished (2P ends immediately).

## State machine

```
AwaitingRoll --(roll, >=1 legal move)--> AwaitingMove --(move)--> AwaitingRoll | Finished
             --(roll, no legal move)---> AwaitingRoll (next seat)
             --(third six)-------------> AwaitingRoll (next seat)
```

Every accepted command increments `State.Version`. Commands carry a `CommandId`; the engine
remembers the last 64 and treats repeats as duplicates, so a retry can never roll or move
twice. This is what makes aggressive client reconnection safe.

## AI

`LudoAi` scores every legal move:

| condition | weight |
|---|---:|
| captures an opponent | 1000 |
| reaches home | 800 |
| enters the home column | 300 |
| leaves the yard | 220 |
| lands on a safe cell | 160 |
| progress | 1 per step |

Ties break on the lowest token index, so replays are deterministic. The same AI plays
offline opponents, fills private-room seats with bots, and auto-plays a seat whose player
disconnected past the grace period or ran out the turn clock.

## Dice

Generated only on the server, with `RandomNumberGenerator` and rejection sampling for a
uniform 1-6 distribution. Clients have no protocol field for a die value or a result
(`ClientEnvelope` is asserted against this in a test). Offline modes generate dice locally
because the device owns the whole match and there is nobody to cheat.
