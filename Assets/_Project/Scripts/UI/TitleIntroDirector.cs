using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 타이틀 연출 (카메라 대신 레이어 이미지를 이동/확대).
    /// 1) 어둠 → 모니터 깜빡이며 켜짐
    /// 2) 기획서 클로즈업: 커피잔 흔들림, 커피 방울이 단어 위에 떨어져 빈칸이 됨
    /// 3) 기획서 → 모니터 쪽으로 이동 (레이어별 속도가 달라 깊이감)
    /// 4) 모니터로 줌인, 모니터 화면(타이틀 화면)이 전체 화면으로 커짐
    /// 5) 메뉴 선택 가능
    /// 모든 시간은 Time.timeScale 과 무관한 실제 시간. 아무 키/클릭으로 5) 로 건너뛴다.
    /// 앱 실행 후 처음 타이틀에 들어올 때만 재생하고, 이후에는 5) 부터 시작한다.
    /// </summary>
    public class TitleIntroDirector : MonoBehaviour
    {
        [Serializable]
        private class ParallaxLayer
        {
            [SerializeField] private RectTransform _rect;
            [Tooltip("이동 속도 배율. 1 = 책상과 같이 움직임, 작을수록 멀리 있는 것처럼 느리게 움직임")]
            [SerializeField, Range(0f, 1.5f)] private float _panFactor = 1f;
            [Tooltip("확대 배율. 1 = 책상과 같이 커짐")]
            [SerializeField, Range(0f, 1.5f)] private float _zoomFactor = 1f;
            [Tooltip("가장자리가 보이지 않도록 기본으로 조금 크게 (1 = 그대로)")]
            [SerializeField, Min(1f)] private float _overscale = 1f;

            public ParallaxLayer() { }

            public ParallaxLayer(RectTransform rect, float pan, float zoom, float overscale)
            {
                _rect = rect;
                _panFactor = pan;
                _zoomFactor = zoom;
                _overscale = overscale;
            }

            public void Apply(Vector2 focus, float zoom)
            {
                if (_rect == null)
                {
                    return;
                }
                Vector2 size = _rect.rect.size;
                float scale = _overscale * (1f + (zoom - 1f) * _zoomFactor);
                Vector2 offset = -(focus - new Vector2(0.5f, 0.5f));
                offset.x *= size.x * zoom * _panFactor;
                offset.y *= size.y * zoom * _panFactor;
                _rect.localScale = new Vector3(scale, scale, 1f);
                _rect.anchoredPosition = offset;
            }
        }

        [Serializable]
        private struct Shot
        {
            [Tooltip("화면 가운데에 올 지점. 큰 일러스트(방/책상) 기준 0~1, 왼쪽 아래가 (0,0)")]
            public Vector2 Focus;
            [Tooltip("확대 배율. 1 = 큰 일러스트 전체가 화면에 맞음")]
            [Min(1f)] public float Zoom;

            public Shot(Vector2 focus, float zoom)
            {
                Focus = focus;
                Zoom = zoom;
            }
        }

        // 앱을 켠 뒤 한 번 재생했는지. Domain Reload 를 꺼도 Play 할 때마다 초기화된다.
        private static bool s_playedThisLaunch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_playedThisLaunch = false;
        }

        [Header("재생 조건")]
        [SerializeField] private bool _playIntro = true;
        [Tooltip("에디터에서 테스트할 때 켜면 Title 에 들어올 때마다 재생")]
        [SerializeField] private bool _alwaysPlayInEditor;

        [Header("연결")]
        [SerializeField] private TitleScreenView _screen;
        [Tooltip("타이틀 화면을 담는 창. 모니터 화면 영역 → 전체 화면으로 커진다 (RectMask2D)")]
        [SerializeField] private RectTransform _screenViewport;
        [Tooltip("1920x1080 타이틀 화면 내용. 창 크기에 맞게 비율을 유지하며 축소된다")]
        [SerializeField] private RectTransform _screenContent;
        [SerializeField] private CanvasGroup _screenGroup;
        [Tooltip("책상 레이어 안의 모니터 화면 영역 (마젠타 부분)")]
        [SerializeField] private RectTransform _monitorScreenRect;
        [Tooltip("방·책상·빛 레이어의 부모")]
        [SerializeField] private CanvasGroup _worldGroup;
        [SerializeField] private ParallaxLayer[] _layers = new ParallaxLayer[0];
        [SerializeField] private Image _monitorGlow;
        [Tooltip("맨 위 검은 막. 처음 어둠과 방이 드러나는 정도")]
        [SerializeField] private Image _darkOverlay;

        [Header("기획서 클로즈업")]
        [SerializeField] private CanvasGroup _closeupGroup;
        [SerializeField] private TitleDocSentence _docSentence;
        [SerializeField] private RectTransform _mug;
        [Tooltip("커피 방울이 출발하는 위치 (커피잔 기준 로컬 좌표)")]
        [SerializeField] private Vector2 _dropSpawnOffset = new Vector2(-96f, 96f);
        [SerializeField] private Image[] _drops = new Image[0];
        [SerializeField] private Image[] _dropMarks = new Image[0];
        [Tooltip("자국 위치 = 단어 가운데 + 이 값. 빈칸 글자를 가리지 않도록 조금 위로")]
        [SerializeField] private Vector2 _markOffset = new Vector2(0f, 56f);

        [Header("이미지 (비우면 각 Image 에 넣어 둔 것을 그대로 씀)")]
        [SerializeField] private Sprite _roomSprite;
        [SerializeField] private Sprite _deskSprite;
        [SerializeField] private Sprite _glowSprite;
        [SerializeField] private Sprite _closeupSprite;
        [SerializeField] private Sprite _mugSprite;
        [SerializeField] private Sprite[] _dropSprites = new Sprite[0];
        [SerializeField] private Sprite[] _dropMarkSprites = new Sprite[0];
        [SerializeField] private Image _roomImage;
        [SerializeField] private Image _deskImage;
        [SerializeField] private Image _closeupImage;
        [SerializeField] private Image _mugImage;

        [Header("1) 모니터 켜짐 (초)")]
        [SerializeField, Min(0f)] private float _darkHold = 0.3f;
        [SerializeField, Min(0.01f)] private float _monitorOnDuration = 1.2f;
        [Tooltip("가로: 0~1 진행, 세로: 모니터 밝기. 들쭉날쭉하면 깜빡임")]
        [SerializeField] private AnimationCurve _monitorFlicker = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.12f, 0.9f), new Keyframe(0.2f, 0.05f),
            new Keyframe(0.35f, 0.8f), new Keyframe(0.45f, 0.25f), new Keyframe(0.65f, 1f), new Keyframe(1f, 1f));
        [Tooltip("모니터가 다 켜졌을 때 남는 검은 막 알파 (0 = 다 드러남)")]
        [SerializeField, Range(0f, 1f)] private float _darkOverlayEnd = 0.15f;
        [Tooltip("모니터 빛(부드러운 원형 그라데이션)의 최대 세기")]
        [SerializeField, Range(0f, 1f)] private float _glowMaxAlpha = 0.3f;
        [Header("모니터 화면 빛 (화면 가운데에서 퍼짐)")]
        [Tooltip("화면 위에 겹쳐 그리는 부드러운 빛. 비우면 효과 없음")]
        [SerializeField] private Image _screenBloom;
        [Tooltip("빛 가운데 진하기 (0 = 끔)")]
        [SerializeField, Range(0f, 1f)] private float _bloomStrength = 0.22f;
        [Tooltip("빛 크기 = 화면 크기 x 이 값. 1 보다 크면 베젤 밖 방까지 번짐")]
        [SerializeField, Range(1f, 4f)] private float _bloomSize = 2.2f;
        [SerializeField] private Color _bloomColor = new Color32(0xA9, 0xC6, 0xF5, 0xFF);
        [Tooltip("모니터 줌인 구간(0~1) 중 빛이 줄어들기 시작하는 지점")]
        [SerializeField, Range(0f, 1f)] private float _bloomFadeStart = 0.1f;
        [Tooltip("모니터 줌인 구간(0~1) 중 빛이 다 사라지는 지점. 최종 타이틀 화면에는 빛이 없음")]
        [SerializeField, Range(0f, 1f)] private float _bloomFadeEnd = 0.75f;

        [Tooltip("모니터 주변을 뺀 방을 어둡게 하는 막 (가운데가 뚫린 검은 이미지)")]
        [SerializeField] private Image _roomDim;
        [Tooltip("깜빡임 횟수. 0 이면 아래 Monitor Flicker 커브를 그대로 씀")]
        [SerializeField, Min(0)] private int _flickerCount = 3;
        [Tooltip("깜빡일 때 모니터가 꺼지는 정도 (0 = 완전히 꺼짐)")]
        [SerializeField, Range(0f, 1f)] private float _flickerLow = 0.15f;
        [Tooltip("모니터가 꺼질 때 방이 함께 어두워지는 정도 (모니터 주변 제외)")]
        [SerializeField, Range(0f, 1f)] private float _flickerRoomDim = 0.45f;
        [SerializeField] private Shot _wideShot = new Shot(new Vector2(0.5f, 0.5f), 1f);

        [Header("2) 기획서 클로즈업 (초)")]
        [SerializeField, Min(0f)] private float _closeupFadeIn = 0.25f;
        [SerializeField, Min(0f)] private float _mugWobbleDelay = 0.2f;
        [SerializeField, Min(0.01f)] private float _mugWobbleDuration = 0.6f;
        [SerializeField, Range(0f, 30f)] private float _mugWobbleAngle = 7f;
        [SerializeField, Min(0.1f)] private float _mugWobbleFrequency = 5f;
        [Tooltip("흔들림 시작 후 첫 방울이 떨어지기까지")]
        [SerializeField, Min(0f)] private float _firstDropDelay = 0.25f;
        [SerializeField, Min(0.01f)] private float _dropInterval = 0.3f;
        [SerializeField, Min(0.01f)] private float _dropFallTime = 0.25f;
        [Tooltip("마지막 방울 뒤 머무는 시간")]
        [SerializeField, Min(0f)] private float _closeupHold = 2f;

        [Header("3) 기획서 → 모니터 이동 (초)")]
        [SerializeField, Min(0f)] private float _closeupFadeOut = 0.25f;
        [SerializeField, Min(0.01f)] private float _panDuration = 1.5f;
        [SerializeField] private Shot _panStart = new Shot(new Vector2(0.2f, 0.32f), 2.2f);
        [SerializeField] private Shot _panEnd = new Shot(new Vector2(0.686f, 0.64f), 2.2f);
        [Tooltip("켜면 이동 끝 지점을 모니터 화면 가운데로 자동 계산 (_panEnd.Focus 무시)")]
        [SerializeField] private bool _panEndAtMonitor = true;
        [SerializeField] private AnimationCurve _panEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("4) 모니터 줌인 (초)")]
        [SerializeField, Min(0.01f)] private float _zoomDuration = 1f;
        [Tooltip("0 = 모니터 화면이 화면에 꽉 차는 배율을 자동 계산")]
        [SerializeField, Min(0f)] private float _zoomEndScale;
        [Tooltip("자동 계산일 때 모니터 화면이 화면을 채우는 비율")]
        [SerializeField, Range(0.5f, 1f)] private float _zoomFillRatio = 0.95f;
        [Tooltip("줌 마지막 몇 % 동안 타이틀 화면이 전체 화면으로 커지는지")]
        [SerializeField, Range(0.05f, 1f)] private float _screenExpandPortion = 0.3f;
        [SerializeField] private AnimationCurve _zoomEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private Coroutine _routine;
        private bool _playing;
        private Vector2 _stageSize = new Vector2(1920f, 1080f);
        private float _viewportToFull; // 0 = 모니터 영역, 1 = 전체 화면

        private void Awake()
        {
            ApplySprites();
            EnsureBloomSprite();
        }

        private void Start()
        {
            bool play = _playIntro && (!s_playedThisLaunch || (_alwaysPlayInEditor && Application.isEditor));
            s_playedThisLaunch = true;

            if (play)
            {
                _routine = StartCoroutine(PlayRoutine());
            }
            else
            {
                ShowFinal();
            }
        }

        private void Update()
        {
            if (_playing && AnyInputPressed())
            {
                Skip();
            }
        }

        private void LateUpdate()
        {
            // 레이어가 움직인 뒤에 타이틀 화면 창을 모니터 위치에 맞춘다
            if (_playing)
            {
                UpdateViewport();
                FollowBloom();
            }
        }

        public void Skip()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            ShowFinal();
        }

        // ---------------- 연출 ----------------

        private IEnumerator PlayRoutine()
        {
            _playing = true;
            RectTransform parent = _screenViewport != null ? _screenViewport.parent as RectTransform : null;
            if (parent != null && parent.rect.width > 0f)
            {
                _stageSize = parent.rect.size;
            }

            if (_screen != null)
            {
                _screen.SetMenuEnabled(false);
            }
            _viewportToFull = 0f;
            SetActive(_worldGroup, true);
            SetAlpha(_worldGroup, 1f);
            SetActive(_closeupGroup, false);
            SetDark(1f);
            SetGlow(0f);
            SetBloom(0f);
            SetRoomDim(0f);
            SetAlpha(_screenGroup, 0f);
            ApplyShot(_wideShot.Focus, _wideShot.Zoom);
            ResetCloseup();

            // 1) 어둠 → 모니터 켜짐
            yield return Wait(_darkHold);
            if (_screen != null)
            {
                _screen.PlaySfx(TitleSfx.MonitorOn);
            }
            yield return Tween(_monitorOnDuration, k =>
            {
                float b = Flicker(k);
                SetAlpha(_screenGroup, b);
                SetGlow(b * _glowMaxAlpha);
                SetBloom(b);
                // 전체 어둠은 첫 번째 켜짐 동안 한 번만 걷히고, 그 뒤 깜빡임은 모니터 주변을 뺀 방만 어둡게 한다
                SetDark(Mathf.Lerp(1f, _darkOverlayEnd, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / FirstFlashEnd()))));
                SetRoomDim(_flickerRoomDim * (1f - b));
            });
            SetAlpha(_screenGroup, 1f);
            SetGlow(_glowMaxAlpha);
            SetBloom(1f);
            SetDark(_darkOverlayEnd);
            SetRoomDim(0f);

            // 2) 기획서 클로즈업
            SetActive(_closeupGroup, true);
            yield return Tween(_closeupFadeIn, k => SetAlpha(_closeupGroup, k));
            yield return Wait(_mugWobbleDelay);
            yield return CoffeeDropRoutine();
            yield return Wait(_closeupHold);

            // 3) 기획서 → 모니터 (클로즈업이 사라지면 같은 책상의 기획서 위치에서 이동 시작)
            ApplyShot(_panStart.Focus, _panStart.Zoom);
            yield return Tween(_closeupFadeOut, k => SetAlpha(_closeupGroup, 1f - k));
            SetActive(_closeupGroup, false);

            Vector2 panEndFocus = _panEndAtMonitor ? GetMonitorFocus() : _panEnd.Focus;
            if (_screen != null)
            {
                _screen.PlaySfx(TitleSfx.Keyboard);
            }
            yield return Tween(_panDuration, k =>
            {
                float e = _panEase.Evaluate(k);
                ApplyShot(Vector2.Lerp(_panStart.Focus, panEndFocus, e), Mathf.Lerp(_panStart.Zoom, _panEnd.Zoom, e));
            });

            // 4) 모니터 줌인 → 타이틀 화면이 전체 화면으로
            float zoomEnd = _zoomEndScale > 0f ? _zoomEndScale : GetAutoZoom();
            float expandStart = 1f - _screenExpandPortion;
            yield return Tween(_zoomDuration, k =>
            {
                float e = _zoomEase.Evaluate(k);
                Vector2 focus = GetMonitorFocus();
                ApplyShot(Vector2.Lerp(panEndFocus, focus, e), Mathf.Lerp(_panEnd.Zoom, zoomEnd, e));
                float x = Mathf.Clamp01((k - expandStart) / Mathf.Max(0.0001f, _screenExpandPortion));
                _viewportToFull = x * x * (3f - 2f * x);
                SetAlpha(_worldGroup, 1f - _viewportToFull);
                float f = Mathf.Clamp01((k - _bloomFadeStart) / Mathf.Max(0.0001f, _bloomFadeEnd - _bloomFadeStart));
                SetBloom(1f - f * f * (3f - 2f * f));
                SetDark(Mathf.Lerp(_darkOverlayEnd, 0f, _viewportToFull));
            });

            _routine = null;
            ShowFinal();
        }

        private IEnumerator CoffeeDropRoutine()
        {
            int count = _docSentence != null ? Mathf.Min(_drops.Length, _docSentence.WordCount) : 0;
            float total = Mathf.Max(_mugWobbleDuration, _firstDropDelay + _dropInterval * Mathf.Max(0, count - 1) + _dropFallTime);
            float elapsed = 0f;
            int launched = 0;
            var landed = new bool[count];
            var startPos = new Vector3[count];
            var launchTime = new float[count];

            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;

                // 커피잔 흔들림 (점점 잦아듦)
                if (_mug != null)
                {
                    float w = Mathf.Clamp01(elapsed / _mugWobbleDuration);
                    float angle = elapsed < _mugWobbleDuration
                        ? Mathf.Sin(elapsed * _mugWobbleFrequency * Mathf.PI * 2f) * _mugWobbleAngle * (1f - w)
                        : 0f;
                    _mug.localRotation = Quaternion.Euler(0f, 0f, angle);
                }

                // 방울 출발
                while (launched < count && elapsed >= _firstDropDelay + _dropInterval * launched)
                {
                    startPos[launched] = _mug != null ? _mug.TransformPoint(_dropSpawnOffset) : transform.position;
                    launchTime[launched] = _firstDropDelay + _dropInterval * launched;
                    if (_drops[launched] != null)
                    {
                        _drops[launched].gameObject.SetActive(true);
                        _drops[launched].rectTransform.position = startPos[launched];
                    }
                    launched++;
                }

                // 방울 낙하 (가속) → 닿으면 자국 + 빈칸
                for (int i = 0; i < launched; i++)
                {
                    if (landed[i])
                    {
                        continue;
                    }
                    float t = Mathf.Clamp01((elapsed - launchTime[i]) / _dropFallTime);
                    Vector3 target = _docSentence.GetWordWorldCenter(i);
                    if (_drops[i] != null)
                    {
                        RectTransform r = _drops[i].rectTransform;
                        Vector3 p = Vector3.Lerp(startPos[i], target, t);
                        // 위로 살짝 튀었다가 떨어지는 포물선
                        p.y = Mathf.Lerp(startPos[i].y, target.y, t * t) + Mathf.Sin(t * Mathf.PI) * 40f * r.lossyScale.y;
                        r.position = p;
                        float s = Mathf.Lerp(0.7f, 1f, t);
                        r.localScale = new Vector3(s, s, 1f);
                    }
                    if (t >= 1f)
                    {
                        landed[i] = true;
                        Land(i, target);
                    }
                }
                yield return null;
            }

            for (int i = 0; i < count; i++)
            {
                if (!landed[i])
                {
                    Land(i, _docSentence.GetWordWorldCenter(i));
                }
            }
            if (_mug != null)
            {
                _mug.localRotation = Quaternion.identity;
            }
        }

        private void Land(int index, Vector3 worldTarget)
        {
            if (index < _drops.Length && _drops[index] != null)
            {
                _drops[index].gameObject.SetActive(false);
            }
            if (index < _dropMarks.Length && _dropMarks[index] != null)
            {
                RectTransform mark = _dropMarks[index].rectTransform;
                mark.gameObject.SetActive(true);
                mark.position = worldTarget;
                mark.anchoredPosition += _markOffset;
            }
            if (_docSentence != null)
            {
                _docSentence.EraseWord(index);
            }
            if (_screen != null)
            {
                _screen.PlaySfx(TitleSfx.CoffeeDrop);
            }
        }

        // ---------------- 최종 상태 ----------------

        private void ShowFinal()
        {
            _playing = false;
            _viewportToFull = 1f;
            SetActive(_worldGroup, false);
            SetActive(_closeupGroup, false);
            if (_darkOverlay != null)
            {
                _darkOverlay.gameObject.SetActive(false);
            }
            SetAlpha(_screenGroup, 1f);
            SetGlow(_glowMaxAlpha);
            SetRoomDim(0f);
            SetBloom(0f);
            UpdateViewport();
            if (_screen != null)
            {
                _screen.SetMenuEnabled(true);
            }
        }

        private void ResetCloseup()
        {
            if (_docSentence != null)
            {
                _docSentence.ResetAll();
            }
            foreach (Image d in _drops)
            {
                if (d != null) d.gameObject.SetActive(false);
            }
            foreach (Image m in _dropMarks)
            {
                if (m != null) m.gameObject.SetActive(false);
            }
            if (_mug != null)
            {
                _mug.localRotation = Quaternion.identity;
            }
        }

        // ---------------- 계산 ----------------

        private void ApplyShot(Vector2 focus, float zoom)
        {
            foreach (ParallaxLayer layer in _layers)
            {
                layer?.Apply(focus, zoom);
            }
        }

        /// <summary>모니터 화면 가운데의 큰 일러스트 기준 0~1 좌표.</summary>
        private Vector2 GetMonitorFocus()
        {
            if (_monitorScreenRect == null)
            {
                return _panEnd.Focus;
            }
            Vector2 aMin = _monitorScreenRect.anchorMin;
            Vector2 aMax = _monitorScreenRect.anchorMax;
            return (aMin + aMax) * 0.5f;
        }

        /// <summary>책상 레이어 배율 1 기준 모니터 크기로, 모니터가 화면에 꽉 차는 배율.</summary>
        private float GetAutoZoom()
        {
            if (_monitorScreenRect == null)
            {
                return 3f;
            }
            Vector2 size = _monitorScreenRect.anchorMax - _monitorScreenRect.anchorMin;
            Vector2 layerSize = _monitorScreenRect.parent is RectTransform layer ? layer.rect.size : _stageSize;
            float zx = _stageSize.x / Mathf.Max(1f, size.x * layerSize.x);
            float zy = _stageSize.y / Mathf.Max(1f, size.y * layerSize.y);
            return Mathf.Max(1f, Mathf.Min(zx, zy) * _zoomFillRatio);
        }

        /// <summary>타이틀 화면 창을 (모니터 영역 ↔ 전체 화면) 사이에 맞추고, 내용은 비율을 유지하며 맞춘다.</summary>
        private void UpdateViewport()
        {
            if (_screenViewport == null)
            {
                return;
            }
            RectTransform parent = _screenViewport.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Rect full = parent.rect;
            Rect monitor = full;
            if (_monitorScreenRect != null && _worldGroup != null && _worldGroup.gameObject.activeInHierarchy)
            {
                var corners = new Vector3[4];
                _monitorScreenRect.GetWorldCorners(corners);
                Vector3 bl = parent.InverseTransformPoint(corners[0]);
                Vector3 tr = parent.InverseTransformPoint(corners[2]);
                monitor = Rect.MinMaxRect(bl.x, bl.y, tr.x, tr.y);
            }

            float k = _viewportToFull;
            Vector2 center = Vector2.Lerp(monitor.center, full.center, k);
            Vector2 size = Vector2.Lerp(monitor.size, full.size, k);

            _screenViewport.anchorMin = _screenViewport.anchorMax = new Vector2(0.5f, 0.5f);
            _screenViewport.pivot = new Vector2(0.5f, 0.5f);
            _screenViewport.anchoredPosition = center - full.center;
            _screenViewport.sizeDelta = size;

            if (_screenContent != null)
            {
                Vector2 contentSize = _screenContent.rect.size;
                float s = Mathf.Min(size.x / Mathf.Max(1f, contentSize.x), size.y / Mathf.Max(1f, contentSize.y));
                _screenContent.localScale = new Vector3(s, s, 1f);
                _screenContent.anchoredPosition = Vector2.zero;
            }
        }

        private void ApplySprites()
        {
            SetSprite(_roomImage, _roomSprite);
            SetSprite(_deskImage, _deskSprite);
            SetSprite(_monitorGlow, _glowSprite);
            SetSprite(_closeupImage, _closeupSprite);
            SetSprite(_mugImage, _mugSprite);
            for (int i = 0; i < _drops.Length && i < _dropSprites.Length; i++)
            {
                SetSprite(_drops[i], _dropSprites[i]);
            }
            for (int i = 0; i < _dropMarks.Length && i < _dropMarkSprites.Length; i++)
            {
                SetSprite(_dropMarks[i], _dropMarkSprites[i]);
            }
        }

        // ---------------- 공용 ----------------

        private static bool AnyInputPressed()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)) return true;
            Gamepad pad = Gamepad.current;
            if (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) return true;
            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;
            return false;
        }

        private static IEnumerator Wait(float seconds)
        {
            if (seconds > 0f)
            {
                yield return new WaitForSecondsRealtime(seconds);
            }
        }

        private static IEnumerator Tween(float duration, Action<float> step)
        {
            if (duration <= 0f)
            {
                step(1f);
                yield break;
            }
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                step(Mathf.Clamp01(t / duration));
                yield return null;
            }
            step(1f);
        }

        private void SetDark(float alpha)
        {
            if (_darkOverlay == null)
            {
                return;
            }
            _darkOverlay.gameObject.SetActive(alpha > 0.001f);
            Color c = _darkOverlay.color;
            c.a = alpha;
            _darkOverlay.color = c;
        }

        /// <summary>모니터 밝기 0~1. 깜빡임 횟수가 0 이면 커브, 아니면 켜짐/꺼짐을 번갈아 만든 뒤 마지막 35% 는 계속 켜짐.</summary>
        private float Flicker(float k)
        {
            if (_flickerCount <= 0)
            {
                return Mathf.Clamp01(_monitorFlicker.Evaluate(k));
            }
            const float flickerPart = 0.65f;
            if (k >= flickerPart)
            {
                return 1f;
            }
            float phase = k / flickerPart * _flickerCount;
            int i = Mathf.Min(_flickerCount - 1, Mathf.FloorToInt(phase));
            float frac = phase - i;
            float on = Mathf.Lerp(0.7f, 1f, (i + 1f) / _flickerCount);
            // 한 번의 깜빡임 = 짧게 켜짐(60%) → 꺼짐(40%). 경계는 아주 짧게 부드럽게
            float edgeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(frac / 0.08f));
            float edgeOut = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((frac - 0.6f) / 0.06f));
            float low = i == 0 ? 0f : _flickerLow;
            if (i == _flickerCount - 1)
            {
                return Mathf.Lerp(low, 1f, edgeIn); // 마지막은 꺼지지 않고 그대로 켜짐
            }
            float b = Mathf.Lerp(low, on, edgeIn);
            return Mathf.Lerp(b, _flickerLow, edgeOut);
        }

        private float FirstFlashEnd()
        {
            return _flickerCount <= 0 ? 0.12f : Mathf.Max(0.01f, 0.65f / _flickerCount * 0.6f);
        }

        private static Sprite s_bloomSprite;

        /// <summary>가운데 1 → 가장자리 0 으로 부드럽게 줄어드는 원형 빛 (코드로 만듦, Bilinear, 계단 없음).</summary>
        private void EnsureBloomSprite()
        {
            if (_screenBloom == null)
            {
                return;
            }
            _screenBloom.raycastTarget = false;
            if (_screenBloom.sprite != null)
            {
                return;
            }
            if (s_bloomSprite == null)
            {
                const int size = 256;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "TitleScreenBloom",
                    hideFlags = HideFlags.DontSave,
                };
                float half = (size - 1) * 0.5f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - half) / half;
                        float dy = (y - half) / half;
                        float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                        // 가운데는 넓고 평평하게, 끝은 0 으로 매끄럽게
                        float a = Mathf.Exp(-r * r * 3.2f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, r)));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                s_bloomSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                s_bloomSprite.hideFlags = HideFlags.DontSave;
            }
            _screenBloom.sprite = s_bloomSprite;
        }

        /// <summary>화면 빛 세기 (0~1, Bloom Strength 를 곱함). 위치와 크기는 화면 창을 따라간다.</summary>
        private void SetBloom(float k)
        {
            if (_screenBloom == null)
            {
                return;
            }
            float a = Mathf.Clamp01(k) * _bloomStrength;
            Color c = _bloomColor;
            c.a = a;
            _screenBloom.color = c;
            _screenBloom.enabled = a > 0.001f;
            FollowBloom();
        }

        /// <summary>빛을 화면 창 가운데에 맞춘다. 화면 창이 움직인 뒤(LateUpdate)에도 부른다.</summary>
        private void FollowBloom()
        {
            if (_screenBloom != null && _screenBloom.enabled && _screenViewport != null)
            {
                RectTransform rt = _screenBloom.rectTransform;
                rt.anchorMin = rt.anchorMax = _screenViewport.anchorMin;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = _screenViewport.anchoredPosition;
                rt.sizeDelta = _screenViewport.sizeDelta * _bloomSize;
            }
        }

        private void SetRoomDim(float alpha)
        {
            if (_roomDim == null)
            {
                return;
            }
            Color c = _roomDim.color;
            c.a = alpha;
            _roomDim.color = c;
            _roomDim.enabled = alpha > 0.001f;
        }

        private void SetGlow(float alpha)
        {
            if (_monitorGlow == null)
            {
                return;
            }
            Color c = _monitorGlow.color;
            c.a = alpha;
            _monitorGlow.color = c;
        }

        private static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (group != null)
            {
                group.alpha = alpha;
            }
        }

        private static void SetActive(CanvasGroup group, bool active)
        {
            if (group != null)
            {
                group.gameObject.SetActive(active);
            }
        }

        private static void SetSprite(Image image, Sprite sprite)
        {
            if (image != null && sprite != null)
            {
                image.sprite = sprite;
            }
        }
    }
}
