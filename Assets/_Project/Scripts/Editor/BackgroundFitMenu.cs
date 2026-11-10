#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// 선택한 배경 스프라이트를 맵 테두리 벽 안쪽에 꽉 차게 맞춘다.
    /// 배경 이미지의 PPU나 지금 Scale 값과 상관없이 위치와 Scale을 직접 계산해서, 손으로 숫자를 맞출 필요가 없게 한다.
    /// 맵 범위는 씬의 "Level" 오브젝트 아래 렌더러 전체이고, 바깥 테두리 벽 두께(1칸)만큼 안쪽으로 줄인 곳을 채운다.
    /// </summary>
    public static class BackgroundFitMenu
    {
        private const string MenuPath = "CoffeeSpec/Background/선택한 배경을 맵 안쪽에 꽉 채우기";
        private const string LevelRootName = "Level";
        // 맵 바깥 테두리 벽 두께(월드 단위). 벽 타일 한 칸 = 1
        private const float WallThickness = 1f;

        [MenuItem(MenuPath)]
        private static void FitToLevel()
        {
            GameObject target = Selection.activeGameObject;
            SpriteRenderer renderer = target != null ? target.GetComponent<SpriteRenderer>() : null;
            if (renderer == null || renderer.sprite == null)
            {
                EditorUtility.DisplayDialog("배경 맞추기", "배경 스프라이트(SpriteRenderer)가 붙은 오브젝트를 하이어라키에서 선택해 주세요.", "확인");
                return;
            }

            GameObject level = FindRoot(target, LevelRootName);
            if (level == null)
            {
                EditorUtility.DisplayDialog("배경 맞추기", $"씬에서 '{LevelRootName}' 오브젝트를 찾지 못했습니다.", "확인");
                return;
            }
            if (target.transform.IsChildOf(level.transform))
            {
                EditorUtility.DisplayDialog("배경 맞추기", $"배경이 '{LevelRootName}' 안에 있으면 맵 범위가 배경 크기로 잡힙니다. 배경을 '{LevelRootName}' 바깥으로 빼 주세요.", "확인");
                return;
            }

            bool found = false;
            Bounds map = default;
            foreach (Renderer r in level.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!found) { map = r.bounds; found = true; }
                else map.Encapsulate(r.bounds);
            }
            if (!found)
            {
                EditorUtility.DisplayDialog("배경 맞추기", $"'{LevelRootName}' 아래에 보이는 오브젝트가 없습니다.", "확인");
                return;
            }

            Vector2 inner = new Vector2(map.size.x - WallThickness * 2f, map.size.y - WallThickness * 2f);
            // sprite.bounds는 Scale 1일 때 크기라서, 원하는 크기를 이것으로 나누면 필요한 Scale이 된다
            Bounds spriteBounds = renderer.sprite.bounds;
            Vector3 scale = new Vector3(inner.x / spriteBounds.size.x, inner.y / spriteBounds.size.y, 1f);

            Undo.RecordObject(target.transform, "배경 맞추기");
            if (target.transform.parent != null)
            {
                Undo.SetTransformParent(target.transform, null, "배경 맞추기");
            }
            target.transform.rotation = Quaternion.identity;
            target.transform.localScale = scale;
            // 스프라이트 기준점이 가운데가 아니어도 그림 가운데가 맵 가운데에 오게 한다
            Vector3 offset = Vector3.Scale(spriteBounds.center, scale);
            target.transform.position = new Vector3(map.center.x - offset.x, map.center.y - offset.y, target.transform.position.z);

            EditorSceneManager.MarkSceneDirty(target.scene);
            float ratioGap = Mathf.Abs(scale.x - scale.y) / Mathf.Max(scale.x, scale.y);
            string msg = $"'{target.name}'({renderer.sprite.name})을 {inner.x:0.##}x{inner.y:0.##} 크기로 맞췄습니다. Scale ({scale.x:0.###}, {scale.y:0.###})";
            if (ratioGap > 0.02f) msg += " - 이미지 비율이 맵과 달라 늘어나 보입니다. 맵 크기에 맞게 만든 배경 이미지인지 확인해 주세요.";
            Debug.Log("[BackgroundFitMenu] " + msg, target);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanFit()
        {
            return Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<SpriteRenderer>() != null;
        }

        private static GameObject FindRoot(GameObject from, string name)
        {
            foreach (GameObject root in from.scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
            }
            return null;
        }
    }
}
#endif
