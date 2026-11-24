#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 성능 측정용 안드로이드 개발 빌드(APK)를 만든다.
/// - Development Build + Autoconnect Profiler 를 켜고 APK로 빌드한 뒤, 끝나면 원래 설정으로 되돌린다.
/// - 결과는 Logs/dev_build_result.txt 에 기록한다. (빌드 중에는 에디터가 응답하지 않을 수 있다)
/// 서명 키스토어 비밀번호는 Project Settings > Player > Publishing Settings 에 입력돼 있어야 한다.
/// </summary>
public static class DevBuild
{
    public const string OutputPath = "Builds/Android/Meowgician_dev.apk";
    public const string ResultPath = "Logs/dev_build_result.txt";

    [MenuItem("Tools/Build/Android Dev APK (profiling)")]
    public static void BuildAndroidDevApk()
    {
        bool oldDevelopment = EditorUserBuildSettings.development;
        bool oldConnect = EditorUserBuildSettings.connectProfiler;
        bool oldBundle = EditorUserBuildSettings.buildAppBundle;
        bool oldTiming = PlayerSettings.enableFrameTimingStats;
        var sb = new StringBuilder();

        try
        {
            EditorUserBuildSettings.development = true;
            EditorUserBuildSettings.connectProfiler = true;
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.enableFrameTimingStats = true;
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            if (File.Exists(OutputPath)) File.Delete(OutputPath);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.ConnectWithProfiler
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            sb.AppendLine("result=" + summary.result);
            sb.AppendLine("output=" + summary.outputPath);
            sb.AppendLine("sizeMB=" + (summary.totalSize / (1024.0 * 1024.0)).ToString("F1"));
            sb.AppendLine("seconds=" + summary.totalTime.TotalSeconds.ToString("F0"));
            sb.AppendLine("errors=" + summary.totalErrors + " warnings=" + summary.totalWarnings);
            foreach (var step in report.steps)
                foreach (var msg in step.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        sb.AppendLine("ERROR: " + msg.content);
        }
        catch (Exception e)
        {
            sb.AppendLine("result=EXCEPTION");
            sb.AppendLine(e.ToString());
        }
        finally
        {
            EditorUserBuildSettings.development = oldDevelopment;
            EditorUserBuildSettings.connectProfiler = oldConnect;
            EditorUserBuildSettings.buildAppBundle = oldBundle;
            PlayerSettings.enableFrameTimingStats = oldTiming;
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ResultPath, sb.ToString());
        }
    }
}
#endif
