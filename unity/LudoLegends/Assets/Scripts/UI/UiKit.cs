using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LudoLegends.Client
{
    /// <summary>
    /// Procedural uGUI toolkit.
    ///
    /// The whole interface is built from code: no prefabs, no scenes to wire, no textures
    /// to import. That keeps the repository text-only, makes review easy, and produces a
    /// tiny APK. Rendering is plain uGUI Images + legacy Text, which is the cheapest path
    /// on low-end Android devices.
    /// </summary>
    public static class UiKit
    {
        // Brand palette (original, not derived from any existing game).
        public static readonly Color Ink = Hex("2B2118");
        public static readonly Color Parchment = Hex("F7F1E3");
        public static readonly Color Card = Hex("FFFDF8");
        public static readonly Color Primary = Hex("6C3FC5");
        public static readonly Color PrimaryDark = Hex("4E2C92");
        public static readonly Color Accent = Hex("F2A93B");
        public static readonly Color Success = Hex("2E9E5B");
        public static readonly Color Danger = Hex("D1483C");
        public static readonly Color Muted = Hex("7A6A58");

        public static readonly Color Red = Hex("E5453F");
        public static readonly Color Blue = Hex("2F6FE0");
        public static readonly Color Green = Hex("2FA84F");
        public static readonly Color Yellow = Hex("E8B427");

        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_font == null)
                    {
                        _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    }
                }

                return _font;
            }
        }

        public static Color Hex(string hex)
        {
            Color color;
            if (ColorUtility.TryParseHtmlString("#" + hex, out color))
            {
                return color;
            }

            return Color.magenta;
        }

        public static Color ColorFor(int colorIndex)
        {
            switch (colorIndex)
            {
                case 0: return Red;
                case 1: return Blue;
                case 2: return Green;
                case 3: return Yellow;
                default: return Muted;
            }
        }

        // ------------------------------------------------------------- layout

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Image Panel(Transform parent, string name, Color color)
        {
            RectTransform rect = Rect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Image RoundedPanel(Transform parent, string name, Color color, float radius = 24f)
        {
            Image image = Panel(parent, name, color);
            image.sprite = Sprites.RoundedRect(radius);
            image.type = Image.Type.Sliced;
            return image;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            RectTransform rect = Rect(parent, "Label");
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(Transform parent, string text, Action onClick, Color? background = null, int fontSize = 30)
        {
            Image image = RoundedPanel(parent, "Button_" + text, background ?? Primary, 26f);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.88f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Text label = Label(image.transform, text, fontSize, Color.white);
            label.raycastTarget = false;

            if (onClick != null)
            {
                button.onClick.AddListener(() =>
                {
                    AudioService.Instance.PlayClick();
                    AudioService.Instance.Tap();
                    onClick();
                });
            }

            return button;
        }

        public static InputField Input(Transform parent, string placeholder, string initial = "")
        {
            Image background = RoundedPanel(parent, "Input", new Color(1f, 1f, 1f, 0.92f), 18f);

            RectTransform textRect = Rect(background.transform, "Text");
            textRect.offsetMin = new Vector2(22f, 6f);
            textRect.offsetMax = new Vector2(-22f, -6f);

            var text = textRect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = 30;
            text.color = Ink;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            RectTransform placeholderRect = Rect(background.transform, "Placeholder");
            placeholderRect.offsetMin = new Vector2(22f, 6f);
            placeholderRect.offsetMax = new Vector2(-22f, -6f);

            var hint = placeholderRect.gameObject.AddComponent<Text>();
            hint.font = Font;
            hint.fontSize = 30;
            hint.color = new Color(0.5f, 0.45f, 0.4f, 0.7f);
            hint.alignment = TextAnchor.MiddleLeft;
            hint.text = placeholder;
            hint.raycastTarget = false;

            var field = background.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.text = initial;
            field.characterLimit = 24;

            return field;
        }

        public static ScrollRect Scroll(Transform parent, out RectTransform content, float spacing = 12f)
        {
            RectTransform viewport = Rect(parent, "ScrollView");
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform contentRect = Rect(viewport, "Content");
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            var layout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(8, 8, 8, 24);

            var fitter = contentRect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = contentRect;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            content = contentRect;
            return scroll;
        }

        public static GameObject Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;

            return go;
        }

        public static GameObject Column(Transform parent, float height, float spacing = 8f)
        {
            var go = new GameObject("Column", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;

            return go;
        }

        public static LayoutElement Sized(GameObject target, float height, float width = -1f)
        {
            var element = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            if (width > 0f)
            {
                element.minWidth = width;
                element.preferredWidth = width;
            }

            return element;
        }

        public static void Stretch(GameObject target, float padding = 0f)
        {
            var rect = (RectTransform)target.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        public static RectTransform EnsureEventSystem()
        {
            EventSystem existing = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (existing != null)
            {
                return existing.transform as RectTransform;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return go.transform as RectTransform;
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }
    }

    /// <summary>Procedurally generated sprites (rounded panels, circles) cached by size.</summary>
    public static class Sprites
    {
        private static readonly System.Collections.Generic.Dictionary<int, Sprite> Rounded =
            new System.Collections.Generic.Dictionary<int, Sprite>();

        private static readonly System.Collections.Generic.Dictionary<int, Sprite> Circles =
            new System.Collections.Generic.Dictionary<int, Sprite>();

        public static Sprite RoundedRect(float radius)
        {
            int key = Mathf.Max(4, Mathf.RoundToInt(radius));
            if (Rounded.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }

            int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float r = key;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(r - x, x - (size - 1 - r)));
                    float dy = Mathf.Max(0f, Mathf.Max(r - y, y - (size - 1 - r)));
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(r - distance + 0.5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(key, key, key, key));
            Rounded[key] = sprite;
            return sprite;
        }

        public static Sprite Circle(int diameter = 96)
        {
            int key = Mathf.Max(16, diameter);
            if (Circles.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }

            var texture = new Texture2D(key, key, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float radius = key * 0.5f;
            for (int y = 0; y < key; y++)
            {
                for (int x = 0; x < key; x++)
                {
                    float dx = x - radius + 0.5f;
                    float dy = y - radius + 0.5f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, key, key), new Vector2(0.5f, 0.5f), 100f);
            Circles[key] = sprite;
            return sprite;
        }

        /// <summary>Right-angled triangle used for the four centre home triangles.</summary>
        public static Sprite Triangle(int pointsDown = 3)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size;
                    float ny = (y + 0.5f) / size;
                    bool inside = nx + ny <= 1f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, inside ? 1f : 0f));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
