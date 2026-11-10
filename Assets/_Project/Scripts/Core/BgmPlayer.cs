using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// 씬에 맞는 배경음악을 반복 재생한다. 타이틀은 Resources/Bgm/apple_cider, 스테이지는 Resources/Bgm/little_cafe.
    /// 씬마다 오브젝트를 두지 않아도 되게 게임이 켜질 때 한 번 만들고 씬이 바뀌어도 남긴다.
    /// 같은 곡을 쓰는 씬끼리(스테이지 → 다음 스테이지) 넘어갈 때는 끊지 않고 이어 틀고,
    /// 곡이 바뀌면 앞 곡을 서서히 줄인 뒤 새 곡을 서서히 키운다. 곡이 없는 씬(인트로 등)에서는 서서히 끈다.
    /// </summary>
    public sealed class BgmPlayer : MonoBehaviour
    {
        private const string TitleClip = "Bgm/apple_cider";
        private const string StageClip = "Bgm/little_cafe";
        private const string TitleSceneName = "Title";
        // 효과음(PlayOneShot)이 묻히지 않을 정도로 낮춘 기본 볼륨
        private const float Volume = 0.5f;
        private const float FadeInTime = 1f;
        private const float FadeOutTime = 0.6f;

        private static BgmPlayer _instance;

        private AudioSource _source;
        private Coroutine _change;
        private string _currentPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("BgmPlayer");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BgmPlayer>();
        }

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.volume = 0f;
            // 기획서가 열려 timeScale이 0이어도 음악은 계속 흐른다 (AudioSource는 timeScale과 무관)
            _source.ignoreListenerPause = true;
            SceneManager.activeSceneChanged += OnSceneChanged;
            GameSettings.VolumeChanged += OnVolumeChanged;
            Apply(SceneManager.GetActiveScene());
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            GameSettings.VolumeChanged -= OnVolumeChanged;
            if (_instance == this) _instance = null;
        }

        private void OnSceneChanged(Scene from, Scene to) => Apply(to);

        // 설정 화면의 배경음악 볼륨(배율)을 곱한 실제 목표 볼륨
        private static float TargetVolume => Volume * GameSettings.BgmVolume;

        // 설정에서 볼륨을 움직이면 페이드 중이 아닐 때 바로 따라간다 (페이드 중이면 끝날 때 새 값으로 맞춰짐)
        private void OnVolumeChanged()
        {
            if (_change == null && _source != null && _source.isPlaying) _source.volume = TargetVolume;
        }

        private static string ClipFor(string sceneName)
        {
            if (sceneName == TitleSceneName) return TitleClip;
            if (StageScenes.TryGetStageNumber(sceneName, out _)) return StageClip;
            return null;
        }

        private void Apply(Scene scene)
        {
            string path = ClipFor(scene.name);
            if (path == _currentPath && (path == null || _source.isPlaying)) return;
            _currentPath = path;
            if (_change != null) StopCoroutine(_change);
            _change = StartCoroutine(ChangeTo(path));
        }

        private IEnumerator ChangeTo(string path)
        {
            if (_source.isPlaying)
            {
                yield return Fade(0f, FadeOutTime);
                _source.Stop();
            }
            if (path == null) { _change = null; yield break; }

            var clip = Resources.Load<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"[BgmPlayer] Resources/{path} 음악을 찾지 못했다");
                _change = null;
                yield break;
            }
            _source.clip = clip;
            _source.volume = 0f;
            _source.Play();
            yield return Fade(TargetVolume, FadeInTime);
            _change = null;
        }

        private IEnumerator Fade(float target, float time)
        {
            float from = _source.volume;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                _source.volume = Mathf.Lerp(from, target, t / time);
                yield return null;
            }
            _source.volume = target;
        }
    }
}
