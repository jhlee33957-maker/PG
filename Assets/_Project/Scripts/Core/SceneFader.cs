using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// 페이드 아웃 → 씬 로드 → 페이드 인.
    /// 씬에 따로 배치할 필요 없다. 처음 호출될 때 검은 화면 오버레이를 스스로 만들고,
    /// 씬이 바뀌어도 유지되어야 페이드 인을 이어서 할 수 있으므로 DontDestroyOnLoad로 둔다.
    /// </summary>
    public class SceneFader : MonoBehaviour
    {
        private const float DefaultFadeDuration = 0.5f;

        // 다른 어떤 UI보다 위에 그려지도록
        private const int OverlaySortingOrder = 1000;

        private static SceneFader _instance;

        private CanvasGroup _canvasGroup;
        private bool _isLoading;

        /// <summary>전환 중이면 true. 전환 중 버튼을 연타해도 무시하기 위해 쓴다.</summary>
        public static bool IsLoading => _instance != null && _instance._isLoading;

        /// <summary>
        /// 페이드하며 씬을 전환한다. 씬은 Build Profiles의 Scene List에 등록되어 있어야 한다.
        /// </summary>
        public static void LoadScene(string sceneName, float fadeDuration = DefaultFadeDuration)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("[SceneFader] 씬 이름이 비어 있습니다.");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneFader] '{sceneName}' 씬을 불러올 수 없습니다. 씬 이름이 맞는지, Build Profiles의 Scene List에 등록되어 있는지 확인하세요.");
                return;
            }

            SceneFader fader = GetOrCreate();
            if (fader == null || fader._canvasGroup == null || !fader.isActiveAndEnabled)
            {
                Debug.LogWarning("[SceneFader] 페이드 화면이 없어 씬 전환을 시작하지 못함");
                return;
            }
            if (fader._isLoading)
            {
                return;
            }

            fader.StartCoroutine(fader.FadeAndLoad(sceneName, fadeDuration));
        }

        private static SceneFader GetOrCreate()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject("[SceneFader]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SceneFader>();
            _instance.BuildOverlay();
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void BuildOverlay()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;

            // 페이드 중에는 뒤쪽 버튼이 눌리지 않도록 이 캔버스가 클릭을 받아 막는다
            gameObject.AddComponent<GraphicRaycaster>();

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;

            var imageGo = new GameObject("Black", typeof(RectTransform), typeof(Image));
            imageGo.transform.SetParent(transform, false);

            var rect = (RectTransform)imageGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            imageGo.GetComponent<Image>().color = Color.black;
        }

        private IEnumerator FadeAndLoad(string sceneName, float duration)
        {
            _isLoading = true;
            try
            {
                if (_canvasGroup == null) yield break;
                _canvasGroup.blocksRaycasts = true;
                yield return Fade(0f, 1f, duration);
                if (_canvasGroup == null) yield break;

                AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
                while (operation != null && !operation.isDone) yield return null;
                yield return Fade(1f, 0f, duration);
            }
            finally
            {
                // 로드 실패나 오버레이 파괴 때도 입력 잠금과 전환 상태를 남기지 않는다.
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 0f;
                    _canvasGroup.blocksRaycasts = false;
                }
                _isLoading = false;
            }
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            // 일시정지(timeScale = 0) 중에도 페이드가 멈추지 않도록 unscaled 시간을 쓴다
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (_canvasGroup == null) yield break;
                elapsed += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            if (_canvasGroup != null) _canvasGroup.alpha = to;
        }
    }
}
