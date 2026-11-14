// Stubs for UnityEngine.UI, UnityEngine.EventSystems and UnityEngine.Networking.
#pragma warning disable 0067, 0649, 0414

using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum RenderMode { ScreenSpaceOverlay = 0, ScreenSpaceCamera = 1, WorldSpace = 2 }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }

    public sealed class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
        public int sortingOrder { get; set; }
        public Camera worldCamera { get; set; }
        public bool overrideSorting { get; set; }
        public float scaleFactor { get; }
        public float referencePixelsPerUnit { get; set; }
    }

    public sealed class CanvasGroup : Behaviour
    {
        public float alpha { get; set; }
        public bool interactable { get; set; }
        public bool blocksRaycasts { get; set; }
        public bool ignoreParentGroups { get; set; }
    }

    public sealed class CanvasRenderer : Component
    {
        public void SetAlpha(float alpha) { }
        public float GetAlpha() => 1f;
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main { get; }
        public Color backgroundColor { get; set; }
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; }
    }
}

namespace UnityEngine.Events
{
    public class UnityEventBase { }
    public class UnityEvent : UnityEventBase
    {
        public void AddListener(Action call) { }
        public void RemoveListener(Action call) { }
        public void RemoveAllListeners() { }
        public void Invoke() { }
    }

    public class UnityEvent<T0> : UnityEventBase
    {
        public void AddListener(Action<T0> call) { }
        public void RemoveListener(Action<T0> call) { }
        public void RemoveAllListeners() { }
        public void Invoke(T0 arg0) { }
    }
}

namespace UnityEngine.EventSystems
{
    public class EventSystem : MonoBehaviour
    {
        public static EventSystem current { get; set; }
        public GameObject firstSelectedGameObject { get; set; }
        public void SetSelectedGameObject(GameObject selected) { }
        public bool IsPointerOverGameObject() => false;
    }

    public abstract class BaseInputModule : MonoBehaviour { }

    public class PointerInputModule : BaseInputModule { }

    public class StandaloneInputModule : PointerInputModule { }

    public abstract class BaseEventData
    {
        public object selectedObject { get; }
    }

    public class PointerEventData : BaseEventData
    {
        public Vector2 position { get; set; }
        public Vector2 delta { get; set; }
        public GameObject pointerPress { get; set; }
        public GameObject pointerEnter { get; set; }
        public int pointerId { get; set; }
        public InputButton button { get; set; }
        public enum InputButton { Left = 0, Right = 1, Middle = 2 }
    }

    public class AxisEventData : BaseEventData
    {
        public Vector2 moveVector { get; set; }
    }

    public interface IEventSystemHandler { }
    public interface IPointerClickHandler : IEventSystemHandler { void OnPointerClick(PointerEventData eventData); }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData eventData); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData eventData); }
    public interface IPointerEnterHandler : IEventSystemHandler { void OnPointerEnter(PointerEventData eventData); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData eventData); }
    public interface IBeginDragHandler : IEventSystemHandler { void OnBeginDrag(PointerEventData eventData); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData eventData); }
    public interface IEndDragHandler : IEventSystemHandler { void OnEndDrag(PointerEventData eventData); }

    public static class ExecuteEvents
    {
        public static bool Execute<T>(GameObject target, BaseEventData eventData, EventFunction<T> functor) where T : IEventSystemHandler => false;
        public delegate void EventFunction<T1>(T1 handler, BaseEventData eventData);
    }
}

namespace UnityEngine.UI
{
    using UnityEngine.Events;
    using UnityEngine.EventSystems;

    public abstract class Graphic : UIBehaviour
    {
        public Color color { get; set; }
        public bool raycastTarget { get; set; }
        public RectTransform rectTransform { get; }
        public Canvas canvas { get; }
        public CanvasRenderer canvasRenderer { get; }
        public Material material { get; set; }
        public virtual void SetAllDirty() { }
        public virtual void SetVerticesDirty() { }
        public virtual void SetLayoutDirty() { }
        public void CrossFadeAlpha(float alpha, float duration, bool ignoreTimeScale) { }
        public void CrossFadeColor(Color targetColor, float duration, bool ignoreTimeScale, bool useAlpha) { }
    }

    public abstract class MaskableGraphic : Graphic
    {
        public bool maskable { get; set; }
    }

    public class UIBehaviour : MonoBehaviour
    {
        protected virtual void Awake() { }
        protected virtual void OnEnable() { }
        protected virtual void Start() { }
        protected virtual void OnDisable() { }
        protected virtual void OnDestroy() { }
        protected virtual void OnRectTransformDimensionsChange() { }
    }

    public class Image : MaskableGraphic
    {
        public Sprite sprite { get; set; }
        public Type type { get; set; }
        public bool preserveAspect { get; set; }
        public bool fillCenter { get; set; }
        public float fillAmount { get; set; }
        public FillMethod fillMethod { get; set; }
        public int fillOrigin { get; set; }
        public float pixelsPerUnitMultiplier { get; set; }
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
    }

