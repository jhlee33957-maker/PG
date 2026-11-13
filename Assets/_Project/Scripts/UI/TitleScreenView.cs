using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>효과음 종류. TitleScreenView.PlaySfx 로 재생한다.</summary>
    public enum TitleSfx
    {
        MonitorOn,
        CoffeeDrop,
        Keyboard,
        MenuExecute,
    }

    /// <summary>
    /// 모니터 안 타이틀 화면 (VS Code 스타일 에디터).
    /// 제목/주석 문구, 메뉴 잠금, 스캔라인·빛 번짐 강도, 효과음을 관리한다.
    /// 메뉴 버튼 로직은 TitleUI 가 그대로 담당한다.
    /// </summary>
    public class TitleScreenView : MonoBehaviour
    {
        [Header("제목")]
        [Tooltip("게임 제목. 주석 줄 위에 크게 표시된다")]
        [SerializeField] private string _gameTitle = "Cat Script";
        [SerializeField] private TMP_Text _titleText;
        [Tooltip("제목 앞에 붙는 주석 기호")]
        [SerializeField] private string _titlePrefix = "//";
        [SerializeField] private Color _titlePrefixColor = new Color32(0x4E, 0xC9, 0xB0, 0xFF);

        [Header("주석 줄 (위에서부터)")]
        [SerializeField] private TMP_Text[] _commentTargets = new TMP_Text[0];
        [SerializeField, TextArea] private string[] _commentTexts = new string[0];

        [Header("메뉴")]
        [Tooltip("메뉴 줄들의 부모. 연출 중에는 이 CanvasGroup 으로 클릭을 막는다")]
        [SerializeField] private CanvasGroup _menuGroup;

        [Header("화면 효과")]
        [SerializeField] private RawImage _scanlines;
        [Tooltip("스캔라인 진하기. 0 = 끔. 너무 높으면 글자가 안 읽힌다")]
        [SerializeField, Range(0f, 0.5f)] private float _scanlineIntensity = 0.06f;
        [Tooltip("스캔라인 한 줄 간격 (캔버스 단위, 1920x1080 기준)")]
        [SerializeField, Min(2)] private int _scanlinePeriod = 4;
        [SerializeField] private RawImage _glow;
        [Tooltip("화면 가장자리 비네트(부드럽게 어두워짐) 진하기. 0 = 끔. 가운데 타원은 없음")]
        [SerializeField, Range(0f, 1f)] private float _glowIntensity = 0.25f;
        [SerializeField] private Color _glowColor = new Color(0f, 0f, 0f, 1f);

        [Header("효과음 (비어 있어도 동작)")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _sfxMonitorOn;
        [SerializeField] private AudioClip _sfxCoffeeDrop;
        [SerializeField] private AudioClip _sfxKeyboard;
        [SerializeField] private AudioClip _sfxMenuExecute;
        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 0.8f;

        private bool _menuEnabled = true;

        /// <summary>메뉴 줄 하나가 실행 연출 중이면 true. 그동안 다른 줄은 클릭되지 않는다.</summary>
        public bool IsExecuting { get; set; }

        public bool MenuEnabled => _menuEnabled;

        public string GameTitle
        {
            get => _gameTitle;
            set { _gameTitle = value; ApplyTexts(); }
        }

        private void Awake()
        {
            EnsureEffectTextures();
            ApplyTexts();
            ApplyEffects();
        }

        private void OnValidate()
        {
            ApplyTexts();
            ApplyEffects();
        }

        private void OnRectTransformDimensionsChange()
        {
            ApplyEffects();
        }

        /// <summary>연출 중에는 false 로 메뉴를 잠근다.</summary>
        public void SetMenuEnabled(bool enabled)
        {
            _menuEnabled = enabled;
            if (_menuGroup != null)
            {
                _menuGroup.interactable = enabled;
                _menuGroup.blocksRaycasts = enabled;
            }
        }

        public void PlaySfx(TitleSfx sfx)
        {
            AudioClip clip = null;
            switch (sfx)
            {
                case TitleSfx.MonitorOn: clip = _sfxMonitorOn; break;
                case TitleSfx.CoffeeDrop: clip = _sfxCoffeeDrop; break;
                case TitleSfx.Keyboard: clip = _sfxKeyboard; break;
                case TitleSfx.MenuExecute: clip = _sfxMenuExecute; break;
            }

            if (clip == null || _audioSource == null)
            {
                return;
            }
            _audioSource.PlayOneShot(clip, _sfxVolume);
        }

        private void ApplyTexts()
        {
            if (_titleText != null)
            {
                string hex = ColorUtility.ToHtmlStringRGB(_titlePrefixColor);
                _titleText.text = $"<color=#{hex}>{_titlePrefix}</color>{_gameTitle}";
            }

            if (_commentTargets == null || _commentTexts == null)
            {
                return;
            }

            int count = Mathf.Min(_commentTargets.Length, _commentTexts.Length);
            for (int i = 0; i < count; i++)
            {
                if (_commentTargets[i] != null)
                {
                    _commentTargets[i].text = _commentTexts[i];
                }
            }
        }

        private void ApplyEffects()
        {
            if (_scanlines != null)
            {
                _scanlines.color = new Color(0f, 0f, 0f, _scanlineIntensity);
                _scanlines.enabled = _scanlineIntensity > 0.0001f;
                float height = ((RectTransform)_scanlines.transform).rect.height;
                if (height <= 0f)
                {
                    height = 1080f;
                }
                _scanlines.uvRect = new Rect(0f, 0f, 1f, height / Mathf.Max(2, _scanlinePeriod));
            }

            if (_glow != null)
            {
                Color c = _glowColor;
                c.a = _glowIntensity;
                _glow.color = c;
                _glow.enabled = _glowIntensity > 0.0001f;
            }
        }

        /// <summary>씬 빌더가 텍스처를 넣지 않았으면 실행 중에 만든다.</summary>
        private void EnsureEffectTextures()
        {
            if (_scanlines != null && _scanlines.texture == null)
            {
                _scanlines.texture = CreateScanlineTexture();
            }
            if (_glow != null && _glow.texture == null)
            {
                _glow.texture = CreateGlowTexture(128);
            }
        }

        /// <summary>1x2 텍스처: 위 투명, 아래 흰색. RawImage 색의 알파로 진하기를 조절한다.</summary>
        public static Texture2D CreateScanlineTexture()
        {
            var tex = new Texture2D(1, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                name = "TitleScanline",
            };
            tex.SetPixel(0, 0, Color.white);
            tex.SetPixel(0, 1, new Color(1f, 1f, 1f, 0f));
            tex.Apply();
            return tex;
        }

        /// <summary>가운데는 투명하고 가장자리로 갈수록 부드럽게 진해지는 비네트 (계단 없음, Bilinear).</summary>
        public static Texture2D CreateGlowTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "TitleScreenVignette",
            };
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f; // 모서리 = 1
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
