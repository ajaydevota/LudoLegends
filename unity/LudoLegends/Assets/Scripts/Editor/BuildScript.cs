#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LudoLegends.EditorTools
{
    /// <summary>
    /// Android build automation: APK for testers, AAB for Google Play, from the menu or CI.
    ///
    /// Signing credentials are read from environment variables so keystores and passwords
    /// never live in the repository:
    ///   LUDO_KEYSTORE_PATH, LUDO_KEYSTORE_PASS, LUDO_KEYALIAS_NAME, LUDO_KEYALIAS_PASS
    ///
    /// CI usage:
    ///   Unity -quit -batchmode -projectPath unity/LudoLegends \
    ///         -executeMethod LudoLegends.EditorTools.BuildScript.BuildAabFromCI \
    ///         -logFile build.log
    /// </summary>
    public static class BuildScript
    {
        private const string PackageName = "com.yourstudio.ludolegends";
        private const string DefaultOutput = "Build";

        [MenuItem("Ludo Legends/Configure Android Player Settings")]
        public static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Your Studio";
            PlayerSettings.productName = "Ludo Legends";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            // Android 6.0 (API 23) is the floor; Unity 6 modules still support it.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;

            // Small, fast, battery-friendly defaults for a 2D board game.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);
            PlayerSettings.Android.optimizedFramePacing = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;

            Debug.Log("Android player settings configured for Ludo Legends.");
        }

        [MenuItem("Ludo Legends/Build APK")]
        public static void BuildApk()
        {
            Build(false);
        }

        [MenuItem("Ludo Legends/Build AAB (Play Store)")]
        public static void BuildAab()
        {
            Build(true);
        }

        public static void BuildApkFromCI()
        {
            Build(false);
        }

        public static void BuildAabFromCI()
        {
            Build(true);
        }

        private static void Build(bool appBundle)
        {
            ConfigurePlayer();

            EnsureStartupScene();

            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), DefaultOutput);
            Directory.CreateDirectory(outputDir);

            string extension = appBundle ? ".aab" : ".apk";
            string path = Path.Combine(outputDir, "LudoLegends-" + PlayerSettings.bundleVersion + extension);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Select(s => s.path).ToArray(),
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = appBundle ? BuildOptions.None : BuildOptions.None
            };

            // Unity refuses to build a player with zero scenes, even though this game
            // builds its whole UI at runtime. EnsureStartupScene() guarantees one exists.
            if (options.scenes == null || options.scenes.Length == 0)
            {
                throw new InvalidOperationException("No startup scene could be prepared; aborting the build.");
            }

            EditorUserBuildSettings.buildAppBundle = appBundle;
            ApplySigning();

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("Build succeeded: " + path + " (" + (summary.totalSize / (1024 * 1024)) + " MB)");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            else
            {
                Debug.LogError("Build failed: " + summary.result + " (" + summary.totalErrors + " errors)");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        /// <summary>
        /// Guarantees a valid startup scene exists and is in Build Settings.
        ///
        /// The game needs no authored scene (AppBootstrap builds the canvas, board and every
        /// screen at runtime), but Unity will not produce an APK without at least one scene in
        /// the build list, so an empty one is generated on demand and committed as
        /// Assets/Scenes/Main.unity on first build.
        /// </summary>
        [MenuItem("Ludo Legends/Create Startup Scene")]
        public static void EnsureStartupScene()
        {
            const string sceneDirectory = "Assets/Scenes";
            const string scenePath = sceneDirectory + "/Main.unity";

            if (!File.Exists(scenePath))
            {
                Directory.CreateDirectory(sceneDirectory);
                UnityEngine.SceneManagement.Scene scene =
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, scenePath);
                AssetDatabase.Refresh();
                Debug.Log("Created startup scene at " + scenePath + " (empty by design; the UI is built at runtime).");
            }

            bool listed = EditorBuildSettings.scenes != null &&
                          EditorBuildSettings.scenes.Any(s => s.path == scenePath);

            if (!listed)
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
                Debug.Log("Added " + scenePath + " to Build Settings.");
            }
        }

        private static void ApplySigning()
        {
            string keystore = Environment.GetEnvironmentVariable("LUDO_KEYSTORE_PATH");
            string keystorePass = Environment.GetEnvironmentVariable("LUDO_KEYSTORE_PASS");
            string aliasName = Environment.GetEnvironmentVariable("LUDO_KEYALIAS_NAME");
            string aliasPass = Environment.GetEnvironmentVariable("LUDO_KEYALIAS_PASS");

            if (string.IsNullOrEmpty(keystore) || !File.Exists(keystore))
            {
                Debug.LogWarning(
                    "No keystore configured. Unchecked 'Custom Keystore' uses the debug key, which Google Play rejects. " +
                    "Set LUDO_KEYSTORE_PATH / LUDO_KEYSTORE_PASS / LUDO_KEYALIAS_NAME / LUDO_KEYALIAS_PASS for a release build.");
                PlayerSettings.Android.useCustomKeystore = false;
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = aliasName;
            PlayerSettings.Android.keyaliasPass = aliasPass;
        }
    }
}
#endif
