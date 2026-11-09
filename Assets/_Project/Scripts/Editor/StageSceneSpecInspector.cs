using System.IO;
using System.Text.RegularExpressions;
using CoffeeSpec.Core;
using CoffeeSpec.Spec;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// 프로젝트 창에서 StageNScene 파일을 고르면 그 씬이 쓰는 StageSpec 편집 화면을 Inspector에 붙인다.
    /// 씬을 열지 않고도 기획서를 고칠 수 있게 하기 위해서다. 어떤 StageSpec을 쓸지는 씬 안의 SpecUI에서만 바꾼다.
    /// </summary>
    [CustomEditor(typeof(SceneAsset))]
    public class StageSceneSpecInspector : UnityEditor.Editor
    {
        private const string SpecUIPrefabGuid = "9b83e62b1d455be40b064ef1faf51975"; // Prefabs/UI/SpecUI
        private const string SpecFieldName = "_spec";

        // 씬 파일 안의 SpecUI 오버라이드(_spec)나 직접 저장된 _spec 참조에서 에셋 GUID를 뽑는다
        private static readonly Regex OverrideRegex = new Regex(
            @"propertyPath: _spec\s*\r?\n\s*value:[^\n]*\r?\n\s*objectReference: \{fileID: -?\d+, guid: ([0-9a-f]{32})",
            RegexOptions.Compiled);
        private static readonly Regex DirectRegex = new Regex(
            @"\b_spec: \{fileID: -?\d+, guid: ([0-9a-f]{32})", RegexOptions.Compiled);

        private bool _isStage;
        private bool _fromPrefabDefault;
        private StageSpec _spec;
        private UnityEditor.Editor _specEditor;

        private void OnEnable()
        {
            var scene = target as SceneAsset;
            _isStage = scene != null && StageScenes.TryGetStageNumber(scene.name, out _);
            if (_isStage) _spec = ResolveSpec(AssetDatabase.GetAssetPath(scene), out _fromPrefabDefault);
        }

        private void OnDisable()
        {
            if (_specEditor != null) DestroyImmediate(_specEditor);
        }

        public override void OnInspectorGUI()
        {
            if (!_isStage) return;

            // 씬 에셋 Inspector는 기본으로 비활성이라 편집할 수 있게 켠다
            bool wasEnabled = GUI.enabled;
            GUI.enabled = true;

            EditorGUILayout.LabelField("기획서 (StageSpec)", EditorStyles.boldLabel);
            if (_spec == null)
            {
                EditorGUILayout.HelpBox("이 씬의 SpecUI에 연결된 StageSpec이 없어. 씬을 열고 SpecUI의 Spec 칸에 연결해줘.", MessageType.Info);
                GUI.enabled = wasEnabled;
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("연결된 에셋", _spec, typeof(StageSpec), false);
            }
            if (_fromPrefabDefault)
            {
                EditorGUILayout.HelpBox("씬에 따로 연결이 없어서 SpecUI 프리팹 기본값을 쓰고 있어.", MessageType.None);
            }
            EditorGUILayout.Space();

            CreateCachedEditor(_spec, null, ref _specEditor);
            _specEditor.OnInspectorGUI();

            GUI.enabled = wasEnabled;
        }

        private static StageSpec ResolveSpec(string scenePath, out bool fromPrefabDefault)
        {
            fromPrefabDefault = false;

            // 열려 있는 씬은 아직 저장 안 한 변경이 있을 수 있으니 파일보다 실제 오브젝트를 먼저 본다
            var loaded = SceneManager.GetSceneByPath(scenePath);
            if (loaded.isLoaded)
            {
                foreach (var root in loaded.GetRootGameObjects())
                {
                    var found = FindSpecIn(root);
                    if (found != null) return found;
                }
                return null;
            }

            string text = File.ReadAllText(Path.GetFullPath(scenePath));
            var match = OverrideRegex.Match(text);
            if (!match.Success) match = DirectRegex.Match(text);
            if (match.Success)
            {
                var spec = AssetDatabase.LoadAssetAtPath<StageSpec>(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value));
                if (spec != null) return spec;
            }

            // 씬에 오버라이드가 없으면 SpecUI 프리팹에 들어 있는 값이 쓰인다
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(SpecUIPrefabGuid));
            if (prefab == null || !text.Contains(SpecUIPrefabGuid)) return null;
            fromPrefabDefault = true;
            return FindSpecIn(prefab);
        }

        private static StageSpec FindSpecIn(GameObject root)
        {
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var prop = new SerializedObject(mb).FindProperty(SpecFieldName);
                if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference &&
                    prop.objectReferenceValue is StageSpec spec)
                    return spec;
            }
            return null;
        }
    }
}