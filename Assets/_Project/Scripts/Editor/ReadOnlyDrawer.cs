#if UNITY_EDITOR
using CoffeeSpec.Core;
using UnityEditor;
using UnityEngine;

namespace CoffeeSpec.EditorTools
{
    /// <summary>[ReadOnly] 필드를 회색(수정 불가)으로 그린다.</summary>
    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public class ReadOnlyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }
    }
}
#endif