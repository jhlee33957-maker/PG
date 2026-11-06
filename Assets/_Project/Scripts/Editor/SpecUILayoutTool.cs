using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// 기획서 UI(SpecUI2) 프리팹의 하단 단어 바를 두 줄로 바꾼다.
    /// 프리팹 파일을 텍스트로 고치지 않고, Unity API로 열어서 수정 후 저장한다.
    /// 다시 실행해도 결과는 같다.
    /// </summary>
    public static class SpecUILayoutTool
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/SpecUI2.prefab";

        // 하단 바: Thinking... 줄 + 단어 두 줄이 들어가는 높이
        private const float BarHeight = 220f;

        // 단어 버튼 한 칸 (WordButton 프리팹 크기와 같게)
        private static readonly Vector2 CellSize = new Vector2(150f, 52f);
        private static readonly Vector2 CellSpacing = new Vector2(20f, 14f);

        // 단어 영역: Build 버튼 왼쪽까지, 한 줄에 6개 → 두 줄 12개
        private const float ContainerWidth = 1100f;
        private const float ContainerLeft = 24f;
        private const float ContainerBottom = 30f;

        [MenuItem("CoffeeSpec/Spec UI/하단 단어 바 두 줄로")]
        private static void MakeOptionBarTwoRows()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                RectTransform bar = FindRect(root.transform, "OptionBar");
                RectTransform container = FindRect(root.transform, "OptionContainer");
                if (bar == null || container == null)
                {
                    Debug.LogError("[SpecUILayoutTool] OptionBar 또는 OptionContainer를 찾지 못했습니다.");
                    return;
                }

                float oldHeight = bar.sizeDelta.y;
                float delta = BarHeight - oldHeight;

                // 하단 바 높이 (앵커 아래쪽 기준)
                bar.sizeDelta = new Vector2(bar.sizeDelta.x, BarHeight);
                bar.anchoredPosition = new Vector2(bar.anchoredPosition.x, BarHeight * bar.pivot.y);

                // 하단 바 위에 붙어 있던 영역(CodeArea, Sidebar)을 같이 올린다
                foreach (Transform child in bar.parent)
                {
                    var rect = child as RectTransform;
                    if (rect == null || rect == bar || rect.anchorMin.y != 0f)
                    {
                        continue;
                    }
                    if (Mathf.Abs(rect.offsetMin.y - oldHeight) < 0.5f)
                    {
                        rect.offsetMin = new Vector2(rect.offsetMin.x, rect.offsetMin.y + delta);
                    }
                }

                // 가로 한 줄 배치 → 칸 배치 (넘치면 다음 줄)
                var horizontal = container.GetComponent<HorizontalLayoutGroup>();
                if (horizontal != null)
                {
                    Object.DestroyImmediate(horizontal);
                }

                var grid = container.GetComponent<GridLayoutGroup>();
                if (grid == null)
                {
                    grid = container.gameObject.AddComponent<GridLayoutGroup>();
                }
                grid.cellSize = CellSize;
                grid.spacing = CellSpacing;
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                grid.childAlignment = TextAnchor.UpperCenter;
                grid.constraint = GridLayoutGroup.Constraint.Flexible;

                float containerHeight = CellSize.y * 2f + CellSpacing.y;
                container.anchorMin = Vector2.zero;
                container.anchorMax = Vector2.zero;
                container.pivot = Vector2.zero;
                container.anchoredPosition = new Vector2(ContainerLeft, ContainerBottom);
                container.sizeDelta = new Vector2(ContainerWidth, containerHeight);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[SpecUILayoutTool] 하단 바 {oldHeight} → {BarHeight}, 단어 두 줄 배치로 변경했습니다.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static RectTransform FindRect(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child as RectTransform;
                }
                RectTransform found = FindRect(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
