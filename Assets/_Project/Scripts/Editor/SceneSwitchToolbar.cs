#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// 플레이 버튼 옆의 씬 전환 버튼. 지금 있는 곳을 이름으로 보여 준다.
    /// EditorScene에서는 "에디터씬"이고 누르면 지금 고른 맵 데이터의 스테이지 씬을 연다.
    /// 다른 씬에서는 "스테이지씬"이고 누르면 EditorScene을 연다.
    /// </summary>
    internal static class SceneSwitchToolbar
    {
        private const string ElementPath = "CoffeeSpec/Scene Switch";
        private const string EditorLabel = "에디터씬";
        private const string StageLabel = "스테이지씬";
        private const string StyledClass = "coffeespec-scene-switch";

        // 플레이 버튼 묶음과 같은 색과 크기 (화면에서 잰 값)
        private static readonly Color ButtonColor = new Color(80f / 255f, 80f / 255f, 80f / 255f);
        private static readonly Color HoverColor = new Color(96f / 255f, 96f / 255f, 96f / 255f);
        private const float ButtonHeight = 20f;

        // 플레이 버튼 왼쪽에 오도록 가운데 묶음의 맨 앞에 둔다
        [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = -100)]
        private static MainToolbarElement Create()
        {
            bool inEditor = SceneManager.GetActiveScene().path == LevelEditorWindow.EditorScenePath;
            var content = inEditor
                ? new MainToolbarContent(EditorLabel, "누르면 지금 고른 맵 데이터의 스테이지 씬으로 이동")
                : new MainToolbarContent(StageLabel, "누르면 EditorScene으로 이동");
            EditorApplication.delayCall += StyleButton;
            return new MainToolbarButton(content, inEditor ? (System.Action)GoToStage : GoToEditor);
        }

        // 씬이 바뀌면 버튼 이름도 바꾼다
        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorSceneManager.activeSceneChangedInEditMode += (_, _) => MainToolbar.Refresh(ElementPath);
            EditorSceneManager.sceneOpened += (_, _) => MainToolbar.Refresh(ElementPath);
            EditorApplication.delayCall += StyleButton;
        }

        /// <summary>
        /// 툴바 버튼 API에는 배경색 설정이 없어서, 그려진 버튼을 UI 트리에서 찾아 플레이 버튼처럼 회색 박스로 칠한다.
        /// 툴바가 다시 그려지면 스타일이 사라지므로 Create가 불릴 때마다 다시 칠한다.
        /// </summary>
        private static void StyleButton()
        {
            foreach (var root in ToolbarRoots())
            {
                root.Query<TextElement>().ForEach(text =>
                {
                    if (text.text != EditorLabel && text.text != StageLabel) return;
                    VisualElement button = text;
                    while (button != null && !(button is Button)) button = button.parent;
                    if (button == null) button = text.parent ?? text;
                    Apply(button);
                });
            }
        }

        private static void Apply(VisualElement button)
        {
            var s = button.style;
            s.backgroundColor = ButtonColor;
            s.height = ButtonHeight;
            s.paddingLeft = 8; s.paddingRight = 8;
            s.marginRight = 3;
            s.borderTopLeftRadius = 3; s.borderTopRightRadius = 3;
            s.borderBottomLeftRadius = 3; s.borderBottomRightRadius = 3;
            s.borderLeftWidth = 0; s.borderRightWidth = 0; s.borderTopWidth = 0; s.borderBottomWidth = 0;
            s.alignSelf = Align.Center;
            if (button.ClassListContains(StyledClass)) return;
            button.AddToClassList(StyledClass);
            button.RegisterCallback<MouseEnterEvent>(_ => button.style.backgroundColor = HoverColor);
            button.RegisterCallback<MouseLeaveEvent>(_ => button.style.backgroundColor = ButtonColor);
        }

        // 메인 툴바는 EditorWindow가 아니라 내부 GUIView라서 visualTree를 리플렉션으로 꺼낸다
        private static System.Collections.Generic.IEnumerable<VisualElement> ToolbarRoots()
        {
            var guiViewType = typeof(Editor).Assembly.GetType("UnityEditor.GUIView");
            if (guiViewType == null) yield break;
            var prop = guiViewType.GetProperty("visualTree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null) yield break;
            foreach (var view in Resources.FindObjectsOfTypeAll(guiViewType))
            {
                if (!view.GetType().Name.Contains("Toolbar")) continue;
                if (prop.GetValue(view) is VisualElement tree) yield return tree;
            }
        }

        private static void GoToEditor() => Open(LevelEditorWindow.EditorScenePath);

        private static void GoToStage()
        {
            string path = LevelEditorWindow.CurrentMapScenePath();
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("스테이지씬", "레벨 에디터에서 맵을 먼저 골라줘.", "확인");
                return;
            }
            Open(path);
        }

        private static void Open(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return; // 플레이 중에는 씬을 바꾸지 않는다
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }
    }
}
#endif