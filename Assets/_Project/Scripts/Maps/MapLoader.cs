using UnityEngine;

namespace CoffeeSpec.Maps
{
    /// <summary>
    /// 씬이 시작할 때 "Level" 밑의 배치를 통째로 복사해 숨겨 두고, 리트할 때 그 복사본으로 다시 깐다.
    /// 씬은 바꾸지 않아서 기획서 화면에 고른 단어가 그대로 남는다.
    /// </summary>
    public class MapLoader : MonoBehaviour
    {
        private const string LevelRootName = "Level";

        private GameObject _snapshot; // 시작 상태 그대로의 Level 복사본 (꺼 둠)

        private void Awake()
        {
            var root = FindRoot();
            if (root == null)
            {
                Debug.LogWarning("[MapLoader] 씬에 Level 오브젝트가 없어 리트할 배치를 기억하지 못함");
                return;
            }
            // 복사본이 켜진 채로 생기면 RuleManager에 등록되므로 원본을 잠깐 끄고 복사한다
            bool wasActive = root.activeSelf;
            root.SetActive(false);
            _snapshot = Instantiate(root);
            _snapshot.name = LevelRootName + "_Snapshot";
            root.SetActive(wasActive);
        }

        /// <summary>지금 Level을 지우고 시작할 때의 배치로 되돌린다.</summary>
        public void Reload()
        {
            if (_snapshot == null)
            {
                Debug.LogError("[MapLoader] 기억해 둔 배치가 없음");
                return;
            }
            var old = FindRoot();
            if (old != null)
            {
                // 끄면서 바로 RuleManager 목록에서 빠지게 한 뒤 지운다 (Destroy는 프레임 끝에 일어나서)
                old.SetActive(false);
                Destroy(old);
            }
            var fresh = Instantiate(_snapshot);
            fresh.name = LevelRootName;
            fresh.SetActive(true);
        }

        private GameObject FindRoot()
        {
            foreach (var go in gameObject.scene.GetRootGameObjects())
                if (go.name == LevelRootName) return go;
            return null;
        }
    }
}