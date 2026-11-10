using System.Collections.Generic;
using CoffeeSpec.Core;
using UnityEditor;
using UnityEngine;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// Sprite/Object의 애니메이션 시트(PlayerCat, Pigeon, CatEnter, DoorFlap)를 점 필터, 32 PPU, 같은 너비 컷으로 자르고
    /// 각 프리팹의 기본 그림을 첫 컷(대기)으로 맞춘다. 실제 재생은 EntitySpriteAnimator가 한다.
    /// Resources 밖이라 실행 중에 이름으로 못 찾으므로, 자른 컷을 SpriteSheetLibrary 에셋에 담고
    /// 빌드 시작 때 미리 불러오도록 Player Settings의 Preloaded Assets에 넣어 둔다.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class EntitySpriteSheetPostprocessor : AssetPostprocessor
    {
        // 스크립트보다 그림이 먼저 임포트되면 컷이 예전 크기로 남는다. 컴파일이 끝난 뒤 한 번 확인해서 다시 자른다
        static EntitySpriteSheetPostprocessor()
        {
            EditorApplication.delayCall += ReimportIfStale;
            EditorApplication.delayCall += RefreshLibrary;
        }

        private static bool _refreshQueued;

        private static void QueueRefreshLibrary()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            EditorApplication.delayCall += RefreshLibrary;
        }

        // 각 시트의 컷을 번호 순서로 SpriteSheetLibrary에 담는다. 바뀐 게 있을 때만 저장한다
        private static void RefreshLibrary()
        {
            _refreshQueued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isUpdating || EditorApplication.isCompiling)
            {
                QueueRefreshLibrary();
                return;
            }

            var entries = new List<SpriteSheetLibrary.Sheet>();
            foreach (var sheet in Sheets)
            {
                var frames = new List<Sprite>();
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(sheet.TexturePath))
                    if (obj is Sprite sprite) frames.Add(sprite);
                if (frames.Count == 0)
                {
                    Debug.LogWarning($"[EntitySpriteSheetPostprocessor] {sheet.TexturePath} 에 컷이 없어 SpriteSheetLibrary에 넣지 못했다");
                    continue;
                }
                frames.Sort((a, b) => FrameIndex(a.name).CompareTo(FrameIndex(b.name)));
                entries.Add(new SpriteSheetLibrary.Sheet { Name = sheet.Name, Frames = frames.ToArray() });
            }

            var library = AssetDatabase.LoadAssetAtPath<SpriteSheetLibrary>(SpriteSheetLibrary.AssetPath);
            if (library == null)
            {
                string folder = System.IO.Path.GetDirectoryName(SpriteSheetLibrary.AssetPath).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Debug.LogWarning($"[EntitySpriteSheetPostprocessor] {folder} 폴더가 없어 SpriteSheetLibrary를 만들지 못했다");
                    return;
                }
                library = ScriptableObject.CreateInstance<SpriteSheetLibrary>();
                AssetDatabase.CreateAsset(library, SpriteSheetLibrary.AssetPath);
                Debug.Log($"[EntitySpriteSheetPostprocessor] {SpriteSheetLibrary.AssetPath} 를 만들었다");
            }

            if (library.SetSheets(entries.ToArray()))
            {
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
                Debug.Log($"[EntitySpriteSheetPostprocessor] SpriteSheetLibrary 갱신: 시트 {entries.Count}개");
            }

            EnsurePreloaded(library);
        }

        // 빌드에서는 Preloaded Assets에 들어 있어야 시작할 때 불러와져 코드가 찾을 수 있다
        private static void EnsurePreloaded(SpriteSheetLibrary library)
        {
            var preloaded = new List<Object>(PlayerSettings.GetPreloadedAssets());
            if (preloaded.Contains(library)) return;
            preloaded.RemoveAll(o => o == null || o is SpriteSheetLibrary);
            preloaded.Add(library);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            AssetDatabase.SaveAssets();
            Debug.Log("[EntitySpriteSheetPostprocessor] SpriteSheetLibrary를 Player Settings > Preloaded Assets에 넣었다");
        }

        private static int FrameIndex(string name)
        {
            int u = name.LastIndexOf('_');
            return u >= 0 && int.TryParse(name.Substring(u + 1), out int n) ? n : int.MaxValue;
        }

        private static void ReimportIfStale()
        {
            foreach (var sheet in Sheets)
            {
                if (!(AssetImporter.GetAtPath(sheet.TexturePath) is TextureImporter importer)) continue;
                var metas = importer.spritesheet;
                bool stale = importer.spriteImportMode != SpriteImportMode.Multiple || metas == null || metas.Length != sheet.FrameCount;
                for (int i = 0; !stale && i < metas.Length; i++)
                {
                    var r = metas[i].rect;
                    stale = metas[i].name != $"{sheet.Name}_{i}" || (int)r.width != sheet.CellWidth || (int)r.height != sheet.CellHeight || (int)r.x != i * sheet.CellWidth;
                }
                if (stale) AssetDatabase.ImportAsset(sheet.TexturePath, ImportAssetOptions.ForceUpdate);
            }
        }

        private readonly struct Sheet
        {
            internal readonly string Name, TexturePath, PrefabPath;
            internal readonly int CellWidth, CellHeight, FrameCount;

            internal Sheet(string name, string prefabPath, int cellWidth, int cellHeight, int frameCount)
            {
                Name = name;
                TexturePath = $"Assets/_Project/Sprite/Object/{name}.png";
                PrefabPath = prefabPath;
                CellWidth = cellWidth;
                CellHeight = cellHeight;
                FrameCount = frameCount;
            }
        }

        private const int PixelsPerUnit = 32;

        private static readonly Sheet[] Sheets =
        {
            // 대기 2, 걷기 3, 점프, 낙하
            new Sheet("PlayerCat", "Assets/_Project/Prefabs/Object/Player.prefab", 16, 20, 7),
            // 대기 1, 걷기 3, 날기 3
            new Sheet("Pigeon", "Assets/_Project/Prefabs/Object/Pigeon.prefab", 16, 16, 7),
            // 클리어 연출: 고양이가 캣도어로 들어가는 7컷 (프리팹 기본 그림은 건드리지 않는다)
            new Sheet("CatEnter", null, 28, 20, 7),
            // 클리어 연출: 문 플랩 0 열림 ~ 5 닫힘
            new Sheet("DoorFlap", null, 20, 40, 6),
        };

        private static bool TryFind(string path, out Sheet sheet)
        {
            foreach (var s in Sheets)
            {
                if (string.Equals(path, s.TexturePath, System.StringComparison.OrdinalIgnoreCase)) { sheet = s; return true; }
            }
            sheet = default;
            return false;
        }

        private void OnPreprocessTexture()
        {
            if (!TryFind(assetPath, out var sheet)) return;

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
            importer.spritePivot = new Vector2(0.5f, 0.5f);

            var metas = new SpriteMetaData[sheet.FrameCount];
            for (int i = 0; i < sheet.FrameCount; i++)
            {
                metas[i] = new SpriteMetaData
                {
                    name = $"{sheet.Name}_{i}",
                    rect = new Rect(i * sheet.CellWidth, 0f, sheet.CellWidth, sheet.CellHeight),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f)
                };
            }
            importer.spritesheet = metas;

            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            platform.format = TextureImporterFormat.RGBA32;
            importer.SetPlatformTextureSettings(platform);
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool touched = false;
            foreach (string asset in importedAssets)
            {
                if (TryFind(asset, out var sheet)) { AssignIdleToPrefab(sheet); touched = true; }
            }
            foreach (string asset in movedAssets)
            {
                if (TryFind(asset, out var sheet)) { AssignIdleToPrefab(sheet); touched = true; }
            }
            foreach (string asset in deletedAssets) if (TryFind(asset, out _)) touched = true;
            foreach (string asset in movedFromAssetPaths) if (TryFind(asset, out _)) touched = true;
            if (touched) QueueRefreshLibrary();
        }

        private static bool AssignIdleToPrefab(Sheet sheet)
        {
            // 연출 전용 시트는 프리팹에 넣지 않는다
            if (string.IsNullOrEmpty(sheet.PrefabPath)) return true;
            Sprite idle = null;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(sheet.TexturePath))
            {
                if (obj is Sprite sprite && sprite.name == $"{sheet.Name}_0") { idle = sprite; break; }
            }
            if (idle == null) return false;

            var root = PrefabUtility.LoadPrefabContents(sheet.PrefabPath);
            if (root == null) return false;
            try
            {
                var renderer = root.GetComponent<SpriteRenderer>();
                if (renderer == null) return false;
                if (renderer.sprite == idle) return true;
                renderer.sprite = idle;
                PrefabUtility.SaveAsPrefabAsset(root, sheet.PrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
