using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// 스테이지 씬에서 배경음악(Resources/Bgm/little_cafe)을 반복 재생한다.
    /// 씬마다 오브젝트를 두지 않아도 되게 게임이 켜질 때 한 번 만들고 씬이 바뀌어도 남긴다.
    /// 스테이지끼리 넘어갈 때는 끊지 않고 이어 틀고, 타이틀·인트로처럼 스테이지가 아닌 씬에서는 서서히 끈다.
    /// </summary>
    public sealed class BgmPlayer : MonoBehaviour
    {
        private const string ClipPath = "Bgm/little_cafe";
        // 효과음(PlayOneShot)이 묻히지 않을 정도로 낮춘 기본 볼륨
        private const float Volume = 0.5f;
        private const float FadeInTime = 1f;
        private const float FadeOutTime = 0.6f;

        private static BgmPlayer _instance;

        private AudioSource _source;
        private Coroutine _fade;

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
            Apply(SceneManager.GetActiveScene());
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (_instance == this) _instance = null;
        }

        private void OnSceneChanged(Scene from, Scene to) => Apply(to);

        private void Apply(Scene scene)
        {
            bool stage = StageScenes.TryGetStageNumber(scene.name, out _);
            if (stage)
            {
                if (_source.clip == null)
                {
                    _source.clip = Resources.Load<AudioClip>(ClipPath);
                    if (_source.clip == null)
                    {
                        Debug.LogWarning($"[BgmPlayer] Resources/{ClipPath} 음악을 찾지 못했다");
                        return;
                    }
                }
                if (!_source.isPlaying) _source.Play();
                FadeTo(Volume, FadeInTime, false);
            }
            else if (_source.isPlaying)
            {
                FadeTo(0f, FadeOutTime, true);
            }
        }

        private void FadeTo(float target, float time, bool stopAtEnd)
        {
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(Fade(target, time, stopAtEnd));
        }

        private IEnumerator Fade(float target, float time, bool stopAtEnd)
        {
            float from = _source.volume;
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                _source.volume = Mathf.Lerp(from, target, t / time);
                yield return null;
            }
            _source.volume = target;
            if (stopAtEnd) _source.Stop();
            _fade = null;
        }
    }
}
