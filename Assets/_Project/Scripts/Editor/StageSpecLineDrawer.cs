#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CoffeeSpec.Rules;
using CoffeeSpec.Spec;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// StageSpec 줄 하나를 그린다.
    /// 줄 종류(문장/주석/빈 줄) → 문장이면 SentencesCSV Id 드롭다운 → 빈칸마다 처음 단어 드롭다운 → 미리보기.
    /// 처음 단어는 그 빈칸 품사에 맞고 Word Pool에 있는 단어만 보여 주고,
    /// Word Pool 개수만큼 이미 다른 줄에서 쓰고 있으면 회색(선택 불가)으로 보여 준다.
    /// 저장은 기존 글자 형식("NounIsNoun, Person _", "// 주석", "")으로 한다.
    /// </summary>
    [CustomPropertyDrawer(typeof(StageSpecLineAttribute))]
    public class StageSpecLineDrawer : PropertyDrawer
    {
        private const string CsvField = "_sentenceCsv";
        private const string PoolField = "_wordPool";
        private const string LinesField = "_templateIds";
        private const string BlankLabel = "(빈칸)";

        private static readonly string[] KindNames = { "문장", "주석", "빈 줄" };

        private static float Line => EditorGUIUtility.singleLineHeight;
        private static float Space => EditorGUIUtility.standardVerticalSpacing;
        private static float ElementGap => EditorGUIUtility.singleLineHeight;

        // 저장된 글자를 줄 종류, 문장 틀 Id, 처음 단어로 나눈 것
        private struct Parsed
        {
            public StageSpec.LineKind Kind;
            public string Comment;
            public string TemplateId;
            public List<WordId> Preset;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String) return Line;
            var parsed = Parse(property.stringValue);
            int lines = 1; // 줄 종류
            if (parsed.Kind == StageSpec.LineKind.Comment) lines += 1;
            else if (parsed.Kind == StageSpec.LineKind.Sentence)
            {
                var templates = LoadTemplates(property.serializedObject);
                templates.TryGetValue(parsed.TemplateId ?? "", out var template);
                lines += 2 + (template != null ? template.Pattern.Count : 0); // 문장 틀 + 빈칸들 + 미리보기
            }
            // 요소끼리 붙어 보이지 않게 아래에 한 줄을 비워 둔다
            return (Line + Space) * lines + ElementGap;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            var parsed = Parse(property.stringValue);
            var rect = new Rect(position.x, position.y, position.width, Line);

            // 줄 종류
            int kind = (int)parsed.Kind;
            int nextKind = EditorGUI.Popup(rect, label.text, kind, KindNames);
            if (nextKind != kind)
            {
                parsed.Kind = (StageSpec.LineKind)nextKind;
                if (parsed.Kind == StageSpec.LineKind.Comment) parsed.Comment = parsed.Comment ?? "";
                // 빈 Id는 빈 줄로 저장되므로 문장으로 바꿀 때는 CSV 첫 문장 틀을 넣어 둔다
                if (parsed.Kind == StageSpec.LineKind.Sentence && string.IsNullOrEmpty(parsed.TemplateId))
                {
                    foreach (var id in LoadTemplates(property.serializedObject).Keys) { parsed.TemplateId = id; break; }
                }
                property.stringValue = Serialize(parsed);
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            if (parsed.Kind == StageSpec.LineKind.Comment)
            {
                rect.y += Line + Space;
                string text = EditorGUI.TextField(rect, "주석", parsed.Comment ?? "");
                if (text != parsed.Comment)
                {
                    parsed.Comment = text;
                    property.stringValue = Serialize(parsed);
                }
            }
            else if (parsed.Kind == StageSpec.LineKind.Sentence)
            {
                DrawSentence(ref rect, property, parsed);
            }
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private void DrawSentence(ref Rect rect, SerializedProperty property, Parsed parsed)
        {
            var so = property.serializedObject;
            var templates = LoadTemplates(so);
            var ids = new List<string>(templates.Keys);

            // 문장 틀 Id
            rect.y += Line + Space;
            if (ids.Count == 0)
            {
                EditorGUI.HelpBox(rect, "문장 틀 CSV를 먼저 넣어줘", MessageType.Warning);
                return;
            }
            int current = ids.IndexOf(parsed.TemplateId ?? "");
            var names = new List<string>(ids);
            if (current < 0)
            {
                names.Insert(0, string.IsNullOrEmpty(parsed.TemplateId) ? "(선택 안 함)" : $"{parsed.TemplateId} (CSV에 없음)");
                ids.Insert(0, parsed.TemplateId ?? "");
                current = 0;
            }
            int next = EditorGUI.Popup(rect, "문장 틀", current, names.ToArray());
            if (next != current)
            {
                parsed.TemplateId = ids[next];
                parsed.Preset.Clear(); // 빈칸 구성이 달라지므로 처음 단어는 비운다
                property.stringValue = Serialize(parsed);
                return;
            }

            if (!templates.TryGetValue(parsed.TemplateId ?? "", out var template)) return;

            // 처음 단어 개수를 빈칸 수에 맞춘다
            while (parsed.Preset.Count < template.Pattern.Count) parsed.Preset.Add(default);

            var pool = ReadPool(so);
            var used = CountUsedInOtherLines(so, property.propertyPath, templates);

            for (int i = 0; i < template.Pattern.Count; i++)
            {
                rect.y += Line + Space;
                DrawSlot(rect, property, parsed, template, i, pool, used);
            }

            // 미리보기
            rect.y += Line + Space;
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.TextField(rect, "미리보기", Preview(template, parsed.Preset));
        }

        private void DrawSlot(Rect rect, SerializedProperty property, Parsed parsed, SentenceTemplate template, int slot,
                              Dictionary<WordId, int> pool, Dictionary<WordId, int> used)
        {
            WordId current = parsed.Preset[slot];
            var labelRect = EditorGUI.PrefixLabel(rect, new GUIContent($"{slot + 1}. {template.SlotLabel(slot)}"));

            string buttonText;
            if (current.IsEmpty) buttonText = BlankLabel;
            else if (!pool.ContainsKey(current)) buttonText = $"{current.Value} (Word Pool에 없음)";
            else if (!template.Accepts(slot, current)) buttonText = $"{current.Value} (품사가 안 맞음)";
            else buttonText = $"{current.Value} ({WordTags.DisplayName(current)})";

            // 들여쓰기가 버튼 안쪽까지 밀리지 않게 잠깐 끈다
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            if (!EditorGUI.DropdownButton(labelRect, new GUIContent(buttonText), FocusType.Keyboard))
            {
                EditorGUI.indentLevel = indent;
                return;
            }
            EditorGUI.indentLevel = indent;

            var so = property.serializedObject;
            string path = property.propertyPath;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(BlankLabel), current.IsEmpty, () => SetSlot(so, path, slot, default));

            // Word Pool 순서대로, 같은 단어는 한 번만 보여 준다
            foreach (var pair in pool)
            {
                WordId word = pair.Key;
                if (!template.Accepts(slot, word)) continue;
                used.TryGetValue(word, out int usedCount);
                bool isCurrent = word == current;
                bool full = !isCurrent && usedCount >= pair.Value;
                var content = new GUIContent($"{word.Value} ({WordTags.DisplayName(word)})  {usedCount}/{pair.Value}");
                if (full) menu.AddDisabledItem(content);
                else menu.AddItem(content, isCurrent, () => SetSlot(so, path, slot, word));
            }
            menu.DropDown(labelRect);
        }

        // 메뉴는 나중에 불리므로 경로로 다시 찾아서 저장한다
        private static void SetSlot(SerializedObject so, string path, int slot, WordId word)
        {
            so.Update();
            var prop = so.FindProperty(path);
            if (prop == null) return;
            var parsed = Parse(prop.stringValue);
            while (parsed.Preset.Count <= slot) parsed.Preset.Add(default);
            parsed.Preset[slot] = word;
            prop.stringValue = Serialize(parsed);
            so.ApplyModifiedProperties();
        }

        // ---- 읽기/쓰기 ----

        private static Parsed Parse(string raw)
        {
            var p = new Parsed { Preset = new List<WordId>() };
            string entry = raw == null ? "" : raw.Trim();
            if (entry.Length == 0)
            {
                p.Kind = StageSpec.LineKind.Empty;
                return p;
            }
            if (entry.StartsWith(StageSpec.CommentPrefix, System.StringComparison.Ordinal))
            {
                p.Kind = StageSpec.LineKind.Comment;
                p.Comment = entry.Substring(StageSpec.CommentPrefix.Length).Trim();
                return p;
            }

            p.Kind = StageSpec.LineKind.Sentence;
            int comma = entry.IndexOf(StageSpec.PresetSeparator);
            if (comma < 0)
            {
                p.TemplateId = entry;
                return p;
            }
            p.TemplateId = entry.Substring(0, comma).Trim();
            foreach (string token in entry.Substring(comma + 1).Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries))
                p.Preset.Add(token == StageSpec.EmptyPresetToken ? default : new WordId(token));
            return p;
        }

        private static string Serialize(Parsed p)
        {
            switch (p.Kind)
            {
                case StageSpec.LineKind.Empty:
                    return "";
                case StageSpec.LineKind.Comment:
                    return string.IsNullOrEmpty(p.Comment) ? StageSpec.CommentPrefix : $"{StageSpec.CommentPrefix} {p.Comment}";
            }

            string id = p.TemplateId ?? "";
            // 처음 단어가 하나도 없으면 Id만 저장한다 (모두 빈칸)
            bool any = false;
            foreach (var w in p.Preset) if (!w.IsEmpty) { any = true; break; }
            if (!any) return id;

            var tokens = new List<string>();
            foreach (var w in p.Preset) tokens.Add(w.IsEmpty ? StageSpec.EmptyPresetToken : w.Value);
            return $"{id}{StageSpec.PresetSeparator} {string.Join(" ", tokens)}";
        }

        private static Dictionary<string, SentenceTemplate> LoadTemplates(SerializedObject so)
        {
            var csv = so.FindProperty(CsvField)?.objectReferenceValue as TextAsset;
            return csv == null ? new Dictionary<string, SentenceTemplate>() : SentenceTemplate.ParseCsv(csv.text);
        }

        // Word Pool의 단어별 개수 (넣은 순서 유지)
        private static Dictionary<WordId, int> ReadPool(SerializedObject so)
        {
            var result = new Dictionary<WordId, int>();
            var pool = so.FindProperty(PoolField);
            if (pool == null) return result;
            for (int i = 0; i < pool.arraySize; i++)
            {
                var word = new WordId(pool.GetArrayElementAtIndex(i).FindPropertyRelative("_value").stringValue);
                if (word.IsEmpty) continue;
                result.TryGetValue(word, out int n);
                result[word] = n + 1;
            }
            return result;
        }

        // 지금 그리는 줄을 뺀 나머지 줄에서 처음 단어로 쓰인 개수
        private static Dictionary<WordId, int> CountUsedInOtherLines(SerializedObject so, string selfPath,
                                                                     Dictionary<string, SentenceTemplate> templates)
        {
            var result = new Dictionary<WordId, int>();
            var lines = so.FindProperty(LinesField);
            if (lines == null) return result;
            for (int i = 0; i < lines.arraySize; i++)
            {
                var element = lines.GetArrayElementAtIndex(i);
                if (element.propertyPath == selfPath) continue;
                var parsed = Parse(element.stringValue);
                if (parsed.Kind != StageSpec.LineKind.Sentence) continue;
                // 빈칸보다 많이 적힌 단어는 게임에서도 무시되므로 세지 않는다
                int count = templates.TryGetValue(parsed.TemplateId ?? "", out var t) ? t.Pattern.Count : parsed.Preset.Count;
                for (int s = 0; s < parsed.Preset.Count && s < count; s++)
                {
                    var w = parsed.Preset[s];
                    if (w.IsEmpty) continue;
                    result.TryGetValue(w, out int n);
                    result[w] = n + 1;
                }
            }
            return result;
        }

        // 빈칸은 "_", 채운 칸은 활용형과 어미까지 반영해서 보여 준다
        private static string Preview(SentenceTemplate template, List<WordId> preset)
        {
            return SentenceTemplate.TokenPattern.Replace(template.Text, m =>
            {
                int slot = int.Parse(m.Groups[1].Value);
                WordId word = slot < preset.Count ? preset[slot] : default;
                if (m.Groups[3].Success) return word.IsEmpty ? "" : SentenceTemplate.ResolveEnding(m.Groups[3].Value, word);
                if (word.IsEmpty) return "_";
                return WordTags.Form(word, m.Groups[2].Success ? m.Groups[2].Value : null);
            });
        }
    }
}
#endif