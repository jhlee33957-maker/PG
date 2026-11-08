using System;
using System.Collections;
using CoffeeSpec.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// Intro 씬 연출. 컷 목록을 순서대로 재생하고, 마지막 컷이 끝나면 스테이지 씬으로 넘어간다.
    /// 컷마다 보여 줄 오브젝트, 재생 시간, 효과음, 텍스트 이벤트(비트)를 Inspector에서 할당한다.
    /// 화면 클릭 / 아무 키 = 다음 컷, BTN_Skip = 바로 스테이지 씬.
    /// </summary>
    public class IntroDirector : MonoBehaviour
    {
        /// <summary>컷 안에서 순서대로 실행되는 이벤트 하나. 앞 비트가 끝난 뒤 _delay만큼 기다렸다 실행된다.</summary>
        [Serializable]
        private class IntroBeat
        {
            [Tooltip("메모용 이름 (예: 메신저 알림, 상태 → 퇴근)")]
            [SerializeField] private string _label;

            [Tooltip("앞 비트가 끝난 뒤(첫 비트는 컷 시작 후) 기다리는 시간(초)")]
            [SerializeField, Min(0f)] private float _delay;

            [Tooltip("이 비트에서 켤 오브젝트 (예: 메신저 알림 팝업, 보낸 메시지 말풍선)")]
            [SerializeField] private GameObject _activate;

            [Tooltip("이 비트에서 끌 오브젝트")]
            [SerializeField] private GameObject _deactivate;

            [Tooltip("텍스트를 바꿀 대상 (예: 기획자 상태 텍스트, 코드 에디터 줄)")]
            [SerializeField] private TMP_Text _textTarget;

            [SerializeField, TextArea] private string _text;

            [Tooltip("켜면 한 글자씩 타이핑된다. 끄면 바로 바뀐다")]
            [SerializeField] private bool _typewriter;

            [Tooltip("타이핑 글자 간격(초)")]
            [SerializeField, Min(0.005f)] private float _charInterval = 0.05f;

            [Tooltip("이 비트가 시작될 때 재생할 효과음 (예: 툭, 주르륵, 메신저 알림음)")]
            [SerializeField] private AudioClip _sfx;
            [SerializeField] private RectTransform _emphasize;
            public RectTransform Emphasize => _emphasize;
            [Header("비트 카메라 이동 (메시지 도착 시 줌)")]
            [SerializeField] private bool _moveCamera;
            [SerializeField] private Vector2 _cameraPosition;
            [SerializeField, Min(0.01f)] private float _cameraScale = 1f;
            [SerializeField, Min(0f)] private float _moveDuration = 0.8f;
            public bool MoveCamera => _moveCamera;
            public Vector2 CameraPosition => _cameraPosition;
            public float CameraScale => _cameraScale;
            public float MoveDuration => _moveDuration;

            public float Delay => _delay;
            public GameObject Activate => _activate;
            public GameObject Deactivate => _deactivate;
            public TMP_Text TextTarget => _textTarget;
            public string Text => _text;
            public bool Typewriter => _typewriter;
            public float CharInterval => _charInterval;
            public AudioClip Sfx => _sfx;
        }

        [Serializable]
        private class IntroCut
        {
            [Tooltip("메모용 이름 (예: 컷1 퇴근)")]
            [SerializeField] private string _label;

            [Tooltip("이 컷 동안 켜져 있을 오브젝트. 다른 컷에만 있는 오브젝트는 자동으로 꺼진다")]
            [SerializeField] private GameObject[] _objects = new GameObject[0];

            [Tooltip("시간 표시 텍스트 (비워 두면 건드리지 않음)")]
            [SerializeField] private TMP_Text _timeLabel;
            [SerializeField] private string _timeText;

            [Tooltip("컷 최소 재생 시간(초). 비트가 더 길면 비트가 끝날 때까지 기다린다")]
            [SerializeField, Min(0f)] private float _duration = 2f;

            [Tooltip("컷이 밝아지는 시간(초). 0이면 바로 전환")]
            [SerializeField, Min(0f)] private float _fadeIn = 0.3f;

            [Tooltip("컷 시작 시 재생할 효과음")]
            [SerializeField] private AudioClip _sfxOnStart;

            [SerializeField] private IntroBeat[] _beats = new IntroBeat[0];

            [Header("연속 카메라 / 암전 (기존 컷은 사용 안 함)")]
            [SerializeField] private bool _cameraEnabled;
            [SerializeField] private Vector2 _cameraPosition;
            [SerializeField, Min(0.01f)] private float _cameraScale = 1f;
            [SerializeField, Min(0f)] private float _moveDuration;
            [SerializeField, Min(0f)] private float _holdBeforeMove;
            public float HoldBeforeMove => _holdBeforeMove;
            [SerializeField] private bool _hardCutOnEnter;
            public bool HardCutOnEnter => _hardCutOnEnter;
            [SerializeField] private bool _blackBefore;
            [SerializeField] private bool _blackAfter;
            [SerializeField] private GameObject _afterMoveActivate;
            [SerializeField, Min(0f)] private float _revealDuration = 0.25f;
            public float RevealDuration => _revealDuration;
            public bool CameraEnabled => _cameraEnabled;
            public Vector2 CameraPosition => _cameraPosition;
            public float CameraScale => _cameraScale;
            public float MoveDuration => _moveDuration;
            public bool BlackBefore => _blackBefore;
            public bool BlackAfter => _blackAfter;
            public GameObject AfterMoveActivate => _afterMoveActivate;

            public GameObject[] Objects => _objects;
            public TMP_Text TimeLabel => _timeLabel;
            public string TimeText => _timeText;
            public float Duration => _duration;
            public float FadeIn => _fadeIn;
            public AudioClip SfxOnStart => _sfxOnStart;
            public IntroBeat[] Beats => _beats;
        }

        [Header("컷 (위에서부터 순서대로 재생)")]
        [SerializeField] private IntroCut[] _cuts = new IntroCut[0];

        [Header("전환")]
        [Tooltip("인트로가 끝나면 넘어갈 스테이지 씬 이름. Build Profiles의 Scene List에 등록되어 있어야 한다")]
        [SerializeField] private string _stageSceneName = "Stage1Scene";

        [Tooltip("마지막 컷 후 페이드 아웃 시간(초)")]
        [SerializeField, Min(0.01f)] private float _endFadeDuration = 0.8f;

        [Tooltip("컷 사이 암전용 검은 Image (화면 전체를 덮고 Raycast Target은 끈다)")]
        [SerializeField] private Image _blackOverlay;

        [Header("입력")]
        [SerializeField] private Button _btnSkip;

        [Tooltip("컷 전환 직후 입력을 무시하는 시간(초). 연타로 컷이 두 개씩 넘어가는 것을 막는다")]
        [SerializeField, Min(0f)] private float _inputCooldown = 0.2f;

        [Header("사운드")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 0.8f;

        [Tooltip("타이핑할 때 글자마다 낼 키보드 소리 (비워 두면 무음)")]
        [SerializeField] private AudioClip _typingSfx;
        [SerializeField, Min(1)] private int _typingSfxEvery = 2;
        [SerializeField, Range(0f, 1f)] private float _typingVolume = 0.4f;

        [Header("UI 카메라")]
        [SerializeField] private RectTransform _world;
        [SerializeField, Min(1f)] private float _maxCameraScale = 3f;
        private RectTransform _popped;
        private Vector3 _popOriginal;
        private bool _cameraMoving;
        private Vector2 _cameraGoalPosition;
        private float _cameraGoalScale = 1f;
        private int _cutIndex = -1;
        private Coroutine _cutRoutine;
        private float _inputBlockedUntil;
        private bool _finished;

        private void Awake()
        {
            if (_btnSkip != null)
            {
                _btnSkip.onClick.AddListener(Finish);
            }
            else
            {
                Debug.LogWarning("[IntroDirector] BTN_Skip이 할당되지 않았습니다.", this);
            }

            // 시작 전 모든 컷 오브젝트와 비트에서 켤 오브젝트를 꺼 둔다
            foreach (IntroCut cut in _cuts)
            {
                SetObjectsActive(cut.Objects, false);
                if (cut.AfterMoveActivate != null) cut.AfterMoveActivate.SetActive(false);
                foreach (IntroBeat beat in cut.Beats)
                {
                    if (beat.Activate != null)
                    {
                        beat.Activate.SetActive(false);
                    }
                }
            }

            SetOverlayAlpha(1f);
        }

        private void Start()
        {
            if (_cuts.Length == 0)
            {
                Debug.LogError("[IntroDirector] 컷이 하나도 없습니다. 바로 스테이지로 넘어갑니다.", this);
                Finish();
                return;
            }

            GoToCut(0);
        }

        private void Update()
        {
            if (_finished || SceneFader.IsLoading || Time.unscaledTime < _inputBlockedUntil)
            {
                return;
            }

            if (WasAdvancePressed())
            {
                Next();
            }
        }

        // ---------------- 진행 ----------------

        private void Next()
        {
            if (_cutIndex + 1 >= _cuts.Length)
            {
                Finish();
                return;
            }

            GoToCut(_cutIndex + 1);
        }

        private void GoToCut(int index)
        {
            if (_cutRoutine != null)
            {
                StopCoroutine(_cutRoutine);
            }

            // Clicking during a tween completes its pose before beginning the next move.
            if (_cameraMoving) SnapCameraGoal();
            ResetPop();
            _cutIndex = index;
            _inputBlockedUntil = Time.unscaledTime + _inputCooldown;
            _cutRoutine = StartCoroutine(PlayCut(_cuts[index]));
        }

        private IEnumerator PlayCut(IntroCut cut)
        {
            // Set the new shot BEFORE hiding the preceding document overlay.
            // This is synchronous: no old paper/cup pose can leak for a frame.
            if (cut.HardCutOnEnter && cut.CameraEnabled)
            {
                _cameraGoalPosition = cut.CameraPosition;
                _cameraGoalScale = Mathf.Clamp(cut.CameraScale, 0.25f, Mathf.Max(1f, _maxCameraScale));
                SnapCameraGoal();
            }
            // 이전 컷 오브젝트를 끄고 이번 컷 오브젝트만 켠다
            foreach (IntroCut other in _cuts)
            {
                if (other != cut)
                {
                    SetObjectsActive(other.Objects, false);
                    if (other.AfterMoveActivate != null) other.AfterMoveActivate.SetActive(false);
                }
            }
            SetObjectsActive(cut.Objects, true);

            if (cut.TimeLabel != null && !string.IsNullOrEmpty(cut.TimeText))
            {
                cut.TimeLabel.text = cut.TimeText;
            }

            // 타이핑할 줄은 비운 채로 시작한다
            foreach (IntroBeat beat in cut.Beats)
            {
                if (beat.Typewriter && beat.TextTarget != null)
                {
                    beat.TextTarget.text = string.Empty;
                }
            }

            PlaySfx(cut.SfxOnStart, _sfxVolume);

            if (cut.BlackBefore) SetOverlayAlpha(1f);
            if (cut.BlackAfter) SetOverlayAlpha(1f);
            else yield return FadeOverlay(_blackOverlay != null ? _blackOverlay.color.a : 0f, 0f, cut.FadeIn);
            yield return WaitUnscaled(cut.HoldBeforeMove);
            yield return MoveCamera(cut);
            if (cut.AfterMoveActivate != null)
            {
                cut.AfterMoveActivate.SetActive(true);
                CanvasGroup reveal = cut.AfterMoveActivate.GetComponent<CanvasGroup>();
                if (reveal != null)
                {
                    float elapsed = 0;
                    reveal.alpha = 0;
                    while (elapsed < cut.RevealDuration)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        reveal.alpha = Mathf.Clamp01(elapsed / cut.RevealDuration);
                        yield return null;
                    }
                    reveal.alpha = 1;
                }
            }
            float startTime = Time.unscaledTime;

            foreach (IntroBeat beat in cut.Beats)
            {
                yield return WaitUnscaled(beat.Delay);
                yield return PlayBeat(beat);
            }

            float remaining = cut.Duration - (Time.unscaledTime - startTime);
            yield return WaitUnscaled(remaining);

            _cutRoutine = null;
            Next();
        }

        private IEnumerator PlayBeat(IntroBeat beat)
        {
            PlaySfx(beat.Sfx, _sfxVolume);

            if (beat.Deactivate != null)
            {
                beat.Deactivate.SetActive(false);
            }
            if (beat.Activate != null)
            {
                beat.Activate.SetActive(true);
            }

            if (beat.MoveCamera) yield return MoveCameraTo(beat.CameraPosition, beat.CameraScale, beat.MoveDuration);

            if (beat.TextTarget == null)
            {
                yield break;
            }

            string text = beat.Text ?? string.Empty;
            if (!beat.Typewriter)
            {
                beat.TextTarget.text = text;
                if (beat.Emphasize != null) yield return Pop(beat.Emphasize);
                yield break;
            }

            for (int i = 1; i <= text.Length; i++)
            {
                beat.TextTarget.text = text.Substring(0, i);
                if (!char.IsWhiteSpace(text[i - 1]) && i % Mathf.Max(1, _typingSfxEvery) == 0)
                {
                    PlaySfx(_typingSfx, _typingVolume);
                }
                yield return WaitUnscaled(beat.CharInterval);
            }
        }

        /// <summary>마지막 컷이 끝났거나 BTN_Skip을 눌렀을 때. 페이드 아웃 후 스테이지 씬으로 간다.</summary>
        private void Finish()
        {
            if (_finished || SceneFader.IsLoading)
            {
                return;
            }

            _finished = true;
            if (_cutRoutine != null)
            {
                StopCoroutine(_cutRoutine);
                _cutRoutine = null;
            }

            SceneFader.LoadScene(_stageSceneName, _endFadeDuration);
        }

        // ---------------- 입력 ----------------

        private bool WasAdvancePressed()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            {
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                return !IsOverSkipButton(Mouse.current.position.ReadValue());
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                return !IsOverSkipButton(Touchscreen.current.primaryTouch.position.ReadValue());
            }

            return false;
        }

        // BTN_Skip을 누른 클릭이 '다음 컷'으로도 처리되지 않게 한다
        private bool IsOverSkipButton(Vector2 screenPosition)
        {
            if (_btnSkip == null || !_btnSkip.gameObject.activeInHierarchy)
            {
                return false;
            }

            var rect = (RectTransform)_btnSkip.transform;
            Canvas canvas = _btnSkip.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, cam);
        }

        // ---------------- 유틸 ----------------

        private void PlaySfx(AudioClip clip, float volume)
        {
            if (clip != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(clip, volume);
            }
        }

        private static void SetObjectsActive(GameObject[] objects, bool active)
        {
            foreach (GameObject go in objects)
            {
                if (go != null)
                {
                    go.SetActive(active);
                }
            }
        }

        private void SetOverlayAlpha(float alpha)
        {
            if (_blackOverlay == null)
            {
                return;
            }

            Color c = _blackOverlay.color;
            c.a = alpha;
            _blackOverlay.color = c;
        }

        private IEnumerator FadeOverlay(float from, float to, float duration)
        {
            if (_blackOverlay == null)
            {
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetOverlayAlpha(Mathf.Lerp(from, to, elapsed / duration));
                yield return null;
            }
            SetOverlayAlpha(to);
        }

        private void SnapCameraGoal()
        {
            if (_world != null)
            {
                _world.anchoredPosition = _cameraGoalPosition;
                _world.localScale = Vector3.one * Mathf.Max(0.01f, _cameraGoalScale);
            }
            _cameraMoving = false;
        }

        private IEnumerator MoveCamera(IntroCut cut)
        {
            if (cut.CameraEnabled) yield return MoveCameraTo(cut.CameraPosition, cut.CameraScale, cut.MoveDuration);
        }

        private IEnumerator MoveCameraTo(Vector2 position, float targetScale, float duration)
        {
            if (_world == null) yield break;
            _cameraGoalPosition = position;
            _cameraGoalScale = Mathf.Clamp(targetScale, 0.25f, Mathf.Max(1f, _maxCameraScale));
            _cameraMoving = true;
            Vector2 from = _world.anchoredPosition;
            float scale = _world.localScale.x;
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
                _world.anchoredPosition = Vector2.Lerp(from, position, t);
                _world.localScale = Vector3.one * Mathf.Lerp(scale, _cameraGoalScale, t);
                yield return null;
            }
            SnapCameraGoal();
        }

        private IEnumerator Pop(RectTransform target)
        {
            _popped = target;
            _popOriginal = target.localScale;
            // Hard size steps rather than a smooth UI animation.
            for (int i = 0; i < 6; i++)
            {
                target.localScale = _popOriginal * (i % 2 == 0 ? 1.12f : 1f);
                yield return WaitUnscaled(0.12f);
            }
            ResetPop();
        }

        private void ResetPop()
        {
            if (_popped != null) _popped.localScale = _popOriginal;
            _popped = null;
        }

        private void OnDestroy()
        {
            if (_btnSkip != null) _btnSkip.onClick.RemoveListener(Finish);
        }

        private static IEnumerator WaitUnscaled(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                yield return null;
            }
        }
    }
}
