// Hand-written stubs of the UnityEngine API used by the Ludo Legends client.
// These exist only so the real client sources can be type-checked without a Unity install.
// Signatures follow the public Unity 6 API for everything the client touches.
#pragma warning disable 0067, 0649, 0414

using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }

        public static void Destroy(Object target) { }
        public static void Destroy(Object target, float delay) { }
        public static void DestroyImmediate(Object target) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static T FindObjectOfType<T>() where T : Object => null;
        public static T[] FindObjectsOfType<T>() where T : Object => Array.Empty<T>();
        public static Object Instantiate(Object original) => null;
        public static T Instantiate<T>(T original) where T : Object => original;
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
    }

    public enum HideFlags { None = 0, HideInHierarchy = 1, DontSave = 4 }

    public struct LayerMask
    {
        public int value { get; set; }
        public static implicit operator int(LayerMask mask) => mask.value;
        public static implicit operator LayerMask(int intVal) => new LayerMask { value = intVal };
        public static int NameToLayer(string layerName) => 0;
        public static string LayerToName(int layer) => string.Empty;
    }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0,
        BeforeSceneLoad = 1,
        AfterAssembliesLoaded = 2,
        BeforeSplashScreen = 3,
        SubsystemRegistration = 4
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
        public RuntimeInitializeLoadType loadType { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class CreateAssetMenuAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) { } }

    public class Component : Object
    {
        public GameObject gameObject { get; }
        public Transform transform { get; }
        public T GetComponent<T>() => default;
        public Component GetComponent(Type type) => null;
        public T GetComponentInChildren<T>() => default;
        public T[] GetComponentsInChildren<T>() => Array.Empty<T>();
        public T GetComponentInParent<T>() => default;
        public bool TryGetComponent<T>(out T component) { component = default; return false; }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled { get; }
    }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => null;
        public Coroutine StartCoroutine(string methodName) => null;
        public void StopCoroutine(Coroutine routine) { }
        public void StopCoroutine(IEnumerator routine) { }
        public void StopAllCoroutines() { }
        public void Invoke(string methodName, float time) { }
        public void CancelInvoke() { }
        public static void print(object message) { }
    }

    public class Coroutine { }
    public class YieldInstruction { }
    public sealed class WaitForSeconds : YieldInstruction { public WaitForSeconds(float seconds) { } }
    public sealed class WaitForSecondsRealtime : CustomYieldInstruction { public WaitForSecondsRealtime(float seconds) { } public override bool keepWaiting => false; }
    public sealed class WaitForEndOfFrame : YieldInstruction { }
    public sealed class WaitForFixedUpdate : YieldInstruction { }
    public abstract class CustomYieldInstruction : IEnumerator
    {
        public abstract bool keepWaiting { get; }
        public object Current => null;
        public bool MoveNext() => keepWaiting;
        public void Reset() { }
    }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }

        public Transform transform { get; }
        public bool activeSelf { get; }
        public bool activeInHierarchy { get; }
        public LayerMask layer { get; set; }
        public string tag { get; set; }

        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component => null;
        public Component AddComponent(Type componentType) => null;
        public T GetComponent<T>() => default;
        public T GetComponentInChildren<T>() => default;
        public static GameObject Find(string name) => null;
        public static GameObject FindWithTag(string tag) => null;
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 lossyScale { get; }
        public Vector3 eulerAngles { get; set; }
        public Vector3 localEulerAngles { get; set; }
        public Transform parent { get; set; }
        public Transform root { get; }
        public int childCount { get; }

        public void SetParent(Transform parent) { }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public void SetAsFirstSibling() { }
        public void SetAsLastSibling() { }
        public void SetSiblingIndex(int index) { }
        public int GetSiblingIndex() => 0;
        public Transform GetChild(int index) => null;
        public Transform Find(string name) => null;
        public void DetachChildren() { }
        public void Translate(Vector3 translation) { }
        public void Rotate(Vector3 eulers) { }
        public void Rotate(float xAngle, float yAngle, float zAngle) { }
        public void Rotate(Vector3 axis, float angle) { }
        public IEnumerator GetEnumerator() => Array.Empty<Transform>().GetEnumerator();
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector3 anchoredPosition3D { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Rect rect { get; }
        public void SetInsetAndSizeFromParentEdge(Edge edge, float inset, float size) { }
        public void SetSizeWithCurrentAnchors(Axis axis, float size) { }
        public void ForceUpdateRectTransforms() { }
        public enum Edge { Left, Right, Top, Bottom }
        public enum Axis { Horizontal, Vertical }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
        public static Vector2 up => new Vector2(0f, 1f);
        public static Vector2 down => new Vector2(0f, -1f);
        public static Vector2 left => new Vector2(-1f, 0f);
        public static Vector2 right => new Vector2(1f, 0f);
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;
        public Vector2 normalized => this;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a;
        public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) => a;
        public static float Distance(Vector2 a, Vector2 b) => 0f;
        public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDelta) => current;
        public static Vector2 Scale(Vector2 a, Vector2 b) => a;
        public static Vector2 ClampMagnitude(Vector2 v, float max) => v;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
        public override bool Equals(object obj) => obj is Vector2 v && this == v;
        public override int GetHashCode() => 0;
        public override string ToString() => "(" + x + ", " + y + ")";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0f; }
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public float magnitude => 0f;
        public Vector3 normalized => this;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a;
        public static float Distance(Vector3 a, Vector3 b) => 0f;
        public static Vector3 Scale(Vector3 a, Vector3 b) => a;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => a * d;
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public override bool Equals(object obj) => obj is Vector3 v && this == v;
        public override int GetHashCode() => 0;
        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Quaternion
    {
        public static Quaternion identity => new Quaternion();
        public static Quaternion Euler(float x, float y, float z) => identity;
        public static Quaternion Euler(Vector3 euler) => identity;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1f);
        public static Color red => new Color(1f, 0f, 0f, 1f);
        public static Color green => new Color(0f, 1f, 0f, 1f);
        public static Color blue => new Color(0f, 0f, 1f, 1f);
        public static Color yellow => new Color(1f, 1f, 0f, 1f);
        public static Color cyan => new Color(0f, 1f, 1f, 1f);
        public static Color magenta => new Color(1f, 0f, 1f, 1f);
        public static Color Lerp(Color a, Color b, float t) => a;
        public static Color operator *(Color a, float d) => new Color(a.r * d, a.g * d, a.b * d, a.a * d);
        public static Color operator *(Color a, Color b) => a;
        public static Color operator +(Color a, Color b) => a;
        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
        public static implicit operator Color(Vector4 v) => new Color(v.x, v.y, v.z, v.w);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => new Color32(0, 0, 0, 255);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public Vector2 center => new Vector2(x + width / 2f, y + height / 2f);
        public Vector2 size => new Vector2(width, height);
        public static Rect zero => new Rect(0f, 0f, 0f, 0f);
        public bool Contains(Vector2 point) => false;
    }

    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string htmlString, out Color color) { color = Color.white; return true; }
        public static string ToHtmlStringRGB(Color color) => "FFFFFF";
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public const float Infinity = float.PositiveInfinity;
        public const float Epsilon = 1.401298E-45f;
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int f) => Math.Abs(f);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float value) => 0f;
        public static float MoveTowards(float current, float target, float maxDelta) => current;
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Exp(float power) => (float)Math.Exp(power);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static float Sign(float f) => Math.Sign(f);
        public static float Repeat(float t, float length) => t - Floor(t / length) * length;
        public static float PingPong(float t, float length) => 0f;
        public static float SmoothStep(float from, float to, float t) => t;
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-5f;
    }

    public static class Time
    {
        public static float time { get; }
        public static float deltaTime { get; }
        public static float unscaledTime { get; }
        public static float unscaledDeltaTime { get; }
        public static float realtimeSinceStartup { get; }
        public static float fixedDeltaTime { get; }
        public static int frameCount { get; }
        public static float timeScale { get; set; }
    }

    public static class Screen
    {
        public static int width { get; }
        public static int height { get; }
        public static float dpi { get; }
        public static SleepTimeout sleepTimeout { get; set; }
        public static bool fullScreen { get; set; }
        public static ScreenOrientation orientation { get; set; }
        public static Rect safeArea { get; }
    }

    public enum SleepTimeout { NeverSleep = -1, SystemSetting = -2 }
    public enum ScreenOrientation { Portrait = 1, PortraitUpsideDown = 2, LandscapeLeft = 3, LandscapeRight = 4, AutoRotation = 5 }

    public static class Application
    {
        public static bool isPlaying { get; }
        public static bool isBatchMode { get; }
        public static string persistentDataPath { get; }
        public static string dataPath { get; }
        public static string version { get; }
        public static string identifier { get; }
        public static int targetFrameRate { get; set; }
        public static RuntimePlatform platform { get; }
        public static event Action<bool> focusChanged;
        public enum RuntimePlatform { Android = 11, IPhonePlayer = 8, WindowsPlayer = 2, OSXPlayer = 1, LinuxPlayer = 13, WebGLPlayer = 17 }
        public static void Quit() { }
        public static void OpenURL(string url) { }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void Log(object message, Object context) { }
        public static void LogWarning(object message) { }
        public static void LogWarning(object message, Object context) { }
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
        public static void LogException(Exception exception) { }
        public static void Assert(bool condition) { }
    }

    public static class PlayerPrefs
    {
        public static void SetString(string key, string value) { }
        public static string GetString(string key, string defaultValue) => defaultValue;
        public static string GetString(string key) => string.Empty;
        public static void SetInt(string key, int value) { }
        public static int GetInt(string key, int defaultValue) => defaultValue;
        public static void SetFloat(string key, float value) { }
        public static float GetFloat(string key, float defaultValue) => defaultValue;
        public static bool HasKey(string key) => false;
        public static void DeleteKey(string key) { }
        public static void DeleteAll() { }
        public static void Save() { }
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
        public static Object Load(string path) => null;
        public static T[] LoadAll<T>(string path) where T : Object => Array.Empty<T>();
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
    }

    public class TextAsset : Object
    {
        public string text { get; }
        public byte[] bytes { get; }
    }

    public class Font : Object
    {
        public static Font CreateDynamicFontFromOSFont(string fontname, int size) => null;
        public int fontSize { get; set; }
    }

    public class Texture : Object
    {
        public int width { get; set; }
        public int height { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
    }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain) { }
        public void SetPixel(int x, int y, Color color) { }
        public void SetPixels(Color[] colors) { }
        public void Apply() { }
        public void Apply(bool updateMipmaps) { }
    }

    public enum TextureFormat { RGBA32 = 4, ARGB32 = 5, RGB24 = 3, Alpha8 = 1 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1 }

    public enum SpriteMeshType { FullRect = 0, Tight = 1 }

    public class Sprite : Object
    {
        public Rect rect { get; }
        public Texture2D texture { get; }
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot) => null;
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit) => null;
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType, Vector4 border) => null;
    }

    public class AudioClip : Object
    {
        public float length { get; }
        public int samples { get; }
        public int channels { get; }
        public int frequency { get; }
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) => null;
        public bool SetData(float[] data, int offsetSamples) => true;
        public bool GetData(float[] data, int offsetSamples) => true;
    }

    public class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public float volume { get; set; }
        public float pitch { get; set; }
        public bool loop { get; set; }
        public bool mute { get; set; }
        public bool playOnAwake { get; set; }
        public bool isPlaying { get; }
        public void Play() { }
        public void Stop() { }
        public void Pause() { }
        public void PlayOneShot(AudioClip clip) { }
        public void PlayOneShot(AudioClip clip, float volumeScale) { }
    }

    public class AudioListener : Behaviour { }

    public static class Random
    {
        public static int Range(int minInclusive, int maxExclusive) => minInclusive;
        public static float Range(float minInclusive, float maxInclusive) => minInclusive;
        public static float value => 0f;
        public static void InitState(int seed) { }
    }

    public static class Input
    {
        public static bool multiTouchEnabled { get; set; }
        public static bool anyKeyDown { get; }
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetMouseButtonDown(int button) => false;
        public static Vector3 mousePosition { get; }
        public static int touchCount { get; }
        public static Touch GetTouch(int index) => default;
    }

    public struct Touch
    {
        public int fingerId { get; }
        public Vector2 position { get; }
        public TouchPhase phase { get; }
    }

    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
    public enum KeyCode { None = 0, Escape = 27, Space = 32, Return = 13, A = 97 }

    public static class QualitySettings
    {
        public static int vSyncCount { get; set; }
        public static int antiAliasing { get; set; }
        public static int targetFrameRate { get; set; }
        public static void SetQualityLevel(int index) { }
    }

    public static class JsonUtility
    {
        public static string ToJson(object obj) => string.Empty;
        public static string ToJson(object obj, bool prettyPrint) => string.Empty;
        public static T FromJson<T>(string json) => default;
        public static void FromJsonOverwrite(string json, object objectToOverwrite) { }
    }

    public static class SystemInfo
    {
        public static string deviceModel { get; }
        public static int systemMemorySize { get; }
        public static string operatingSystem { get; }
    }

    public static class Cursor
    {
        public static bool visible { get; set; }
    }

    public static class AudioSettings
    {
        public static bool muteAudioListener { get; set; }
    }
}
