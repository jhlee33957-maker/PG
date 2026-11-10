#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using CoffeeSpec.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// 배경 스프라이트 위에 빛 레이어(별, 달 후광 등)를 자식으로 붙여 은은하게 반짝이게 한다.
    /// 배경 이미지가 "BG_City.png"면 같은 폴더의 "BG_City_Glow.png" 한 장을 쓴다.
    /// 파일 수를 줄이려고 빛 레이어 여러 장을 빈 곳을 잘라 한 장에 모아 두었고, 각 조각이 시트의 어디에 있고
    /// 원래 배경의 어디에 겹쳐야 하는지는 PNG 안의 글자 정보("CoffeeSpecGlow")에 적혀 있다.
    ///   형식: 이름,시트x,시트y,너비,높이,배경x,배경y;...  (픽셀, 왼쪽 아래가 0)
    /// 메뉴는 그 정보대로 시트를 자르고, 조각마다 기준점을 옮겨서 자식 위치 (0,0)에 두면 배경과 1픽셀도 어긋나지 않게 한다.
    /// 조각 이름의 Stars/Moon으로 반짝임 설정을 고른다. 다시 실행하면 이전에 붙인 빛 레이어를 지우고 새로 붙인다.
    /// </summary>
    public static class BackgroundGlowMenu
    {
        private const string MenuPath = "CoffeeSpec/Background/선택한 배경에 빛 레이어 붙이기";
        private const string GlowSuffix = "_Glow";
        private const string LayoutKey = "CoffeeSpecGlow";
        private const string ChildPrefix = "Glow_";
        private const string UnlitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";

        private struct Piece
        {
            public string Name;
            public RectInt SheetRect;   // 시트 안 위치
            public Vector2Int BaseOrigin; // 배경 이미지 안에서 이 조각의 왼쪽 아래
        }

        [MenuItem(MenuPath)]
        private static void AttachGlowLayers()
        {
            GameObject target = Selection.activeGameObject;
            SpriteRenderer baseRenderer = target != null ? target.GetComponent<SpriteRenderer>() : null;
            if (baseRenderer == null || baseRenderer.sprite == null)
            {
                EditorUtility.DisplayDialog("빛 레이어", "배경 스프라이트(SpriteRenderer)가 붙은 오브젝트를 하이어라키에서 선택해 주세요.", "확인");
                return;
            }

            Sprite baseSprite = baseRenderer.sprite;
            Texture2D baseTexture = baseSprite.texture;
            // 조각 위치는 배경 이미지 전체 기준이라, 배경이 잘린 조각이면 위치가 어긋난다
            if (Mathf.RoundToInt(baseSprite.rect.width) != baseTexture.width || Mathf.RoundToInt(baseSprite.rect.height) != baseTexture.height)
            {
                EditorUtility.DisplayDialog("빛 레이어", "배경 스프라이트가 이미지 전체가 아니라서 빛 레이어를 맞출 수 없습니다. Sprite Mode를 Single로 두거나 이미지 전체 한 칸으로 잘라 주세요.", "확인");
                return;
            }

            string basePath = AssetDatabase.GetAssetPath(baseTexture);
            string folder = Path.GetDirectoryName(basePath).Replace('\\', '/');
            string sheetPath = $"{folder}/{Path.GetFileNameWithoutExtension(basePath)}{GlowSuffix}.png";
            if (!File.Exists(sheetPath))
            {
                EditorUtility.DisplayDialog("빛 레이어", $"{sheetPath} 파일이 없습니다.", "확인");
                return;
            }

            List<Piece> pieces = ReadLayout(sheetPath);
            if (pieces.Count == 0)
            {
                EditorUtility.DisplayDialog("빛 레이어", $"{sheetPath}에 조각 정보({LayoutKey})가 없습니다. 이미지를 다른 프로그램으로 다시 저장하면 지워질 수 있습니다.", "확인");
                return;
            }

            Dictionary<string, Sprite> sprites = ImportSheet(sheetPath, pieces, baseTexture, baseSprite);
            Material unlit = AssetDatabase.LoadAssetAtPath<Material>(UnlitMaterialPath);

            Undo.SetCurrentGroupName("빛 레이어 붙이기");
            int group = Undo.GetCurrentGroup();

            // 다시 실행해도 겹겹이 쌓이지 않게 이전 빛 레이어를 지운다
            for (int i = target.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = target.transform.GetChild(i);
                if (child.name.StartsWith(ChildPrefix, System.StringComparison.Ordinal) && child.GetComponent<SpriteGlowPulse>() != null)
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }

            int index = 0;
            foreach (Piece piece in pieces)
            {
                if (!sprites.TryGetValue(piece.Name, out Sprite sprite))
                {
                    Debug.LogWarning($"[BackgroundGlowMenu] 시트에서 '{piece.Name}' 조각을 찾지 못했습니다.");
                    continue;
                }

                var child = new GameObject(ChildPrefix + piece.Name);
                Undo.RegisterCreatedObjectUndo(child, "빛 레이어 붙이기");
                child.transform.SetParent(target.transform, false);

                var renderer = child.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingLayerID = baseRenderer.sortingLayerID;
                renderer.sortingOrder = baseRenderer.sortingOrder + 1;
                // 빛은 조명과 상관없이 스스로 빛나 보여야 해서 Unlit 재질을 쓴다. 없으면 배경 재질을 따른다
                renderer.sharedMaterial = unlit != null ? unlit : baseRenderer.sharedMaterial;

                var pulse = child.AddComponent<SpriteGlowPulse>();
                ApplyPreset(pulse, piece.Name, index++);
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(target.scene);
            Debug.Log($"[BackgroundGlowMenu] '{target.name}'에 빛 레이어 {index}개를 붙였습니다.", target);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanAttach()
        {
            return Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<SpriteRenderer>() != null;
        }

        // PNG의 tEXt 칸에서 조각 정보를 읽는다 (유니티 텍스처로 들여오면 이 정보가 사라지므로 원본 파일을 직접 읽는다)
        private static List<Piece> ReadLayout(string path)
        {
            var result = new List<Piece>();
            byte[] bytes = File.ReadAllBytes(path);
            int pos = 8; // PNG 서명 다음부터 칸(chunk)이 이어진다
            while (pos + 12 <= bytes.Length)
            {
                int length = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
                string type = Encoding.ASCII.GetString(bytes, pos + 4, 4);
                if (type == "tEXt")
                {
                    string text = Encoding.ASCII.GetString(bytes, pos + 8, length);
                    int zero = text.IndexOf('\0');
                    if (zero > 0 && text.Substring(0, zero) == LayoutKey)
                    {
                        foreach (string entry in text.Substring(zero + 1).Split(';'))
                        {
                            string[] f = entry.Split(',');
                            if (f.Length != 7) continue;
                            result.Add(new Piece
                            {
                                Name = f[0].Trim(),
                                SheetRect = new RectInt(int.Parse(f[1]), int.Parse(f[2]), int.Parse(f[3]), int.Parse(f[4])),
                                BaseOrigin = new Vector2Int(int.Parse(f[5]), int.Parse(f[6]))
                            });
                        }
                        break;
                    }
                }
                if (type == "IEND") break;
                pos += 12 + length;
            }
            return result;
        }

        // 시트를 배경과 같은 픽셀 크기·필터로 들여오고 조각 정보대로 자른다.
        // 조각의 기준점을 "배경의 기준점이 조각 안에서 놓이는 자리"로 옮겨서, 자식 위치 (0,0)이면 배경과 딱 겹친다
        private static Dictionary<string, Sprite> ImportSheet(string path, List<Piece> pieces, Texture2D baseTexture, Sprite baseSprite)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = baseSprite.pixelsPerUnit;
            importer.filterMode = baseTexture.filterMode;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            // 다시 실행해도 스프라이트 ID가 바뀌지 않게 이름이 같은 조각은 이전 ID를 쓴다
            var oldIds = new Dictionary<string, GUID>();
            foreach (SpriteRect old in provider.GetSpriteRects())
            {
                oldIds[old.name] = old.spriteID;
            }

            Vector2 basePivot = baseSprite.pivot; // 배경 이미지 왼쪽 아래 기준 픽셀
            var rects = new List<SpriteRect>();
            var pairs = new List<SpriteNameFileIdPair>();
            foreach (Piece piece in pieces)
            {
                var size = new Vector2(piece.SheetRect.width, piece.SheetRect.height);
                Vector2 pivot = (basePivot - (Vector2)piece.BaseOrigin) / size;
                var rect = new SpriteRect
                {
                    name = piece.Name,
                    rect = new Rect(piece.SheetRect.x, piece.SheetRect.y, piece.SheetRect.width, piece.SheetRect.height),
                    alignment = SpriteAlignment.Custom,
                    pivot = pivot,
                    spriteID = oldIds.TryGetValue(piece.Name, out GUID id) ? id : GUID.Generate()
                };
                rects.Add(rect);
                pairs.Add(new SpriteNameFileIdPair(rect.name, rect.spriteID));
            }
            provider.SetSpriteRects(rects.ToArray());
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(pairs);
            provider.Apply();
            importer.SaveAndReimport();

            var result = new Dictionary<string, Sprite>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite sprite) result[sprite.name] = sprite;
            }
            return result;
        }

        // 조각 이름으로 반짝임 성격을 고른다. 같은 종류끼리는 박자를 엇갈려 한꺼번에 깜빡이지 않게 한다
        private static void ApplyPreset(SpriteGlowPulse pulse, string pieceName, int index)
        {
            var so = new SerializedObject(pulse);
            float min = 0.3f, max = 1f, period = 4f, holdRatio = 0f;
            if (pieceName.StartsWith("Stars"))
            {
                min = 0f; period = 1.6f + 0.5f * index;
            }
            else if (pieceName.StartsWith("Moon"))
            {
                min = 0.25f; period = 5f;
            }
            so.FindProperty("_minAlpha").floatValue = min;
            so.FindProperty("_maxAlpha").floatValue = max;
            so.FindProperty("_period").floatValue = period;
            so.FindProperty("_phase").floatValue = (index * 0.37f) % 1f;
            so.FindProperty("_holdRatio").floatValue = holdRatio;
            so.FindProperty("_flickerChance").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
