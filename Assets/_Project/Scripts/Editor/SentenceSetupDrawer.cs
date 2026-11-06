#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CoffeeSpec.Rules;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// RuleManager 문장 항목을 그린다. Template Id는 CSV Id 드롭다운으로 고르고,
    /// Words 개수는 CSV Pattern 길이로 고정해서 빈칸 수를 잘못 넣는 일을 막는다.
    /// </summary>
    [CustomPropertyDrawer(typeof(RuleManager.SentenceSetup))]
    public class SentenceSetupDrawer : PropertyDrawer
    {
        private const string CsvField = "_sentenceCsv";

        private static float Line => EditorGUIUtility.singleLineHeight;
        private static float Space => EditorGUIUtility.standardVerticalSpacing;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return Line;
            var words = property.FindPropertyRelative("WordIds");
            // 접기 줄 + Template Id + 단어 칸들 + Preview
            return (Line + Space) * (3 + words.arraySize);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var rect = new Rect(position.x, position.y, position.width, Line);
            property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, label, true);
            if (!property.isExpanded) { EditorGUI.EndProperty(); return; }

            EditorGUI.indentLevel++;
            var idProp = property.FindPropertyRelative("TemplateId");
            var words = property.FindPropertyRelative("WordIds");
            var preview = property.FindPropertyRelative("Preview");

            var templates = LoadTemplates(property.serializedObject);
            var ids = new List<string>(templates.Keys);

            // Template Id
            rect.y += Line + Space;
            if (ids.Count > 0)
            {
                int current = ids.IndexOf(idProp.stringValue);
                if (current < 0) { current = 0; idProp.stringValue = ids[0]; }
                int next = EditorGUI.Popup(rect, "Template Id", current, ids.ToArray());
                if (next != current) idProp.stringValue = ids[next];
            }
            else
            {
                EditorGUI.PropertyField(rect, idProp);
            }

            // Words: CSV Pattern 길이로 개수를 맞추고, 칸 이름에 품사를 보여준다
            templates.TryGetValue(idProp.stringValue, out var template);
            if (template != null && words.arraySize != template.Pattern.Count)
                words.arraySize = template.Pattern.Count;

            for (int i = 0; i < words.arraySize; i++)
            {
                rect.y += Line + Space;
                string slot = template != null ? $"{i + 1}. {template.Pattern[i]}" : $"{i + 1}.";
                // 품사 자리에 맞는 단어만 고를 수 있게 걸러서 보여준다
                WordType? filter = template != null ? template.Pattern[i] : null;
                WordIdDrawer.Draw(rect, words.GetArrayElementAtIndex(i), new GUIContent(slot), filter);
            }

            // Preview (읽기 전용)
            rect.y += Line + Space;
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.PropertyField(rect, preview);

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private static Dictionary<string, SentenceTemplate> LoadTemplates(SerializedObject so)
        {
            var csv = so.FindProperty(CsvField)?.objectReferenceValue as TextAsset;
            return csv == null ? new Dictionary<string, SentenceTemplate>() : SentenceTemplate.ParseCsv(csv.text);
        }
    }
}
#endif