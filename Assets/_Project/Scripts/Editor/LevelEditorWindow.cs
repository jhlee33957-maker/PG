#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// EditorScene에서 맵을 배치하고 MapData의 JSON과 대상 씬에 저장하는 레벨 에디터 창.
    /// 배치된 프리팹은 "Level" 루트 밑에만 두어서 카메라, RuleManager 같은 씬 고유 오브젝트는 건드리지 않는다.
    /// </summary>
    public class LevelEditorWindow : EditorWindow
    {
        public const string EditorScenePath = "Assets/Scenes/EditorScene.unity";
        private const string LevelRootName = "Level";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Object"; // 배치용 프리팹만 목록에 띄운다
        private const string MapDataFolder = "Assets/_Project/Data/MapData";

        [SerializeField] private bool _useSnap = true;
        [SerializeField] private float _gridSize = 1f;
        [SerializeField] private float _thumbSize = 72f;
        [SerializeField] private string _currentJson;

        private readonly List<GameObject> _prefabs = new();
        private readonly List<string> _jsonPaths = new();
        private GameObject _selected;
        private Vector2 _scroll;
        private SceneAsset _newMapScene;

        // 한 번의 드래그 동안 이미 놓은 칸. 같은 칸에 여러 개 생기지 않게 한다
        private readonly HashSet<Vector2Int> _strokeCells = new();
        private bool _dragging;
        private Vector3 _lastPlaced;
        // Scene 뷰 키보드 이동 (WASD, Shift로 두 배)
        private const float KeyboardPanSpeed = 1.5f;
        private readonly HashSet<KeyCode> _panKeys = new();
        private bool _panShiftHeld;
        private double _lastPanTime;

        [Serializable]
        private class MapData
        {
            public string scene;      // 대상 씬 이름 (예: TestScene)
            public string scenePath;  // 대상 씬 경로
            public List<MapEntry> entries = new();
        }

        [Serializable]
        private class MapEntry
        {
            public string prefabPath;
            public string name;
            public Vector3 position;
            public Vector3 rotation; // 오일러 각
            public Vector3 scale;
        }

        /// <summary>EditorScene의 Level을 비운 뒤 저장할 때는 true로 둬서 맵 데이터가 빈 배치로 덮이지 않게 한다.</summary>
        public static bool SuppressAutoSave;

        /// <summary>EditorScene을 비웠을 때 드롭다운도 "맵 선택"으로 되돌린다.</summary>
        public static void ResetCurrentMap()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<LevelEditorWindow>())
            {
                w._currentJson = null;
                w.Repaint();
            }
        }

        [MenuItem("CoffeeSpec/Level Editor")]
        public static void Open() => GetWindow<LevelEditorWindow>("Level Editor");

        private void OnEnable()
        {
            RefreshPrefabs();
            RefreshJsonList();
            SceneView.duringSceneGui += OnSceneGUI;
            EditorSceneManager.sceneSaving += OnEditorSceneSaving;
            EditorApplication.update += UpdateKeyboardPan;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorSceneManager.sceneSaving -= OnEditorSceneSaving;
            EditorApplication.update -= UpdateKeyboardPan;
            _panKeys.Clear();
            _selected = null;
        }

        private void OnProjectChange() { RefreshPrefabs(); RefreshJsonList(); Repaint(); }

        private void RefreshPrefabs()
        {
            _prefabs.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null) _prefabs.Add(go);
            }
            _prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        private void RefreshJsonList()
        {
            _jsonPaths.Clear();
            string full = Path.GetFullPath(MapDataFolder);
            if (!Directory.Exists(full)) return;
            foreach (var file in Directory.GetFiles(full, "*.json"))
                _jsonPaths.Add($"{MapDataFolder}/{Path.GetFileName(file)}");
            _jsonPaths.Sort(StringComparer.Ordinal);
        }

        // ───────────── 창 UI ─────────────

        private void OnGUI()
        {
            DrawMapList();
            EditorGUILayout.Space();

            _useSnap = EditorGUILayout.Toggle("그리드 스냅", _useSnap);
            using (new EditorGUI.DisabledScope(!_useSnap))
                _gridSize = Mathf.Max(0.01f, EditorGUILayout.FloatField("그리드 크기", _gridSize));
            _thumbSize = EditorGUILayout.Slider("썸네일 크기", _thumbSize, 40f, 128f);

            EditorGUILayout.HelpBox(_selected == null
                ? "프리팹을 고른 뒤 Scene 뷰를 클릭하거나 드래그하면 배치돼. 우클릭하면 삭제돼."
                : $"선택: {_selected.name}  (클릭/드래그로 배치, 우클릭으로 삭제, Esc로 해제)", MessageType.None);

            DrawPrefabGrid();
        }

        private void DrawMapList()
        {
            EditorGUILayout.LabelField("맵 (MapData)", EditorStyles.boldLabel);
            if (_jsonPaths.Count == 0) EditorGUILayout.HelpBox("MapData 폴더에 JSON이 없어. 아래에서 씬을 골라 새 맵을 만들어.", MessageType.Info);

            // 맵 드롭다운: 고르는 순간 EditorScene에 불러온다
            var names = new List<string> { "(맵 선택)" };
            foreach (var path in _jsonPaths) names.Add(Path.GetFileNameWithoutExtension(path));
            int current = _jsonPaths.IndexOf(_currentJson) + 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                int next = EditorGUILayout.Popup(current, names.ToArray(), GUILayout.ExpandWidth(true));
                if (next != current && next > 0) LoadMap(_jsonPaths[next - 1]);

                // 지금 불러온 맵의 JSON과 씬에 저장한다
                using (new EditorGUI.DisabledScope(current == 0))
                    if (GUILayout.Button("Save", GUILayout.Width(60))) SaveMap(_currentJson);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _newMapScene = (SceneAsset)EditorGUILayout.ObjectField(_newMapScene, typeof(SceneAsset), false, GUILayout.MinWidth(80));
                using (new EditorGUI.DisabledScope(_newMapScene == null))
                    if (GUILayout.Button("맵 데이터 생성", GUILayout.Width(110))) CreateMapFromScene(_newMapScene);
            }
        }

        private void DrawPrefabGrid()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            int columns = Mathf.Max(1, (int)((position.width - 20f) / (_thumbSize + 6f)));
            for (int i = 0; i < _prefabs.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns && i + c < _prefabs.Count; c++)
                    {
                        var prefab = _prefabs[i + c];
                        // 썸네일은 비동기로 만들어지므로 아직 없으면 미니 아이콘으로 대신한다
                        Texture tex = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
                        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();

                        var old = GUI.backgroundColor;
                        if (prefab == _selected) GUI.backgroundColor = Color.cyan;
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(_thumbSize)))
                        {
                            if (GUILayout.Button(tex, GUILayout.Width(_thumbSize), GUILayout.Height(_thumbSize)))
                            {
                                _selected = prefab == _selected ? null : prefab;
                                SceneView.RepaintAll();
                            }
                            GUILayout.Label(prefab.name, EditorStyles.miniLabel, GUILayout.Width(_thumbSize));
                        }
                        GUI.backgroundColor = old;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // ───────────── Scene 뷰 배치 ─────────────

        private void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            HandleKeyboardPan(e);

            // EditorScene에서는 우클릭(누른 채 드래그 포함)으로 Level 밑의 프리팹을 지운다
            if (SceneManager.GetActiveScene().path == EditorScenePath && e.button == 1 && !e.alt &&
                (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                if (e.type == EventType.MouseDown)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName("Erase Prefab");
                }
                TryErase(e.mousePosition);
                e.Use();
                return;
            }
            if (e.type == EventType.MouseUp && e.button == 1)
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup()); // 한 번 드래그로 지운 건 Ctrl+Z 한 번으로 되살린다

            if (_selected == null) return;

            // 클릭이 오브젝트 선택으로 넘어가지 않게 마우스 입력을 이 창이 가져간다
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            Vector3 pos = MouseWorldPosition(e.mousePosition);
            DrawPreview(pos);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                _dragging = true;
                _strokeCells.Clear();
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName($"Paint {_selected.name}");
                TryPlace(pos);
                _lastPlaced = pos;
                GUIUtility.hotControl = id;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _dragging)
            {
                // 마우스를 빠르게 움직여도 칸이 비지 않게 지난 위치부터 지금까지를 칸 단위로 채운다
                PlaceAlongLine(_lastPlaced, pos);
                _lastPlaced = pos;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _dragging)
            {
                _dragging = false;
                _strokeCells.Clear();
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup()); // 한 번 드래그는 Ctrl+Z 한 번으로 되돌린다
                GUIUtility.hotControl = 0;
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _selected = null;
                _dragging = false;
                Repaint();
                e.Use();
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();
        }

        // 마우스 아래의 오브젝트에서 Level 바로 밑의 프리팹 인스턴스를 찾는다. Level 밖(카메라, UI 등)이면 null
        private static Transform FindErasable(Vector2 mouse)
        {
            var picked = HandleUtility.PickGameObject(mouse, false);
            if (picked == null) return null;
            var root = FindLevelRoot(picked.scene);
            if (root == null) return null;

            Transform t = picked.transform;
            while (t != null && t.parent != root.transform) t = t.parent;
            return t;
        }

        private static void TryErase(Vector2 mouse)
        {
            var t = FindErasable(mouse);
            if (t == null) return;
            var scene = t.gameObject.scene;
            Undo.DestroyObjectImmediate(t.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private void PlaceAlongLine(Vector3 from, Vector3 to)
        {
            float step = _useSnap ? _gridSize : 0.25f;
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / step));
            for (int i = 1; i <= count; i++)
                TryPlace(Snap(Vector3.Lerp(from, to, (float)i / count)));
        }

        private void TryPlace(Vector3 pos)
        {
            var cell = new Vector2Int(Mathf.RoundToInt(pos.x / CellSize), Mathf.RoundToInt(pos.y / CellSize));
            if (!_strokeCells.Add(cell)) return;
            if (IsOccupied(pos)) return; // 이미 같은 프리팹이 그 칸에 있으면 겹쳐 놓지 않는다
            Place(_selected, pos);
        }

        private float CellSize => _useSnap ? _gridSize : 0.25f;

        private bool IsOccupied(Vector3 pos)
        {
            var root = FindLevelRoot(SceneManager.GetActiveScene());
            if (root == null) return false;
            foreach (Transform child in root.transform)
            {
                if ((child.position - pos).sqrMagnitude > CellSize * CellSize * 0.01f) continue;
                if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == _selected) return true;
            }
            return false;
        }

        private Vector3 MouseWorldPosition(Vector2 mouse)
        {
            Vector3 p = HandleUtility.GUIPointToWorldRay(mouse).origin;
            p.z = 0f; // 2D 게임이라 z는 0에 둔다
            return Snap(p);
        }

        private Vector3 Snap(Vector3 p)
        {
            if (!_useSnap) return p;
            p.x = Mathf.Round(p.x / _gridSize) * _gridSize;
            p.y = Mathf.Round(p.y / _gridSize) * _gridSize;
            return p;
        }

        private void DrawPreview(Vector3 pos)
        {
            Vector3 size = Vector3.one;
            var r = _selected.GetComponentInChildren<SpriteRenderer>();
            if (r != null && r.sprite != null)
            {
                Vector2 s = r.drawMode == SpriteDrawMode.Simple ? (Vector2)r.sprite.bounds.size : r.size;
                Vector3 scale = r.transform.lossyScale;
                size = new Vector3(s.x * Mathf.Abs(scale.x), s.y * Mathf.Abs(scale.y), 0f);
            }
            Handles.color = Color.cyan;
            Handles.DrawWireCube(pos, size);
            Handles.Label(pos + Vector3.up * (size.y * 0.5f + 0.2f), _selected.name);
        }

        private void Place(GameObject prefab, Vector3 pos)
        {
            var root = GetOrCreateLevelRoot(SceneManager.GetActiveScene());
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
            go.transform.SetParent(root.transform, false);
            go.transform.position = pos;
            Undo.RegisterCreatedObjectUndo(go, $"Place {prefab.name}");
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        // ───────────── Scene 뷰 키보드 이동 ─────────────

        // EditorScene에서 WASD를 누르고 있는 동안 화면을 옮긴다. 키 입력만 여기서 받고 실제 이동은 UpdateKeyboardPan에서 한다
        private void HandleKeyboardPan(Event e)
        {
            if (e.type != EventType.KeyDown && e.type != EventType.KeyUp) return;
            if (SceneManager.GetActiveScene().path != EditorScenePath) return;
            _panShiftHeld = e.shift;

            KeyCode k = e.keyCode;
            if (k != KeyCode.W && k != KeyCode.A && k != KeyCode.S && k != KeyCode.D) return;

            if (e.type == EventType.KeyDown)
            {
                // Ctrl+S 같은 단축키와 우클릭 비행 모드(Unity 기본 WASD)는 그대로 둔다
                if (e.control || e.command || e.alt || Tools.viewTool == ViewTool.FPS) return;
                _panKeys.Add(k);
                e.Use(); // W가 이동 툴로 바뀌는 것 같은 기본 단축키를 막는다
            }
            else if (_panKeys.Remove(k))
            {
                e.Use();
            }
        }

        private void UpdateKeyboardPan()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min((float)(now - _lastPanTime), 0.1f);
            _lastPanTime = now;
            if (_panKeys.Count == 0) return;

            var view = SceneView.lastActiveSceneView;
            if (view == null || EditorWindow.focusedWindow != view)
            {
                _panKeys.Clear(); // 다른 창으로 포커스가 넘어가면 키를 뗀 것으로 본다
                return;
            }

            Vector3 dir = Vector3.zero;
            if (_panKeys.Contains(KeyCode.W)) dir.y += 1f;
            if (_panKeys.Contains(KeyCode.S)) dir.y -= 1f;
            if (_panKeys.Contains(KeyCode.D)) dir.x += 1f;
            if (_panKeys.Contains(KeyCode.A)) dir.x -= 1f;
            if (dir == Vector3.zero) return;

            // 줌 정도(view.size)에 비례해서 움직여야 확대/축소와 상관없이 화면 기준 속도가 같다
            float speed = view.size * KeyboardPanSpeed * (_panShiftHeld ? 2f : 1f);
            view.pivot += view.rotation * dir.normalized * (speed * dt);
            view.Repaint();
        }

        // ───────────── 맵 불러오기 / 저장 ─────────────

        private bool CheckInEditorScene()
        {
            if (SceneManager.GetActiveScene().path == EditorScenePath) return true;
            EditorUtility.DisplayDialog("Level Editor", "EditorScene을 열어둔 상태에서 써줘.", "확인");
            return false;
        }

        private void LoadMap(string jsonPath)
        {
            if (!CheckInEditorScene()) return;
            var data = ReadJson(jsonPath);
            if (data == null) return;
            var root = FindLevelRoot(SceneManager.GetActiveScene());
            if (root != null && root.transform.childCount > 0 && jsonPath != _currentJson &&
                !EditorUtility.DisplayDialog("Level Editor", $"지금 배치를 지우고 {data.scene} 맵을 불러올까?", "불러오기", "취소"))
                return;

            Apply(SceneManager.GetActiveScene(), data);
            _currentJson = jsonPath;
            Debug.Log($"[LevelEditor] {jsonPath}에서 {data.entries.Count}개를 불러왔어.");
        }

        private void SaveMap(string jsonPath)
        {
            if (!CheckInEditorScene()) return;
            var old = ReadJson(jsonPath);
            if (old == null) return;
            var data = Capture(SceneManager.GetActiveScene(), allowRootFallback: false);
            data.scene = old.scene;
            data.scenePath = ResolveScenePath(old);
            if (string.IsNullOrEmpty(data.scenePath))
            {
                EditorUtility.DisplayDialog("Level Editor", $"{old.scene} 씬을 찾을 수 없어.", "확인");
                return;
            }
            if (!EditorUtility.DisplayDialog("Level Editor",
                    $"{data.scene}의 Level 배치를 지금 배치({data.entries.Count}개)로 덮어쓸까?", "저장", "취소"))
                return;

            WriteJson(jsonPath, data);
            ApplyToScene(data);
        }

        // 대상 씬의 Level을 데이터대로 바꾸고 저장한다
        private static void ApplyToScene(MapData data)
        {
            var target = OpenScene(data.scenePath, out bool openedHere);
            if (!target.IsValid()) return;
            Apply(target, data);
            EditorSceneManager.SaveScene(target);
            if (openedHere) EditorSceneManager.CloseScene(target, true);
            Debug.Log($"[LevelEditor] {data.scene}에 {data.entries.Count}개를 저장했어.");
        }

        /// <summary>
        /// EditorScene을 저장할 때(Ctrl+S, 씬을 바꿀 때 나오는 창의 Save 포함) 불러온 맵의 JSON과 대상 씬도 같이 저장한다.
        /// 지금 배치는 바로 읽어 두고, 실제 쓰기는 씬 저장이 끝난 뒤에 한다 (저장 도중에 다른 씬을 열고 저장하지 않기 위해).
        /// </summary>
        private void OnEditorSceneSaving(Scene scene, string path)
        {
            if (SuppressAutoSave || path != EditorScenePath || string.IsNullOrEmpty(_currentJson)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            string jsonPath = _currentJson;
            var old = ReadJson(jsonPath);
            if (old == null) return;
            var data = Capture(scene, allowRootFallback: false);
            data.scene = old.scene;
            data.scenePath = ResolveScenePath(old);
            if (string.IsNullOrEmpty(data.scenePath))
            {
                Debug.LogWarning($"[LevelEditor] {old.scene} 씬을 찾을 수 없어서 맵 데이터는 저장하지 않았어.");
                return;
            }

            EditorApplication.delayCall += () =>
            {
                WriteJson(jsonPath, data);
                ApplyToScene(data);
            };
        }

        // 아직 JSON이 없는 씬의 배치를 읽어 새 맵 JSON을 만든다
        private void CreateMapFromScene(SceneAsset sceneAsset)
        {
            string scenePath = AssetDatabase.GetAssetPath(sceneAsset);
            if (scenePath == EditorScenePath)
            {
                EditorUtility.DisplayDialog("Level Editor", "EditorScene 말고 실제 맵 씬을 골라줘.", "확인");
                return;
            }
            string jsonPath = $"{MapDataFolder}/{sceneAsset.name}.json";
            if (File.Exists(Path.GetFullPath(jsonPath)) &&
                !EditorUtility.DisplayDialog("Level Editor", $"{sceneAsset.name}.json이 이미 있어. 씬 배치로 다시 만들까?", "덮어쓰기", "취소"))
                return;

            var scene = OpenScene(scenePath, out bool openedHere);
            if (!scene.IsValid()) return;
            var data = Capture(scene, allowRootFallback: true);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            data.scene = sceneAsset.name;
            data.scenePath = scenePath;

            WriteJson(jsonPath, data);
            RefreshJsonList();
            _newMapScene = null;
            _currentJson = null;
            LoadMap(jsonPath);
        }

        private static string ResolveScenePath(MapData data)
        {
            if (!string.IsNullOrEmpty(data.scenePath) && File.Exists(Path.GetFullPath(data.scenePath))) return data.scenePath;
            foreach (var guid in AssetDatabase.FindAssets($"t:Scene {data.scene}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == data.scene) return path;
            }
            return null;
        }

        private static Scene OpenScene(string path, out bool openedHere)
        {
            var scene = SceneManager.GetSceneByPath(path);
            openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (!scene.IsValid()) Debug.LogError($"[LevelEditor] {path}를 열 수 없어.");
            return scene;
        }

        // 씬의 배치를 데이터로 뽑는다. 아직 Level 루트가 없으면 루트에 있는 프리팹 인스턴스를 대신 읽는다
        private static MapData Capture(Scene scene, bool allowRootFallback)
        {
            var data = new MapData();
            var level = FindLevelRoot(scene);
            var sources = new List<Transform>();
            if (level != null)
                foreach (Transform child in level.transform) sources.Add(child);
            else if (allowRootFallback)
                foreach (var go in scene.GetRootGameObjects()) sources.Add(go.transform);

            foreach (var t in sources)
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                if (string.IsNullOrEmpty(path)) continue;
                data.entries.Add(new MapEntry
                {
                    prefabPath = path,
                    name = t.name,
                    position = t.position,
                    rotation = t.eulerAngles,
                    scale = t.localScale
                });
            }
            return data;
        }

        // 씬의 Level 밑을 비우고 데이터대로 다시 깐다
        private static void Apply(Scene scene, MapData data)
        {
            var root = GetOrCreateLevelRoot(scene);
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);

            foreach (var entry in data.entries)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[LevelEditor] 프리팹을 찾을 수 없어: {entry.prefabPath}");
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.SetParent(root.transform, false);
                go.name = entry.name;
                go.transform.SetPositionAndRotation(entry.position, Quaternion.Euler(entry.rotation));
                go.transform.localScale = entry.scale;
                Undo.RegisterCreatedObjectUndo(go, "Apply Level");
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static GameObject FindLevelRoot(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == LevelRootName) return go;
            return null;
        }

        private static GameObject GetOrCreateLevelRoot(Scene scene)
        {
            var root = FindLevelRoot(scene);
            if (root != null) return root;
            root = new GameObject(LevelRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Create Level Root");
            return root;
        }

        private static MapData ReadJson(string assetPath)
        {
            try
            {
                var data = JsonUtility.FromJson<MapData>(File.ReadAllText(Path.GetFullPath(assetPath)));
                if (data == null) throw new Exception("빈 파일");
                if (string.IsNullOrEmpty(data.scene)) data.scene = Path.GetFileNameWithoutExtension(assetPath);
                data.entries ??= new List<MapEntry>();
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LevelEditor] {assetPath} 읽기 실패: {e.Message}");
                return null;
            }
        }

        private static void WriteJson(string assetPath, MapData data)
        {
            Directory.CreateDirectory(Path.GetFullPath(MapDataFolder));
            File.WriteAllText(Path.GetFullPath(assetPath), JsonUtility.ToJson(data, true), new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath);
            Debug.Log($"[LevelEditor] {assetPath} 저장");
        }
    }

    /// <summary>EditorScene을 열면 레벨 에디터 창을 자동으로 띄운다.</summary>
    [InitializeOnLoad]
    internal static class LevelEditorAutoOpen
    {
        static LevelEditorAutoOpen()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => { ClearLevel(scene); TryOpen(scene); TryClose(scene, mode); };
            EditorSceneManager.sceneClosing += (scene, _) => ClearLevel(scene);
            // 유니티를 끌 때는 sceneClosing이 오지 않으므로 종료 직전에 따로 비운다
            EditorApplication.wantsToQuit += OnWantsToQuit;
            // 에디터를 켰을 때 이미 EditorScene이 열려 있는 경우
            EditorApplication.delayCall += () => TryOpen(SceneManager.GetActiveScene());
        }

        /// <summary>
        /// EditorScene은 작업대라서 열 때와 닫을 때 Level 밑을 비우고 저장해 둔다.
        /// 그래야 이전 작업이 씬 파일에 남아 다음 맵과 섞이지 않는다.
        /// </summary>
        private static void ClearLevel(Scene scene)
        {
            if (scene.path != LevelEditorWindow.EditorScenePath || !scene.isLoaded) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return; // 플레이 모드 전환 중에는 건드리지 않는다

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != "Level") continue;
                if (root.transform.childCount == 0) return;
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                LevelEditorWindow.SuppressAutoSave = true; // 비운 배치가 맵 데이터로 저장되지 않게 한다
                try { EditorSceneManager.SaveScene(scene); }
                finally { LevelEditorWindow.SuppressAutoSave = false; }
                LevelEditorWindow.ResetCurrentMap();
                return;
            }
        }

        /// <summary>
        /// 유니티 종료 직전. EditorScene이 열려 있으면 저장 안 한 배치를 먼저 물어본 뒤(저장하면 맵 데이터에 반영됨) Level 밑을 비우고 저장한다.
        /// 저장 창에서 취소하면 종료도 취소한다.
        /// </summary>
        private static bool OnWantsToQuit()
        {
            var scene = SceneManager.GetSceneByPath(LevelEditorWindow.EditorScenePath);
            if (!scene.IsValid() || !scene.isLoaded) return true;
            if (scene.isDirty && !EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] { scene })) return false;
            ClearLevel(scene);
            return true;
        }

        // EditorScene이 아닌 씬으로 바꿔 열면 레벨 에디터 창을 닫는다 (추가로 여는 씬은 무시)
        private static void TryClose(Scene scene, OpenSceneMode mode)
        {
            if (mode != OpenSceneMode.Single || scene.path == LevelEditorWindow.EditorScenePath) return;
            foreach (var w in Resources.FindObjectsOfTypeAll<LevelEditorWindow>()) w.Close();
        }

        private static void TryOpen(Scene scene)
        {
            if (scene.path != LevelEditorWindow.EditorScenePath) return;
            if (EditorWindow.HasOpenInstances<LevelEditorWindow>()) return;
            LevelEditorWindow.Open();
        }
    }
}
#endif