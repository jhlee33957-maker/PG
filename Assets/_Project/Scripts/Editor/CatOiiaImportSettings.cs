using UnityEditor;
using UnityEngine;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// Sprite/CatOiia.png(오이아이오 식빵 고양이 회전 8컷)를 32 PPU, 점 필터, 16x13 컷으로 자른다.
    /// CatOiiaDance가 형광색 마스크를 만들 수 있게 읽기 가능으로 둔다.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class CatOiiaImportSettings : AssetPostprocessor
    {
        private const string TexturePath = "Assets/_Project/Sprite/CatOiia.png";
        private const string SpriteName = "CatOiia";
        private const int CellWidth = 16, CellHeight = 13, FrameCount = 8, PixelsPerUnit = 32;

        // 스크립트보다 그림이 먼저 들어왔으면 한 번 다시 불러온다.
        static CatOiiaImportSettings()
        {
            EditorApplication.delayCall += ReimportIfStale;
        }

        private static void ReimportIfStale()
        {
            if (!(AssetImporter.GetAtPath(TexturePath) is TextureImporter importer)) return;
            var metas = importer.spritesheet;
            bool stale = importer.spriteImportMode != SpriteImportMode.Multiple || !importer.isReadable
                         || importer.filterMode != FilterMode.Point || metas == null || metas.Length != FrameCount;
            for (int i = 0; !stale && i < metas.Length; i++)
            {
                var r = metas[i].rect;
                stale = metas[i].name != $"{SpriteName}_{i}" || (int)r.width != CellWidth || (int)r.height != CellHeight || (int)r.x != i * CellWidth;
            }
            if (stale) AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
        }

        private void OnPreprocessTexture()
        {
            if (!string.Equals(assetPath, TexturePath, System.StringComparison.OrdinalIgnoreCase)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = true;
            importer.wrapMode = TextureWrapMode.Clamp;

            var metas = new SpriteMetaData[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                metas[i] = new SpriteMetaData
                {
                    name = $"{SpriteName}_{i}",
                    rect = new Rect(i * CellWidth, 0f, CellWidth, CellHeight),
                    alignment = (int)SpriteAlignment.BottomCenter,
                    pivot = new Vector2(0.5f, 0f)
                };
            }
            importer.spritesheet = metas;

            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            platform.format = TextureImporterFormat.RGBA32;
            importer.SetPlatformTextureSettings(platform);
        }
    }
}
