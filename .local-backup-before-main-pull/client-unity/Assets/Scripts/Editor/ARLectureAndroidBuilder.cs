using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARLectureTranslator.EditorTools
{
    public static class ARLectureAndroidBuilder
    {
        private const string ScenePath = "Assets/Scenes/ARLecture_Task14_Raycast.unity";
        private const string ApkPath = "Builds/ARLecture_Task14_Raycast.apk";

        [MenuItem("AR Lecture Assistant/Build Android Task 1.4 APK")]
        public static void BuildTask14Apk()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                ARLectureSceneBuilder.CreateTask14Scene();
            }

            ARLectureSceneBuilder.ConfigureAndroidXR();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            EditorSceneManager.OpenScene(ScenePath);
            PlayerSettings.applicationIdentifier = "com.defaultcompany.arlectureassistant";
            PlayerSettings.productName = "AR Lecture Assistant";
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);

            System.IO.Directory.CreateDirectory("Builds");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Android build failed: {report.summary.result}");
            }

            Debug.Log($"Android APK created at {ApkPath}");
        }
    }
}
