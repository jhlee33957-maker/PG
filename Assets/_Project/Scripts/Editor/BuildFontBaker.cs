using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 빌드(윈도우·웹 모두) 직전에 한글 동적 폰트를 게임에 쓰인 글자만 담은 고정 폰트로 바꿔,
/// 원본 폰트 파일(NotoSansKR 5.9MB, D2Coding 3.9MB)이 빌드에 들어가지 않게 한다.
/// 빌드가 끝나면 폰트 에셋 파일을 원래대로 되돌리므로 에디터에서는 계속 동적 폰트를 쓴다.
/// 글자는 빌드할 때마다 다시 모으므로 새 문구를 넣어도 따로 할 일은 없다.
/// 빌드가 중간에 끊겨도 Library/FontBakeBackup 에 남은 원본을 다음 에디터 실행 때 되돌린다.
/// </summary>
[InitializeOnLoad]
public sealed class BuildFontBaker : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    private static readonly string[] FontAssets =
    {
        "Assets/_Project/Fonts/NotoSansKR-Regular SDF.asset",
        "Assets/_Project/Fonts/D2Coding-Ver1.4.0-20261003 SDF.asset",
    };

    // 고정 아틀라스 설정. 원래(90pt, 여백 9)대로면 아틀라스가 8장 넘게 필요해서 줄인다
    private const int SamplingSize = 44;
    private const int Padding = 6;
    private const int AtlasSize = 2048;

    private const string BackupFolder = "Library/FontBakeBackup";

    public int callbackOrder => 0;

    static BuildFontBaker()
    {
        // 지난 빌드가 끊겨 고정 폰트로 남아 있으면 되돌린다
        if (Directory.Exists(BackupFolder) && Directory.GetFiles(BackupFolder).Length > 0)
        {
            EditorApplication.delayCall += RestoreFonts;
        }
    }

    public void OnPreprocessBuild(BuildReport report) => BakeFonts();

    public void OnPostprocessBuild(BuildReport report) => RestoreFonts();

    private static void BakeFonts()
    {
        RestoreFonts();
        string characters = CollectUsedCharacters();
        Debug.Log($"[FontBake] 빌드 폰트에 넣을 글자 {characters.Length}개");
        Directory.CreateDirectory(BackupFolder);

        foreach (string path in FontAssets)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null) continue;
            Font source = font.sourceFontFile;
            if (source == null || font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            {
                Debug.LogWarning($"[FontBake] 동적 폰트가 아니라 건너뜀: {path}");
                continue;
            }

            string backup = BackupPath(path);
            File.Copy(path, backup, true);
            try
            {
                if (FontEngine.LoadFontFace(source, SamplingSize) != FontEngineError.Success)
                    throw new Exception("폰트 면을 불러오지 못함");

                SetInternal(font, "faceInfo", FontEngine.GetFaceInfo());
                SetInternal(font, "atlasPadding", Padding);
                SetInternal(font, "atlasWidth", AtlasSize);
                SetInternal(font, "atlasHeight", AtlasSize);
                font.isMultiAtlasTexturesEnabled = true; // 혹시 한 장을 넘으면 다음 장에 이어서 담는다
                font.ClearFontAssetData(true);

                Material material = font.material;
                material.SetFloat(ShaderUtilities.ID_GradientScale, Padding + 1);
                material.SetFloat(ShaderUtilities.ID_TextureWidth, AtlasSize);
                material.SetFloat(ShaderUtilities.ID_TextureHeight, AtlasSize);

                font.TryAddCharacters(characters, out string missing);
                font.atlasPopulationMode = AtlasPopulationMode.Static; // 원본 폰트 파일 참조가 빠진다
                material.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTextures[0]);

                EditorUtility.SetDirty(font);
                EditorUtility.SetDirty(material);
                foreach (Texture2D atlas in font.atlasTextures) if (atlas != null) EditorUtility.SetDirty(atlas);

                string missingVisible = new string(missing.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
                Debug.Log($"[FontBake] 고정 폰트: {path} 글자 {font.characterTable.Count}개, 아틀라스 {font.atlasTextures.Length}장, 폰트에 없는 글자 [{missingVisible}]");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FontBake] 고정 폰트 실패, 원래 폰트로 빌드: {path} {e}");
                RestoreOne(path, backup);
            }
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>고정 폰트로 바꿔 둔 파일을 원본으로 되돌린다. 바꾼 게 없으면 아무것도 하지 않는다.</summary>
    public static void RestoreFonts()
    {
        foreach (string path in FontAssets)
        {
            string backup = BackupPath(path);
            if (File.Exists(backup)) RestoreOne(path, backup);
        }
    }

    private static void RestoreOne(string path, string backup)
    {
        File.Copy(backup, path, true);
        File.Delete(backup);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    private static string BackupPath(string path) => Path.Combine(BackupFolder, Path.GetFileName(path));

    private static void SetInternal(object target, string propertyName, object value)
    {
        Type type = target.GetType();
        while (type != null)
        {
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null && property.SetMethod != null)
            {
                property.SetValue(target, value);
                return;
            }
            type = type.BaseType;
        }
        throw new MissingMemberException(target.GetType().Name, propertyName);
    }

    /// <summary>
    /// 게임에 나올 수 있는 글자를 모은다: 씬·프리팹·에셋·데이터 파일의 글자(\uXXXX 이스케이프 포함), 런타임 코드의 문자열 리터럴, 기본 ASCII.
    /// </summary>
    private static string CollectUsedCharacters()
    {
        var set = new HashSet<char>();
        for (char c = ' '; c <= '~'; c++) set.Add(c);
        foreach (char c in "…·•←→↑↓▶◀■□○●×÷「」『』《》〈〉“”‘’—–") set.Add(c);

        var literal = new Regex("@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\\\r\\n])*\"");
        var escape = new Regex(@"\\u([0-9A-Fa-f]{4})");
        string[] exts = { ".unity", ".prefab", ".asset", ".txt", ".csv", ".json", ".cs" };

        foreach (string file in Directory.EnumerateFiles("Assets", "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (!exts.Contains(ext)) continue;
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/Editor/") || normalized.Contains("/TextMesh Pro/") || normalized.EndsWith(" SDF.asset")) continue;

            string text = File.ReadAllText(file, Encoding.UTF8);
            if (ext == ".cs")
            {
                text = string.Join("\n", literal.Matches(text).Cast<Match>().Select(m => m.Value));
            }
            text = escape.Replace(text, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            foreach (char c in text)
            {
                if (!char.IsControl(c) && !char.IsSurrogate(c)) set.Add(c);
            }
        }
        return new string(set.OrderBy(c => c).ToArray());
    }
}