    public class RawImage : MaskableGraphic
    {
        public Texture texture { get; set; }
        public Rect uvRect { get; set; }
    }

    public class Text : MaskableGraphic
    {
        public string text { get; set; }
        public Font font { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool supportRichText { get; set; }
        public bool resizeTextForBestFit { get; set; }
        public int resizeTextMinSize { get; set; }
        public int resizeTextMaxSize { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public float lineSpacing { get; set; }
        public float preferredWidth { get; }
        public float preferredHeight { get; }
    }

    public struct ColorBlock
    {
        public Color normalColor { get; set; }
        public Color highlightedColor { get; set; }
        public Color pressedColor { get; set; }
        public Color selectedColor { get; set; }
        public Color disabledColor { get; set; }
        public float colorMultiplier { get; set; }
        public float fadeDuration { get; set; }
        public static ColorBlock defaultColorBlock => new ColorBlock();
    }

    public struct SpriteState
    {
        public Sprite highlightedSprite { get; set; }
        public Sprite pressedSprite { get; set; }
        public Sprite selectedSprite { get; set; }
        public Sprite disabledSprite { get; set; }
    }

    public struct Navigation
    {
        public Mode mode { get; set; }
        public enum Mode { None = 0, Horizontal = 1, Vertical = 2, Automatic = 3, Explicit = 4 }
    }

    public class Selectable : UIBehaviour
    {
        public bool interactable { get; set; }
        public Graphic targetGraphic { get; set; }
        public ColorBlock colors { get; set; }
        public SpriteState spriteState { get; set; }
        public Navigation navigation { get; set; }
        public Transition transition { get; set; }
        public Image image { get; set; }
        public enum Transition { None, ColorTint, SpriteSwap, Animation }
        public virtual void Select() { }
    }

    public class Button : Selectable
    {
        public class ButtonClickedEvent : UnityEvent { }
        public ButtonClickedEvent onClick { get; set; }
    }

    public class Toggle : Selectable
    {
        public class ToggleEvent : UnityEvent<bool> { }
        public bool isOn { get; set; }
        public ToggleEvent onValueChanged { get; set; }
        public Graphic graphic { get; set; }
    }

    public class Slider : Selectable
    {
        public class SliderEvent : UnityEvent<float> { }
        public float value { get; set; }
        public float minValue { get; set; }
        public float maxValue { get; set; }
        public bool wholeNumbers { get; set; }
        public SliderEvent onValueChanged { get; set; }
        public RectTransform fillRect { get; set; }
        public RectTransform handleRect { get; set; }
    }

    public class InputField : Selectable
    {
        public class OnChangeEvent : UnityEvent<string> { }
        public class SubmitEvent : UnityEvent<string> { }
        public string text { get; set; }
        public Graphic placeholder { get; set; }
        public Text textComponent { get; set; }
        public int characterLimit { get; set; }
        public ContentType contentType { get; set; }
        public LineType lineType { get; set; }
        public OnChangeEvent onValueChanged { get; set; }
        public SubmitEvent onEndEdit { get; set; }
        public bool isFocused { get; }
        public void ActivateInputField() { }
        public void DeactivateInputField() { }
        public void Select() { }
        public enum ContentType { Standard, Autocorrected, IntegerNumber, DecimalNumber, Alphanumeric, Name, EmailAddress, Password, Pin, Custom }
        public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline }
    }

    public class ScrollRect : UIBehaviour
    {
        public class ScrollRectEvent : UnityEvent<Vector2> { }
        public float scrollSensitivity { get; set; }
        public RectTransform content { get; set; }
        public RectTransform viewport { get; set; }
        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public bool inertia { get; set; }
        public float decelerationRate { get; set; }
        public float elasticity { get; set; }
        public MovementType movementType { get; set; }
        public Scrollbar horizontalScrollbar { get; set; }
        public Scrollbar verticalScrollbar { get; set; }
        public Vector2 normalizedPosition { get; set; }
        public float verticalNormalizedPosition { get; set; }
        public float horizontalNormalizedPosition { get; set; }
        public ScrollRectEvent onValueChanged { get; set; }
        public enum MovementType { Unrestricted, Elastic, Clamped }
    }

    public class Scrollbar : Selectable
    {
        public float value { get; set; }
        public float size { get; set; }
        public class ScrollEvent : UnityEvent<float> { }
        public ScrollEvent onValueChanged { get; set; }
    }

    public class Mask : UIBehaviour { public bool showMaskGraphic { get; set; } }
    public class RectMask2D : UIBehaviour { }

    public class Outline : Shadow
    {
        public Vector2 effectDistance { get; set; }
    }

    public class Shadow : UIBehaviour
    {
        public Color effectColor { get; set; }
        public Vector2 effectDistance { get; set; }
        public bool useGraphicAlpha { get; set; }
    }

    public class LayoutElement : UIBehaviour
    {
        public float minWidth { get; set; }
        public float minHeight { get; set; }
        public float preferredWidth { get; set; }
        public float preferredHeight { get; set; }
        public float flexibleWidth { get; set; }
        public float flexibleHeight { get; set; }
        public bool ignoreLayout { get; set; }
        public int layoutPriority { get; set; }
    }

