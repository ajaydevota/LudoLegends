// Stubs for the UnityEditor APIs touched by Assets/Scripts/Editor/BuildScript.cs.
// Namespaces mirror Unity's real layout: UnityEditor.Build, UnityEditor.Build.Reporting.
#pragma warning disable 0067, 0649, 0414

using System;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
        public MenuItem(string itemName, bool isValidateFunction) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
    }

    public enum BuildTarget { Android = 13, iOS = 9, StandaloneWindows = 5, StandaloneOSX = 2, StandaloneLinux64 = 24 }
    public enum BuildTargetGroup { Unknown = 0, Standalone = 1, Android = 7, iOS = 4 }
    public enum BuildOptions { None = 0, Development = 1, AutoRunPlayer = 4, ShowBuiltPlayer = 8 }
    public enum ScriptingImplementation { Mono2x = 0, IL2CPP = 1, WinRTDotNET = 2, CoreCLR = 3 }
    public enum AndroidSdkVersions { AndroidApiLevelAuto = 0, AndroidApiLevel23 = 23, AndroidApiLevel33 = 33, AndroidApiLevel34 = 34, AndroidApiLevel35 = 35 }
    public enum UIOrientation { Portrait = 0, PortraitUpsideDown = 1, LandscapeRight = 2, LandscapeLeft = 3, AutoRotation = 4 }

    [Flags]
    public enum AndroidArchitecture
    {
        None = 0,
        ARMv7 = 1,
        ARM64 = 2,
        X86 = 4,
        X86_64 = 8,
        All = ~0
    }

    public sealed class PlayerSettings
    {
        public static string companyName { get; set; }
        public static string productName { get; set; }
        public static string bundleVersion { get; set; }
        public static UIOrientation defaultInterfaceOrientation { get; set; }
        public static bool allowedAutorotateToLandscapeLeft { get; set; }
        public static bool allowedAutorotateToLandscapeRight { get; set; }
        public static bool allowedAutorotateToPortrait { get; set; }
        public static bool allowedAutorotateToPortraitUpsideDown { get; set; }
        public static void SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget target, string identifier) { }
        public static void SetApplicationIdentifier(BuildTargetGroup group, string identifier) { }
        public static void SetScriptingBackend(UnityEditor.Build.NamedBuildTarget target, ScriptingImplementation backend) { }
        public static void SetScriptingBackend(BuildTargetGroup group, ScriptingImplementation backend) { }
        public static void SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget target, UnityEditor.Build.ManagedStrippingLevel level) { }
        public static void SetManagedStrippingLevel(BuildTargetGroup group, UnityEditor.Build.ManagedStrippingLevel level) { }

        /// <summary>Unity exposes these as a nested static class, accessed as PlayerSettings.Android.</summary>
        public static class Android
        {
            public static int bundleVersionCode { get; set; }
            public static AndroidSdkVersions minSdkVersion { get; set; }
            public static AndroidSdkVersions targetSdkVersion { get; set; }
            public static AndroidArchitecture targetArchitectures { get; set; }
            public static bool useCustomKeystore { get; set; }
            public static string keystoreName { get; set; }
            public static string keystorePass { get; set; }
            public static string keyaliasName { get; set; }
            public static string keyaliasPass { get; set; }
            public static bool optimizedFramePacing { get; set; }
        }
    }

    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public BuildTarget target { get; set; }
        public BuildTargetGroup targetGroup { get; set; }
        public BuildOptions options { get; set; }
    }

    public static class BuildPipeline
    {
        public static UnityEditor.Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => null;
    }

    public sealed class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene() { }
        public EditorBuildSettingsScene(string path, bool enabled) { this.path = path; this.enabled = enabled; }
        public string path { get; set; }
        public bool enabled { get; set; }
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
    }

    public static class EditorUserBuildSettings
    {
        public static bool buildAppBundle { get; set; }
        public static bool development { get; set; }
    }

    public static class AssetDatabase
    {
        public static void Refresh() { }
        public static void SaveAssets() { }
        public static string[] FindAssets(string filter) => Array.Empty<string>();
        public static string GUIDToAssetPath(string guid) => string.Empty;
    }

    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }

    public enum NewSceneMode { Single = 0, Additive = 1 }

    public static class EditorApplication
    {
        public static bool isBatchMode { get; }
        public static void Exit(int returnValue) { }
    }
}

namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene NewScene(UnityEditor.NewSceneSetup setup, UnityEditor.NewSceneMode mode)
            => default;
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string path) => true;
        public static bool SaveOpenScenes() => true;
        public static UnityEngine.SceneManagement.Scene OpenScene(string path) => default;
    }
}

namespace UnityEditor.Build
{
    public enum ManagedStrippingLevel { Disabled = 0, Low = 1, Medium = 2, High = 3, Minimal = 4 }

    public struct NamedBuildTarget
    {
        public static NamedBuildTarget Android => new NamedBuildTarget();
        public static NamedBuildTarget Standalone => new NamedBuildTarget();
        public string TargetName => "Android";
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name { get; set; }
        public string path { get; }
        public bool IsValid() => true;
    }

    public static class SceneManager
    {
        public static Scene GetActiveScene() => default;
        public static void LoadScene(string sceneName) { }
        public static void LoadScene(int sceneBuildIndex) { }
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown = 0, Succeeded = 1, Failed = 2, Cancelled = 3 }

    public sealed class BuildSummary
    {
        public BuildResult result { get; }
        public ulong totalSize { get; }
        public int totalErrors { get; }
        public int totalWarnings { get; }
        public TimeSpan totalTime { get; }
    }

    public sealed class BuildReport
    {
        public BuildSummary summary { get; }
    }
}
