using System.Collections.Generic;
using CoffeeSpec.Rules;
using CoffeeSpec.Rules.Entities;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// RuleManager의 변신 프리팹 목록(_transformPrefabs)을 빌드 직전에 Prefabs/Object 폴더 기준으로 채운다.
    /// 에디터에서는 목록이 비어 있으면 RuleManager가 실행 중에 스스로 채우지만, 빌드에서는 그럴 수 없어서
    /// 맵에 없는 모습(예: 3스테이지의 비둘기)으로 변신하는 문장이 빌드에서만 안 먹힌다.
    /// 목록이 이미 같으면 아무것도 저장하지 않는다. 메뉴로 직접 돌릴 수도 있다.
    /// </summary>
    public sealed class TransformPrefabListFiller : IPreprocessBuildWithReport
    {
        private const string ObjectPrefabFolder = "Assets/_Project/Prefabs/Object";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Fill();

        [MenuItem("CoffeeSpec/변신 프리팹 목록 채우기")]
        public static void Fill()
        {
            var prefabs = new List<RuleEntity>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ObjectPrefabFolder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && go.TryGetComponent<RuleEntity>(out var entity)) prefabs.Add(entity);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null) continue;
                bool changed = false;
                foreach (var manager in root.GetComponentsInChildren<RuleManager>(true))
                {
                    var so = new SerializedObject(manager);
                    var list = so.FindProperty("_transformPrefabs");
                    if (list == null || SameList(list, prefabs)) continue;
                    list.arraySize = prefabs.Count;
                    for (int i = 0; i < prefabs.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
                if (!changed) continue;
                PrefabUtility.SavePrefabAsset(root);
                Debug.Log($"[TransformPrefabs] {path}: 변신 프리팹 {prefabs.Count}개로 채움");
            }
        }

        private static bool SameList(SerializedProperty list, List<RuleEntity> prefabs)
        {
            if (list.arraySize != prefabs.Count) return false;
            for (int i = 0; i < prefabs.Count; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue != prefabs[i]) return false;
            return true;
        }
    }
}