    public abstract class LayoutGroup : UIBehaviour
    {
        public RectOffset padding { get; set; }
        public TextAnchor childAlignment { get; set; }
    }

    public class HorizontalOrVerticalLayoutGroup : LayoutGroup
    {
        public float spacing { get; set; }
        public bool childForceExpandWidth { get; set; }
        public bool childForceExpandHeight { get; set; }
        public bool childControlWidth { get; set; }
        public bool childControlHeight { get; set; }
        public bool childScaleWidth { get; set; }
        public bool childScaleHeight { get; set; }
        public bool reverseArrangement { get; set; }
    }

    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }

    public class GridLayoutGroup : LayoutGroup
    {
        public Vector2 cellSize { get; set; }
        public Vector2 spacing { get; set; }
        public int constraintCount { get; set; }
        public Constraint constraint { get; set; }
        public enum Constraint { Flexible, FixedColumnCount, FixedRowCount }
    }

    public class ContentSizeFitter : UIBehaviour
    {
        public FitMode horizontalFit { get; set; }
        public FitMode verticalFit { get; set; }
        public enum FitMode { Unconstrained, MinSize, PreferredSize }
    }

    public class AspectRatioFitter : UIBehaviour
    {
        public AspectMode aspectMode { get; set; }
        public float aspectRatio { get; set; }
        public enum AspectMode { None, WidthControlsHeight, HeightControlsWidth, FitInParent, EnvelopeParent }
    }

    public class CanvasScaler : UIBehaviour
    {
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public ScreenMatchMode screenMatchMode { get; set; }
        public float matchWidthOrHeight { get; set; }
        public float scaleFactor { get; set; }
        public float referencePixelsPerUnit { get; set; }
        public float defaultSpriteDPI { get; set; }
        public float fallbackScreenDPI { get; set; }
        public float dynamicPixelsPerUnit { get; set; }
        public Unit physicalUnit { get; set; }
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
        public enum Unit { Centimeters, Millimeters, Inches, Points, Picas }
    }

    public class GraphicRaycaster : UIBehaviour
    {
        public bool ignoreReversedGraphics { get; set; }
        public BlockingObjects blockingObjects { get; set; }
        public enum BlockingObjects { None, TwoD, ThreeD, All }
    }

    public sealed class RectOffset
    {
        public RectOffset() { }
        public RectOffset(int left, int right, int top, int bottom) { }
        public int left { get; set; }
        public int right { get; set; }
        public int top { get; set; }
        public int bottom { get; set; }
    }
}

namespace UnityEngine
{
    public sealed class Material : Object
    {
        public Color color { get; set; }
    }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler : IDisposable
    {
        public string text => string.Empty;
        public byte[] data => Array.Empty<byte>();
        public bool isDone => true;
        public void Dispose() { }
    }

    public class DownloadHandlerBuffer : DownloadHandler
    {
        public static DownloadHandlerBuffer GetContent(UnityWebRequest request) => null;
    }

    public class UploadHandler : IDisposable
    {
        public void Dispose() { }
    }

    public class UploadHandlerRaw : UploadHandler
    {
        public UploadHandlerRaw(byte[] data) { }
    }

    public class UnityWebRequestAsyncOperation : YieldInstruction
    {
        public UnityWebRequest webRequest { get; }
        public bool isDone { get; }
        public float progress { get; }
    }

    public class UnityWebRequest : IDisposable
    {
        public const string kHttpVerbGET = "GET";
        public const string kHttpVerbPOST = "POST";

        public enum Result { InProgress = 0, Success = 1, ConnectionError = 2, ProtocolError = 3, DataProcessingError = 4 }

        public UnityWebRequest() { }
        public UnityWebRequest(string url) { }
        public UnityWebRequest(string url, string method) { }

        public Result result { get; }
        public static string EscapeURL(string s) => s;
        public static string EscapeURL(string s, System.Text.Encoding e) => s;

        public string url { get; set; }
        public string method { get; set; }
        public string error { get; }
        public long responseCode { get; }
        public bool isNetworkError { get; }
        public bool isHttpError { get; }
        public int timeout { get; set; }
        public bool isDone { get; }
        public float downloadProgress { get; }
        public DownloadHandler downloadHandler { get; set; }
        public UploadHandler uploadHandler { get; set; }
        public void SetRequestHeader(string name, string value) { }
        public string GetRequestHeader(string name) => string.Empty;
        public UnityWebRequestAsyncOperation SendWebRequest() => null;
        public void Abort() { }
        public void Dispose() { }
        public static UnityWebRequest Get(string uri) => null;
        public static UnityWebRequest Post(string uri, string postData) => null;
        public static UnityWebRequest Put(string uri, string bodyData) => null;
        public static UnityWebRequest Delete(string uri) => null;
        public static UnityWebRequest Get(string uri, DownloadHandler downloadHandler) => null;
    }
}
