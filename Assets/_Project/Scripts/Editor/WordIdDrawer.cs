#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CoffeeSpec.Rules;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// WordId를 WordsCSV의 Id 드롭다운으로 고르게 한다. 문자열을 직접 치다 생기는 오타를 막는다.
    /// </summary>
    [CustomPropertyDrawer(typeof(WordId))]
    public class WordIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Draw(position, property, label, null);
        }

        /// <param name="filter">null이 아니면 이 품사들 중 하나인 단어만 보여준다 (문장 빈칸용)</param>
        public static void Draw(Rect position, SerializedProperty property, GUIContent label, IReadOnlyList<WordType> filter)
        {
            var valueProp = property.FindPropertyRelative("_value");
            EditorGUI.BeginProperty(position, label, property);

            var ids = new List<string>();
            var names = new List<string>();
            foreach (var e in WordTags.All)
            {
                if (filter != null && !filter.Contains(e.Type)) continue;
                ids.Add(e.Id.Value);
                names.Add($"{e.Id.Value} ({e.DisplayName})");
            }

            string current = valueProp.stringValue;
            int index = ids.IndexOf(current);
            // CSV에서 지워진 값도 사라지지 않게 보여줘서 무엇을 고쳐야 하는지 알 수 있게 한다
            if (index < 0)
            {
                ids.Insert(0, current);
                names.Insert(0, string.IsNullOrEmpty(current) ? "(선택 안 함)" : $"{current} (CSV에 없음)");
                index = 0;
            }

            int next = EditorGUI.Popup(position, label.text, index, names.ToArray());
            if (next != index) valueProp.stringValue = ids[next];
            EditorGUI.EndProperty();
        }
    }

    /// <summary>
    /// WordsCSV를 고치면 드롭다운과 문장 검증이 바로 새 목록을 쓰게 한다.
    /// 한셀/엑셀이 CP949(한글)로 저장한 CSV는 UTF-8(BOM)로 자동 변환해서 글자가 깨지지 않게 한다.
    /// </summary>
    public class WordsCsvPostprocessor : AssetPostprocessor
    {
        private const string DataFolder = "Assets/_Project/Data/";

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool reload = false;
            foreach (var path in imported)
            {
                if (!path.StartsWith(DataFolder) || !path.EndsWith(".csv", System.StringComparison.OrdinalIgnoreCase)) continue;

                // 변환했으면 다시 임포트되면서 이 함수가 한 번 더 불린다 (그땐 이미 BOM이 있어서 그냥 지나간다)
                if (ConvertToUtf8Bom(path))
                {
                    AssetDatabase.ImportAsset(path);
                    continue;
                }
                if (path == WordTags.EditorAssetPath) reload = true;
            }
            if (reload) WordTags.Reload();
        }

        /// <returns>파일을 다시 썼으면 true</returns>
        private static bool ConvertToUtf8Bom(string assetPath)
        {
            string full = System.IO.Path.GetFullPath(assetPath);
            byte[] bytes = System.IO.File.ReadAllBytes(full);

            // 이미 UTF-8 BOM이면 그대로 둔다
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return false;

            string text;
            try
            {
                // BOM 없는 UTF-8인지 엄격하게 확인한다
                text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            }
            catch (System.Text.DecoderFallbackException)
            {
                try
                {
                    // UTF-8이 아니면 한셀 기본 인코딩인 CP949로 읽는다
                    text = System.Text.Encoding.GetEncoding(949).GetString(bytes);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[WordsCSV] {assetPath} 인코딩 변환 실패. UTF-8로 직접 저장해줘. ({e.Message})");
                    return false;
                }
            }

            System.IO.File.WriteAllText(full, text, new System.Text.UTF8Encoding(true));
            Debug.Log($"[WordsCSV] {assetPath} 를 UTF-8(BOM)로 변환했어.");
            return true;
        }
    }
}
#endif