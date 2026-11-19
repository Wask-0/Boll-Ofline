#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEditor.Android;

namespace Boll.EditorTools
{
    /// <summary>Настройки Android и подписанная сборка APK. Меню: Boll → Build Android APK; из консоли: -executeMethod Boll.EditorTools.BollBuild.Build.</summary>
    public static class BollBuild
    {
        const string KeysDirDefault = @"D:\создание игр\Boll Keys";   // вне репозитория
        public const string Output = "Builds/BollOffline.apk";

        [MenuItem("Boll/Apply Android Settings")]
        public static void ApplySettings()
        {
            var t = NamedBuildTarget.Android;
            PlayerSettings.companyName = "Boll";
            PlayerSettings.productName = "Boll offline";
            PlayerSettings.SetApplicationIdentifier(t, "com.boll.offline");
            PlayerSettings.SetScriptingBackend(t, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.SetManagedStrippingLevel(t, ManagedStrippingLevel.Minimal);
            PlayerSettings.stripEngineCode = true;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            AssetDatabase.SaveAssets();
        }

        static void Sign()
        {
            string dir = Environment.GetEnvironmentVariable("BOLL_KEYS_DIR") ?? KeysDirDefault;
            string ks = Path.Combine(dir, "boll-offline.keystore"), info = Path.Combine(dir, "keystore-info.txt");
            if (!File.Exists(ks) || !File.Exists(info)) throw new BuildFailedException("Нет ключа подписи: " + ks);
            string pw = null;
            foreach (string line in File.ReadAllLines(info)) if (line.StartsWith("password:")) pw = line.Substring(9).Trim();
            if (string.IsNullOrEmpty(pw)) throw new BuildFailedException("В keystore-info.txt нет пароля");
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ks.Replace('\\', '/'); PlayerSettings.Android.keystorePass = pw;
            PlayerSettings.Android.keyaliasName = "boll"; PlayerSettings.Android.keyaliasPass = pw;
        }

        [MenuItem("Boll/Build Android APK")]
        public static void Build()
        {
            ApplySettings();
            Sign();
            Directory.CreateDirectory("Builds");
            var opt = new BuildPlayerOptions
            {
                scenes = Array.ConvertAll(Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path),
                locationPathName = Path.GetFullPath(Output), target = BuildTarget.Android, options = BuildOptions.None
            };
            BuildReport r = BuildPipeline.BuildPlayer(opt);
            // пароль ключа не должен остаться в настройках проекта
            PlayerSettings.Android.keystorePass = ""; PlayerSettings.Android.keyaliasPass = ""; PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.keystoreName = "";
            Debug.Log("BOLLBUILD " + r.summary.result + " size=" + (r.summary.totalSize / 1048576) + "MB errors=" + r.summary.totalErrors + " time=" + r.summary.totalTime);
            if (r.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Сборка не удалась: " + r.summary.result);
        }
    }

    /// <summary>Отключает резервное копирование Android (сохранения не переносятся между телефонами).</summary>
    public sealed class DisableBackup : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string root = Directory.GetParent(path).FullName;
            foreach (string dir in new[] { path, Path.Combine(root, "launcher") })
            {
                string f = Path.Combine(dir, "src/main/AndroidManifest.xml");
                if (!File.Exists(f)) continue;
                string x = File.ReadAllText(f);
                if (!x.Contains("<application")) continue;
                x = Regex.Replace(x, "\\sandroid:allowBackup=\"[^\"]*\"", "");
                x = Regex.Replace(x, "\\sandroid:fullBackupContent=\"[^\"]*\"", "");
                x = x.Replace("<application", "<application android:allowBackup=\"false\" android:fullBackupContent=\"false\"");
                File.WriteAllText(f, x);
            }
        }
    }
}
#endif
