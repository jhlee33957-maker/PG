using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 웹(WebGL) 빌드를 F:\PG\BuildWeb 에 만든다. 포트폴리오 사이트에 넣어 브라우저에서 플레이하려고 쓴다.
/// 메뉴 Tools/Build Web 이나 배치 모드 -executeMethod WebBuild.Build 로 실행한다.
/// GitHub Pages는 압축 헤더를 붙여 주지 않으므로 Brotli + 압축 해제 대체(Decompression Fallback)로 만든다.
/// </summary>
public static class WebBuild
{
    private const string OutputPath = "BuildWeb";

    [MenuItem("Tools/Build Web")]
    public static void Build()
    {
        BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
        BuildTargetGroup previousGroup = BuildPipeline.GetBuildTargetGroup(previousTarget);

        // Brotli가 Gzip보다 작다. Pages가 헤더를 안 붙여 줘도 아래 Decompression Fallback이 브라우저에서 풀어 준다
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        // 코드(wasm)를 실행 속도 대신 용량 우선으로 최적화한다 (빌드 시간은 늘어남)
        EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.WebGL), "CodeOptimization", "DiskSizeLTO");
        PlayerSettings.WebGL.decompressionFallback = true;
        // 캔버스를 창 크기에 맞추는 템플릿 (Assets/WebGLTemplates/CoffeeSpecFill)
        PlayerSettings.WebGL.template = "PROJECT:CoffeeSpecFill";
        // 빌드마다 파일 이름이 달라지게 해서 브라우저가 예전 wasm과 새 코드를 섞어 불러오지 않게 한다
        PlayerSettings.WebGL.nameFilesAsHashes = true;

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputPath,
            target = BuildTarget.WebGL,
            targetGroup = BuildTargetGroup.WebGL,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        // 빌드가 실패해도 고정 폰트로 바꿔 둔 파일이 남지 않게 한다 (BuildFontBaker)
        BuildFontBaker.RestoreFonts();
        Debug.Log($"[WebBuild] 결과: {report.summary.result}, 크기 {report.summary.totalSize / (1024 * 1024)}MB, 오류 {report.summary.totalErrors}개");

        // 에디터를 다시 열었을 때 원래 플랫폼(윈도우)으로 돌아와 있게 한다
        if (previousTarget != BuildTarget.WebGL)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(previousGroup, previousTarget);
        }

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
