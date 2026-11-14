using System;
using System.Collections;
using System.Collections.Generic;
using LudoLegends.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LudoLegends.Client
{
    /// <summary>
    /// Renders the Ludo board and tokens from the shared <see cref="BoardTopology"/>.
    ///
    /// Board geometry comes from the same code the server uses to validate moves, so a
    /// token can never be drawn on a cell the rules do not consider valid. Everything is
    /// uGUI Images (no meshes, no shaders), which keeps this cheap on 2 GB devices.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public const float Cell = 100f;
        private const int TokensPerSeat = 4;

        private RectTransform _root;
        private RectTransform _tokenLayer;
        private readonly Dictionary<int, Image> _tokens = new Dictionary<int, Image>();
        private readonly Dictionary<int, Image> _tokenRings = new Dictionary<int, Image>();
        private int[] _legalTokens = Array.Empty<int>();
        private int _mySeat = -1;
        private GameSnapshot _state;
        private Coroutine _animation;

        public event Action<int> TokenTapped;

        public bool Interactable { get; set; }

        public void Build(RectTransform parent)
        {
            _root = UiKit.Rect(parent, "Board");
            _root.sizeDelta = new Vector2(Cell * BoardTopology.GridSize, Cell * BoardTopology.GridSize);

            // Paper background with a soft border.
            Image paper = UiKit.RoundedPanel(_root, "Paper", UiKit.Parchment, 28f);
            UiKit.Stretch(paper.gameObject);

            BuildYards();
            BuildHomeColumns();
            BuildRing();
            BuildCenter();

            _tokenLayer = UiKit.Rect(_root, "Tokens");
            _tokenLayer.anchorMin = new Vector2(0.5f, 0.5f);
            _tokenLayer.anchorMax = new Vector2(0.5f, 0.5f);
            _tokenLayer.sizeDelta = _root.sizeDelta;
        }

        public void SetPlayerSeat(int seat)
        {
            _mySeat = seat;
        }

        // ------------------------------------------------------------- static board

        private void BuildYards()
        {
            for (int color = 0; color < 4; color++)
            {
                PlayerColor playerColor = (PlayerColor)color;
                BoardTopology.YardOrigin(playerColor, out int ox, out int oy);
                Color tint = UiKit.ColorFor(color);

                Image yard = UiKit.Panel(_root, "Yard" + color, tint);
                RectTransform rect = yard.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(Cell * 6f, Cell * 6f);
                rect.anchoredPosition = GridToPosition(ox + 2.5f, oy + 2.5f);

                Image plate = UiKit.RoundedPanel(yard.transform, "Plate", UiKit.Card, 26f);
                RectTransform plateRect = plate.rectTransform;
                plateRect.anchorMin = new Vector2(0.5f, 0.5f);
                plateRect.anchorMax = new Vector2(0.5f, 0.5f);
                plateRect.sizeDelta = new Vector2(Cell * 5.1f, Cell * 5.1f);

                for (int token = 0; token < TokensPerSeat; token++)
                {
                    BoardTopology.BaseSlot(playerColor, token, out int sx, out int sy);
                    Image slot = UiKit.Panel(_root, "Slot" + color + token, new Color(tint.r, tint.g, tint.b, 0.22f));
                    slot.sprite = Sprites.Circle();
                    RectTransform slotRect = slot.rectTransform;
                    slotRect.anchorMin = new Vector2(0.5f, 0.5f);
                    slotRect.anchorMax = new Vector2(0.5f, 0.5f);
                    slotRect.sizeDelta = new Vector2(Cell * 0.78f, Cell * 0.78f);
                    slotRect.anchoredPosition = GridToPosition(sx, sy);
                }
            }
        }

        private void BuildHomeColumns()
        {
            for (int color = 0; color < 4; color++)
            {
                PlayerColor playerColor = (PlayerColor)color;
                Color tint = UiKit.ColorFor(color);

                for (int i = 0; i < BoardTopology.HomeColumnCells; i++)
                {
                    int x = BoardTopology.HomeColumnCellX(playerColor, i);
                    int y = BoardTopology.HomeColumnCellY(playerColor, i);

                    Image cell = UiKit.Panel(_root, "Home" + color + "_" + i, tint);
                    RectTransform rect = cell.rectTransform;
                    rect.anchorMin = new Vector2(0.5f, 0.5f);
                    rect.anchorMax = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = new Vector2(Cell - 4f, Cell - 4f);
                    rect.anchoredPosition = GridToPosition(x, y);

                    // Lighter inner square hints at the direction of travel.
                    Image inner = UiKit.Panel(cell.transform, "Inner", new Color(1f, 1f, 1f, 0.28f));
                    UiKit.Stretch(inner.gameObject, Cell * 0.22f);
                }
            }
        }

        private void BuildRing()
        {
            for (int cell = 0; cell < BoardTopology.TrackCells; cell++)
            {
                int x = BoardTopology.TrackCellX(cell);
                int y = BoardTopology.TrackCellY(cell);

                Image slot = UiKit.Panel(_root, "Cell" + cell, UiKit.Hex("CBBFA6"));
                RectTransform rect = slot.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(Cell - 2f, Cell - 2f);
                rect.anchoredPosition = GridToPosition(x, y);

                Image fill = UiKit.Panel(slot.transform, "Fill", Color.white);
                UiKit.Stretch(fill.gameObject, 2f);

                if (BoardTopology.IsSafeCell(cell))
                {
                    fill.color = UiKit.Hex("FFEFC2");
                    Image star = UiKit.Panel(fill.transform, "Star", UiKit.Hex("B08A2E"));
                    star.sprite = Sprites.Circle(48);
                    RectTransform starRect = star.rectTransform;
                    starRect.anchorMin = new Vector2(0.5f, 0.5f);
                    starRect.anchorMax = new Vector2(0.5f, 0.5f);
                    starRect.sizeDelta = new Vector2(Cell * 0.22f, Cell * 0.22f);
                }

                // Start squares carry their owner's colour trim.
                for (int color = 0; color < 4; color++)
                {
                    if (BoardTopology.StartOffset((PlayerColor)color) == cell)
                    {
                        fill.color = new Color(
                            UiKit.ColorFor(color).r,
                            UiKit.ColorFor(color).g,
                            UiKit.ColorFor(color).b,
                            0.35f);
                    }
                }
            }
        }

        private void BuildCenter()
        {
            float size = Cell * 3f;
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                Image triangle = UiKit.Panel(_root, "Center" + quadrant, UiKit.ColorFor(quadrant));
                triangle.sprite = TriangleSprites.For(quadrant);
                RectTransform rect = triangle.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = GridToPosition(BoardTopology.CenterCell, BoardTopology.CenterCell);
            }

            Image outline = UiKit.Panel(_root, "CenterOutline", new Color(0.2f, 0.15f, 0.1f, 0.25f));
            RectTransform outlineRect = outline.rectTransform;
            outlineRect.anchorMin = new Vector2(0.5f, 0.5f);
            outlineRect.anchorMax = new Vector2(0.5f, 0.5f);
            outlineRect.sizeDelta = new Vector2(size, size);
            outlineRect.anchoredPosition = GridToPosition(BoardTopology.CenterCell, BoardTopology.CenterCell);
            outline.gameObject.AddComponent<Outline>();
        }

        // ------------------------------------------------------------- tokens

        private Vector2 GridToPosition(float gridX, float gridY)
        {
            float half = BoardTopology.GridSize * Cell * 0.5f;
            return new Vector2(
                (gridX + 0.5f) * Cell - half,
                half - (gridY + 0.5f) * Cell);
        }

        private static int TokenKey(int seat, int token)
        {
            return seat * 16 + token;
        }

        private Image EnsureToken(int seat, int token, int color)
        {
            int key = TokenKey(seat, token);
            if (_tokens.TryGetValue(key, out Image existing))
            {
                existing.color = UiKit.ColorFor(color);
                return existing;
            }

            Image ring = UiKit.Panel(_tokenLayer, "TokenRing" + key, new Color(0.15f, 0.11f, 0.08f, 0.85f));
            ring.sprite = Sprites.Circle();
            RectTransform ringRect = ring.rectTransform;
            ringRect.anchorMin = new Vector2(0.5f, 0.5f);
            ringRect.anchorMax = new Vector2(0.5f, 0.5f);
            ringRect.sizeDelta = new Vector2(Cell * 0.78f, Cell * 0.78f);
            ring.raycastTarget = false;
            _tokenRings[key] = ring;

            Image body = UiKit.Panel(ring.transform, "Token" + key, UiKit.ColorFor(color));
            body.sprite = Sprites.Circle();
            RectTransform bodyRect = body.rectTransform;
            bodyRect.anchorMin = new Vector2(0.5f, 0.5f);
            bodyRect.anchorMax = new Vector2(0.5f, 0.5f);
            bodyRect.sizeDelta = new Vector2(Cell * 0.66f, Cell * 0.66f);
            body.raycastTarget = false;

            Image gloss = UiKit.Panel(body.transform, "Gloss", new Color(1f, 1f, 1f, 0.55f));
            gloss.sprite = Sprites.Circle();
            RectTransform glossRect = gloss.rectTransform;
            glossRect.anchorMin = new Vector2(0.32f, 0.52f);
            glossRect.anchorMax = new Vector2(0.32f, 0.52f);
            glossRect.sizeDelta = new Vector2(Cell * 0.22f, Cell * 0.22f);
            gloss.raycastTarget = false;

            var hit = ring.gameObject.AddComponent<Button>();
            hit.transition = Selectable.Transition.None;
            int capturedSeat = seat;
            int capturedToken = token;
            hit.onClick.AddListener(() =>
            {
                if (!Interactable || _mySeat != capturedSeat)
                {
                    return;
                }

                if (Array.IndexOf(_legalTokens, capturedToken) < 0)
                {
                    return;
                }

                AudioService.Instance.PlayClick();
                TokenTapped?.Invoke(capturedToken);
            });

            _tokens[key] = body;
            return body;
        }

        /// <summary>Where a token should sit, including small offsets for stacked tokens.</summary>
        private Vector2 TokenPosition(SeatSnapshot seat, int tokenIndex)
        {
            int progress = seat.Tokens[tokenIndex];
            PlayerColor color = (PlayerColor)seat.Color;

            if (progress < 0)
            {
                BoardTopology.BaseSlot(color, tokenIndex, out int bx, out int by);
                return GridToPosition(bx, by);
            }

            if (progress >= BoardTopology.ProgressHome)
            {
                // Finished tokens gather in the centre, nudged toward their own triangle.
                Vector2 center = GridToPosition(BoardTopology.CenterCell, BoardTopology.CenterCell);
                Vector2 direction = DirectionFor(color);
                float spread = Cell * 0.16f;
                return center + direction * (Cell * 0.34f + (tokenIndex - 1.5f) * spread * 0.5f);
            }

            BoardTopology.ProgressToCell(color, progress, out int x, out int y);
            Vector2 position = GridToPosition(x, y);

            // Fan out tokens sharing a cell so both remain visible.
            int index = 0;
            int count = 0;
            for (int s = 0; s < _state.Seats.Length; s++)
            {
                SeatSnapshot other = _state.Seats[s];
                for (int t = 0; t < other.Tokens.Length; t++)
                {
                    if (other.Tokens[t] != progress)
                    {
                        continue;
                    }

                    if (BoardTopology.AbsoluteCell((PlayerColor)other.Color, progress) ==
                        BoardTopology.AbsoluteCell(color, progress))
                    {
                        if (s == seat.Index && t == tokenIndex)
                        {
                            index = count;
                        }

                        count++;
                    }
                }
            }

            if (count > 1)
            {
                position += new Vector2((index - (count - 1) * 0.5f) * Cell * 0.16f, (index % 2 == 0 ? 1f : -1f) * Cell * 0.1f);
            }

            return position;
        }

        private static Vector2 DirectionFor(PlayerColor color)
        {
            switch (color)
            {
                case PlayerColor.Red: return new Vector2(-1f, 0f);
                case PlayerColor.Blue: return new Vector2(0f, 1f);
                case PlayerColor.Green: return new Vector2(1f, 0f);
                default: return new Vector2(0f, -1f);
            }
        }

        /// <summary>Draws authoritative state. No animation; used for resync and first paint.</summary>
        public void Render(GameSnapshot state, int[] legalTokens, int mySeat)
        {
            if (state == null)
            {
                return;
            }

            _state = state;
            _legalTokens = legalTokens ?? Array.Empty<int>();
            _mySeat = mySeat;

            var seen = new HashSet<int>();
            for (int s = 0; s < state.Seats.Length; s++)
            {
                SeatSnapshot seat = state.Seats[s];
                for (int t = 0; t < seat.Tokens.Length; t++)
                {
                    int key = TokenKey(seat.Index, t);
                    seen.Add(key);

                    Image token = EnsureToken(seat.Index, t, seat.Color);
                    RectTransform rect = token.transform.parent as RectTransform;
                    if (rect == null)
                    {
                        continue;
                    }

                    Vector2 target = TokenPosition(seat, t);
                    rect.anchoredPosition = target;

                    bool movable = seat.Index == mySeat && Array.IndexOf(_legalTokens, t) >= 0;
                    float scale = movable ? 1.14f : 1f;
                    rect.localScale = new Vector3(scale, scale, 1f);

                    if (_tokenRings.TryGetValue(key, out Image ring))
                    {
                        ring.color = movable
                            ? UiKit.Accent
                            : new Color(0.15f, 0.11f, 0.08f, 0.85f);

                        ring.rectTransform.SetSiblingIndex(movable ? _tokenLayer.childCount - 1 : ring.rectTransform.GetSiblingIndex());
                    }
                }
            }

            foreach (KeyValuePair<int, Image> pair in _tokens)
            {
                if (!seen.Contains(pair.Key) && pair.Value != null)
                {
                    Destroy(pair.Value.transform.parent.gameObject);
                }
            }
        }

        /// <summary>
        /// Animates one authoritative event. The snapshot paint always wins; this only makes
        /// the transition readable, so a slow device never ends up in a wrong state.
        /// </summary>
        public IEnumerator AnimateMove(GameEvent move)
        {
            int seatIndex = move.Seat;
            int tokenIndex = move.TokenIndex;
            if (_state == null || seatIndex < 0 || seatIndex >= _state.Seats.Length || tokenIndex < 0)
            {
                yield break;
            }

            int key = TokenKey(seatIndex, tokenIndex);
            if (!_tokenRings.TryGetValue(key, out Image ring))
            {
                yield break;
            }

            SeatSnapshot seat = _state.Seats[seatIndex];
            PlayerColor color = (PlayerColor)seat.Color;
            var rect = ring.rectTransform;

            var path = new List<Vector2>();
            if (move.From < 0)
            {
                BoardTopology.BaseSlot(color, tokenIndex, out int bx, out int by);
                path.Add(GridToPosition(bx, by));
            }

            for (int i = 0; i < move.TrackPath.Length; i++)
            {
                int cell = move.TrackPath[i];
                path.Add(GridToPosition(BoardTopology.TrackCellX(cell), BoardTopology.TrackCellY(cell)));
            }

            int homeIndex = move.To - (BoardTopology.LastTrackProgress + 1);
            if (move.To > BoardTopology.LastTrackProgress && move.To < BoardTopology.ProgressHome && homeIndex >= 0)
            {
                path.Add(GridToPosition(
                    BoardTopology.HomeColumnCellX(color, homeIndex),
                    BoardTopology.HomeColumnCellY(color, homeIndex)));
            }

            if (move.To >= BoardTopology.ProgressHome)
            {
                path.Add(GridToPosition(BoardTopology.CenterCell, BoardTopology.CenterCell) + DirectionFor(color) * Cell * 0.34f);
            }

            if (path.Count == 0)
            {
                yield break;
            }

            float stepTime = Mathf.Clamp(0.11f, 0.02f, 0.14f);
            for (int i = 0; i < path.Count; i++)
            {
                Vector2 from = rect.anchoredPosition;
                Vector2 to = path[i];
                float elapsed = 0f;
                AudioService.Instance.PlayStep();

                while (elapsed < stepTime)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / stepTime);
                    rect.anchoredPosition = Vector2.Lerp(from, to, t) + new Vector2(0f, Mathf.Sin(t * Mathf.PI) * Cell * 0.14f);
                    yield return null;
                }

                rect.anchoredPosition = to;
            }
        }

        public IEnumerator AnimateCapture(int seat, int token)
        {
            int key = TokenKey(seat, token);
            if (!_tokenRings.TryGetValue(key, out Image ring))
            {
                yield break;
            }

            AudioService.Instance.PlayCapture();
            var rect = ring.rectTransform;
            Vector2 start = rect.anchoredPosition;
            float duration = 0.35f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                rect.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, t);
                rect.anchoredPosition = start + new Vector2(0f, Mathf.Sin(t * Mathf.PI) * Cell * 0.5f);
                yield return null;
            }

            rect.localScale = Vector3.one;
        }

        public IEnumerator AnimateWin(int seat)
        {
            AudioService.Instance.PlayWin();
            float elapsed = 0f;
            var rects = new List<RectTransform>();
            for (int token = 0; token < TokensPerSeat; token++)
            {
                if (_tokenRings.TryGetValue(TokenKey(seat, token), out Image ring))
                {
                    rects.Add(ring.rectTransform);
                }
            }

            while (elapsed < 1.2f)
            {
                elapsed += Time.deltaTime;
                float bounce = 1f + Mathf.Abs(Mathf.Sin(elapsed * 8f)) * 0.18f;
                for (int i = 0; i < rects.Count; i++)
                {
                    rects[i].localScale = Vector3.one * bounce;
                }

                yield return null;
            }

            for (int i = 0; i < rects.Count; i++)
            {
                rects[i].localScale = Vector3.one;
            }
        }
    }

    /// <summary>Four triangles that meet at the centre of the home square.</summary>
    public static class TriangleSprites
    {
        private static readonly Sprite[] Cache = new Sprite[4];

        public static Sprite For(int quadrant)
        {
            if (Cache[quadrant] != null)
            {
                return Cache[quadrant];
            }

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size;
                    float ny = (y + 0.5f) / size;
                    bool inside;
                    switch (quadrant)
                    {
                        case 0: // top
                            inside = ny >= 0.5f && Mathf.Abs(nx - 0.5f) <= (1f - ny) * 1f;
                            break;
                        case 1: // right
                            inside = nx >= 0.5f && Mathf.Abs(ny - 0.5f) <= (1f - nx) * 1f;
                            break;
                        case 2: // bottom
                            inside = ny <= 0.5f && Mathf.Abs(nx - 0.5f) <= ny * 1f;
                            break;
                        default: // left
                            inside = nx <= 0.5f && Mathf.Abs(ny - 0.5f) <= nx * 1f;
                            break;
                    }

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, inside ? 1f : 0f));
                }
            }

            texture.Apply();
            Cache[quadrant] = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return Cache[quadrant];
        }
    }
}
