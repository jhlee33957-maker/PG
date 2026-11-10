using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// ESC 설정 화면에서 고르는 값(배경음악·효과음 볼륨, 화면 모드, 해상도)을 보관하고 PlayerPrefs에 저장한다.
    /// 볼륨은 각 소리의 원래 볼륨에 곱하는 배율(0~1)이라, 100이면 지금까지 맞춰 둔 소리 크기 그대로 난다.
    /// </summary>
    public static class GameSettings
    {
        private const string BgmKey = "CoffeeSpec.Settings.BgmVolume";
        private const string SfxKey = "CoffeeSpec.Settings.SfxVolume";
        private const string WidthKey = "CoffeeSpec.Settings.Width";
        private const string HeightKey = "CoffeeSpec.Settings.Height";
        private const string FullscreenKey = "CoffeeSpec.Settings.Fullscreen";

        /// <summary>볼륨이 바뀔 때마다 알린다. 이미 재생 중인 배경음악이 바로 따라 바뀌게 하려고 쓴다.</summary>
        public static event Action VolumeChanged;

        public static float BgmVolume { get; private set; } = 1f;
        public static float SfxVolume { get; private set; } = 1f;

        // 씬에 미리 놓인 효과음 AudioSource(타이틀 메뉴 소리 등)의 원래 볼륨. 배율을 여러 번 곱해 작아지지 않게 기억해 둔다
        private static readonly Dictionary<AudioSource, float> BaseVolumes = new Dictionary<AudioSource, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            BgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmKey, 1f));
            SfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, 1f));
            BaseVolumes.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        public static void SetBgmVolume(float value)
        {
            BgmVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(BgmKey, BgmVolume);
            PlayerPrefs.Save();
            VolumeChanged?.Invoke();
        }

        public static void SetSfxVolume(float value)
        {
            SfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxKey, SfxVolume);
            PlayerPrefs.Save();
            ApplySfxToLoadedSources();
            VolumeChanged?.Invoke();
        }

        /// <summary>지금 창 상태가 전체화면인지. 창모드(Windowed)가 아니면 전체화면으로 본다.</summary>
        public static bool IsFullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        /// <summary>
        /// 해상도와 화면 모드를 한 번에 바꾸고 저장한다. 전체화면은 테두리 없는 전체 창(FullScreenWindow)을 쓴다.
        /// 에디터 Game 뷰에서는 Unity가 해상도 변경을 무시하므로 빌드에서 확인해야 한다.
        /// </summary>
        public static void ApplyDisplay(int width, int height, bool fullscreen)
        {
            var mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Screen.SetResolution(width, height, mode);
            PlayerPrefs.SetInt(WidthKey, width);
            PlayerPrefs.SetInt(HeightKey, height);
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>마지막으로 적용한 해상도. 저장된 값이 없으면 지금 화면 크기.</summary>
        public static Vector2Int SavedResolution =>
            new Vector2Int(PlayerPrefs.GetInt(WidthKey, Screen.width), PlayerPrefs.GetInt(HeightKey, Screen.height));

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 사라진 씬의 AudioSource는 키가 null처럼 되므로 정리한다
            var dead = new List<AudioSource>();
            foreach (var kv in BaseVolumes) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var key in dead) BaseVolumes.Remove(key);
            ApplySfxToLoadedSources();
        }

        // 배경음악(BgmPlayer)과 고양이 소리(CatSfx, 재생할 때 직접 배율을 곱함)를 뺀 나머지 AudioSource에 효과음 배율을 건다
        private static void ApplySfxToLoadedSources()
        {
            var sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include);
            foreach (var source in sources)
            {
                if (source == null) continue;
                if (source.GetComponent<BgmPlayer>() != null) continue;
                if (source.gameObject.name == CatSfx.SourceName) continue;
                if (!BaseVolumes.TryGetValue(source, out float baseVolume))
                {
                    baseVolume = source.volume;
                    BaseVolumes[source] = baseVolume;
                }
                source.volume = baseVolume * SfxVolume;
            }
        }
    }
}
