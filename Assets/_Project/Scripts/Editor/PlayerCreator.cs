#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CoffeeSpec.Entities;
using CoffeeSpec.Player;
using CoffeeSpec.Rules;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// 플랫포머 플레이어 프리팹을 만들고 현재 씬에 하나 배치한다.
    /// 프리팹 텍스트를 직접 쓰지 않고 Unity가 저장하게 하려고 만든 메뉴.
    /// </summary>
    public static class PlayerCreator
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        [MenuItem("CoffeeSpec/Create Platformer Player")]
        private static void Create()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = BuildPrefab();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Create Platformer Player");
            Selection.activeGameObject = instance;
            Debug.Log($"[PlayerCreator] 씬에 플레이어 배치 ({PrefabPath})");
        }

        private static GameObject BuildPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
                AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");

            var go = new GameObject("Player");

            // 스프라이트 시트가 준비되기 전까지 임시 사각형으로 보인다
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(0.8f, 1.2f);
            sr.color = new Color(0.4f, 0.8f, 1f);

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.8f, 1.2f);

            var entity = go.AddComponent<RuleEntity>();
            var so = new SerializedObject(entity);
            so.FindProperty("_id").enumValueIndex = (int)WordId.Person;
            so.ApplyModifiedPropertiesWithoutUndo();

            go.AddComponent<PlatformerPlayer>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
#endif