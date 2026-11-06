using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 마젠타(#FF00FF) 배경을 투명으로 바꾸고 Sprite(Point, 압축 없음)로 임포트한다.
    /// Project 창에서 이미지를 선택한 뒤 메뉴: CoffeeSpec/Title/선택한 이미지 마젠타 → 투명 PNG
    /// 원본 파일을 덮어쓴다 (마젠타 원본이 필요하면 미리 복사해 두기).
    /// </summary>
    public static class TitleMagentaConverter
    {
        private const string MenuPath = "CoffeeSpec/Title/선택한 이미지 마젠타 → 투명 PNG";

        // JPG 압축이나 편집 툴 때문에 조금 어긋난 마젠타도 잡도록 약간의 허용 범위를 둔다
        private const byte Tolerance = 8;

        [MenuItem(MenuPath, false, 10)]
        private static void ConvertSelection()
        {
            List<string> paths = GetSelectedTexturePaths();
            int total = 0;
            foreach (string path in paths)
            {
                int changed = ConvertFile(path);
                total += Mathf.Max(0, changed);
                Debug.Log($"[TitleMagentaConverter] {path}: 투명으로 바꾼 픽셀 {changed}개");
            }
            AssetDatabase.Refresh();
            Debug.Log($"[TitleMagentaConverter] 이미지 {paths.Count}개 처리 완료 (투명 픽셀 합계 {total}개)");
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateConvertSelection()
        {
            return GetSelectedTexturePaths().Count > 0;
        }

        private static List<string> GetSelectedTexturePaths()
        {
            var result = new List<string>();
            foreach (Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase) && !result.Contains(path))
                {
                    result.Add(path);
                }
            }
            return result;
        }

        public static bool IsMagenta(Color32 c)
        {
            return c.a > 0 && c.r >= 255 - Tolerance && c.g <= Tolerance && c.b >= 255 - Tolerance;
        }

        public static string ToFullPath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        }

        /// <summary>파일에 마젠타 픽셀이 하나라도 있으면 true.</summary>
        public static bool HasMagenta(string assetPath)
        {
            string full = ToFullPath(assetPath);
            if (!File.Exists(full))
            {
                return false;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(full)))
                {
                    return false;
                }
                foreach (Color32 c in tex.GetPixels32())
                {
                    if (IsMagenta(c))
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        /// <summary>마젠타를 투명으로 바꿔 PNG 로 저장하고 Sprite 로 임포트한다. 바꾼 픽셀 수를 돌려준다 (실패 시 -1).</summary>
        public static int ConvertFile(string assetPath)
        {
            string full = ToFullPath(assetPath);
            if (!File.Exists(full))
            {
                Debug.LogError($"[TitleMagentaConverter] 파일이 없습니다: {assetPath}");
                return -1;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            int changed = 0;
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(full)))
                {
                    Debug.LogError($"[TitleMagentaConverter] 이미지를 읽지 못했습니다: {assetPath}");
                    return -1;
                }

                Color32[] pixels = tex.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (IsMagenta(pixels[i]))
                    {
                        pixels[i] = new Color32(0, 0, 0, 0);
                        changed++;
                    }
                }

                if (changed > 0)
                {
                    tex.SetPixels32(pixels);
                    tex.Apply();
                    File.WriteAllBytes(full, tex.EncodeToPNG());
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }

            ApplySpriteImportSettings(assetPath);
            return changed;
        }

        /// <summary>Sprite, Point 필터, 압축 없음, 밉맵 없음, 최대 4096.</summary>
        public static void ApplySpriteImportSettings(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }
    }
}
