using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoffeeSpec.Core;
using CoffeeSpec.Spec;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// 프로젝트 창 우클릭 Create > CoffeeSpec > NewScene으로 Stage(N+1)Scene을 만든다.
    /// 기존 스테이지를 복사하지 않고 프리팹 기본값으로 새로 깐다. 다른 스테이지를 고쳐도 새 씬 기본값이 따라 바뀌지 않게 하기 위해서다.
    /// </summary>
    public static class NewStageSceneMenu
    {
        private const string LogTag = "[NewStageScene]";
        private const string ScenesFolder = "Assets/Scenes";
        private const string SpecFolder = "Assets/_Project/StageSpec";
        private const string MapDataFolder = "Assets/_Project/Data/MapData";
        private const string LevelRootName = "Level";
        private const string EventSystemName = "EventSystem";

        // 프리팹을 옮기거나 이름을 바꿔도 찾을 수 있게 경로 대신 GUID로 가리킨다 (Stage 씬들과 같은 순서)
        private const string MainCameraPrefabGuid = "3f24d8baa2788814590cbfbc1bc76047";    // Prefabs/InGameBase/Main Camera
        private const string InGameScriptsPrefabGuid = "194722cfeca1db84787374cfdb0771d5"; // Prefabs/InGameBase/InGameScripts
        private const string SpecUIPrefabGuid = "9b83e62b1d455be40b064ef1faf51975";        // Prefabs/UI/SpecUI

        [MenuItem("Assets/Create/CoffeeSpec/NewScene", false, 100)]
        private static void CreateNextStageScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("NewScene", "플레이 중에는 만들 수 없어. 플레이를 멈추고 다시 눌러줘.", "확인");
                return;
            }

            var cameraPrefab = LoadPrefab(MainCameraPrefabGuid);
            var inGamePrefab = LoadPrefab(InGameScriptsPrefabGuid);
            var specUIPrefab = LoadPrefab(SpecUIPrefabGuid);
            if (cameraPrefab == null || inGamePrefab == null || specUIPrefab == null)
            {
                EditorUtility.DisplayDialog("NewScene", "Main Camera, InGameScripts, SpecUI 프리팹 중 찾지 못한 게 있어. 콘솔을 확인해줘.", "확인");
                return;
            }

            // 우클릭한 폴더와 상관없이 스테이지 씬은 항상 Assets/Scenes에 모은다 (번호 계산과 Level Editor가 이 폴더를 본다)
            int newStage = FindMaxStageNumber() + 1;
            string newName = StageScenes.GetSceneName(newStage);
            string newPath = $"{ScenesFolder}/{newName}.unity";
            if (File.Exists(Path.GetFullPath(newPath)))
            {
                EditorUtility.DisplayDialog("NewScene", $"{newName}이 이미 있어서 만들지 않았어.", "확인");
                return;
            }

            // 새 씬을 열면서 지금 씬을 닫게 되므로 저장할지 먼저 물어본다
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(cameraPrefab, scene);
            // MapLoader와 Level Editor가 이 이름의 루트 밑에서 맵 배치를 찾는다
            new GameObject(LevelRootName);
            PrefabUtility.InstantiatePrefab(inGamePrefab, scene);
            var specUI = (GameObject)PrefabUtility.InstantiatePrefab(specUIPrefab, scene);
            CreateEventSystem();

            StageSpec spec = GetOrCreateSpec(newStage);
            int linked = AssignSpec(specUI, spec);

            if (!EditorSceneManager.SaveScene(scene, newPath))
            {
                Debug.LogError($"{LogTag} {newPath} 저장에 실패했어.");
                return;
            }
            AddToBuildSettings(newPath);
            string mapPath = CreateMapData(newName, newPath);
            Debug.Log($"{LogTag} {newName}을 만들었어. 기획서 {AssetDatabase.GetAssetPath(spec)} 연결 {linked}곳, 맵 데이터 {mapPath}, 빌드 목록에 추가함.");
        }

        [MenuItem("Assets/Create/CoffeeSpec/NewScene", true)]
        private static bool CanCreate() => !EditorApplication.isPlaying;

        private static GameObject LoadPrefab(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) Debug.LogError($"{LogTag} GUID {guid} 프리팹을 찾지 못했어.");
            return prefab;
        }

        // 다른 스테이지와 같은 구성: EventSystem + Input System용 UI 모듈(기본 입력 액션)
        private static void CreateEventSystem()
        {
            var go = new GameObject(EventSystemName, typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        private static int FindMaxStageNumber()
        {
            int max = 0;
            foreach (var file in Directory.GetFiles(Path.GetFullPath(ScenesFolder), "*.unity"))
            {
                if (StageScenes.TryGetStageNumber(Path.GetFileNameWithoutExtension(file), out int n) && n > max) max = n;
            }
            return max;
        }

        // SpecPanel처럼 StageSpec을 받는 _spec 필드에 새 기획서를 꽂는다
        private static int AssignSpec(GameObject root, StageSpec spec)
        {
            int count = 0;
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var field = FindField(mb.GetType(), "_spec");
                if (field == null || !typeof(StageSpec).IsAssignableFrom(field.FieldType)) continue;
                var so = new SerializedObject(mb);
                var prop = so.FindProperty("_spec");
                if (prop == null) continue;
                prop.objectReferenceValue = spec;
                so.ApplyModifiedPropertiesWithoutUndo();
                count++;
            }
            return count;
        }

        private static System.Reflection.FieldInfo FindField(System.Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (f != null) return f;
            }
            return null;
        }

        private static StageSpec GetOrCreateSpec(int stage)
        {
            string path = $"{SpecFolder}/Stage{stage}Spec.asset";
            var existing = AssetDatabase.LoadAssetAtPath<StageSpec>(path);
            if (existing != null) return existing;

            var spec = ScriptableObject.CreateInstance<StageSpec>();
            AssetDatabase.CreateAsset(spec, path);
            // CreateInstance로는 Reset이 불리지 않아서 기본 문장 틀 CSV를 직접 연결한다
            var so = new SerializedObject(spec);
            so.FindProperty("_sentenceCsv").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(StageSpec.DefaultSentenceCsvPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return spec;
        }

        // Level Editor의 MapData와 같은 모양. 필드 이름이 JSON 키라서 바꾸면 Level Editor가 못 읽는다
        [System.Serializable]
        private class EmptyMapData
        {
            public string scene;
            public string scenePath;
            public List<EmptyMapEntry> entries = new List<EmptyMapEntry>();
        }

        [System.Serializable]
        private class EmptyMapEntry
        {
            public string prefabPath;
            public string name;
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale;
        }

        // 배치가 빈 맵 JSON을 만들어 Level Editor 드롭다운에서 바로 고를 수 있게 한다. 이미 있으면 그대로 둔다
        private static string CreateMapData(string sceneName, string scenePath)
        {
            string path = $"{MapDataFolder}/{sceneName}.json";
            if (File.Exists(Path.GetFullPath(path))) return path + " (기존 파일 유지)";

            var data = new EmptyMapData { scene = sceneName, scenePath = scenePath };
            Directory.CreateDirectory(Path.GetFullPath(MapDataFolder));
            File.WriteAllText(Path.GetFullPath(path), JsonUtility.ToJson(data, true), new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
            return path;
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}