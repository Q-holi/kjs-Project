using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kjs.Editor
{
    public static class BuildCommands
    {
        public const string ScenePath = "Assets/Scenes/SharedWorld.unity";

        [MenuItem("KJS/Open Shared World")]
        public static void OpenWorld()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("KJS/Build Current Desktop Target")]
        public static void BuildDesktop()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            string defaultPath;
            switch (target)
            {
                case BuildTarget.StandaloneWindows64: defaultPath = "Builds/Windows/kjs.exe"; break;
                case BuildTarget.StandaloneOSX: defaultPath = "Builds/macOS/kjs.app"; break;
                case BuildTarget.StandaloneLinux64: defaultPath = "Builds/Linux/kjs.x86_64"; break;
                default: throw new InvalidOperationException("Select a Windows, macOS or Linux desktop target first.");
            }
            string output = Argument("-kjsBuildPath") ?? defaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.companyName = "Kjs";
            PlayerSettings.productName = "kjs Project";
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode3D;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = target, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Build failed: " + report.summary.result);
            Debug.Log("KJS build succeeded: " + output);
        }

        private static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}
