using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 코드 한 줄처럼 보이는 메뉴 줄.
    /// 이 오브젝트의 투명 Image 가 마우스를 받고, 클릭하면 실행 연출(커서 이동 → 깜빡임) 뒤에
    /// TitleUI 가 연결해 둔 버튼(_targetButton)의 onClick 을 대신 눌러 준다.
    /// 그래서 TitleUI 의 버튼 로직은 그대로 쓰인다.
    /// _targetButton.interactable 이 false 면(예: 기록 없는 SelectStage) 주석 처리된 회색 줄로 바뀌고 클릭되지 않는다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class TitleMenuLine : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("연결")]
        [SerializeField] private TitleScreenView _view;
        [Tooltip("TitleUI 에 할당된 버튼 (BTN_GameStart 등). 이 버튼의 onClick 을 실행한다")]
        [SerializeField] private Button _targetButton;
        [SerializeField] private TMP_Text _codeText;
        [SerializeField] private TMP_Text _lineNumberText;
        [Tooltip("마우스를 올렸을 때 연하게 켜지는 줄 배경")]
        [SerializeField] private Image _highlight;
        [Tooltip("실행 연출용 커서 (세로 막대 Image)")]
        [SerializeField] private RectTransform _caret;

        [Header("문구")]
        [SerializeField] private string _code = "StartGame();";
        [Tooltip("잠겼을 때 표시할 문구")]
        [SerializeField] private string _lockedCode = "// SelectStage();  // 잠김";
        [Tooltip("코드 앞 들여쓰기")]
        [SerializeField] private string _indent = "    ";

        [Header("색")]
        [SerializeField] private Color _normalColor = new Color32(0xD4, 0xD4, 0xD4, 0xFF);
        [SerializeField] private Color _hoverColor = new Color32(0xE8, 0x91, 0x3B, 0xFF);
        [SerializeField] private Color _lineNumberColor = new Color32(0x5C, 0x63, 0x70, 0xFF);
        [SerializeField] private Color _lockedColor = new Color32(0x5C, 0x63, 0x70, 0xFF);
        [SerializeField] private Color _highlightColor = new Color(1f, 1f, 1f, 0.06f);
        [Tooltip("실행 깜빡임 때 줄 배경 색")]
        [SerializeField] private Color _executeFlashColor = new Color(0.91f, 0.57f, 0.23f, 0.22f);

        [Header("실행 연출 (실제 시간, 초)")]
        [SerializeField, Min(0f)] private float _caretMoveDuration = 0.12f;
        [SerializeField, Min(0)] private int _blinkCount = 2;
        [SerializeField, Min(0.01f)] private float _blinkInterval = 0.07f;

        // 선택된 줄 앞에 붙는 표시. 씬에서 _code가 이 표시로 시작하는 줄(StartGame)이 처음 선택된 줄이 된다
        private const string Marker = "\u25B6 ";

        private static TitleMenuLine s_selected;
        private static event Action SelectionChanged;

        private bool _hovered;
        private bool _lastLocked;
        private bool _initialized;
        private Coroutine _running;

        private bool IsLocked => _targetButton == null || !_targetButton.interactable;
        private bool IsSelected => s_selected == this;

        private void Awake()
        {
            // 표시는 선택에 따라 붙였다 뗐다 하므로 문구에서는 빼 둔다
            bool startsSelected = StripMarker(ref _code);
            StripMarker(ref _lockedCode);
            if (startsSelected && s_selected == null)
            {
                s_selected = this;
            }
        }

        private void OnDestroy()
        {
            // 씬을 다시 열면 처음 선택된 줄(StartGame)부터 다시 시작하도록 비운다
            if (s_selected == this)
            {
                s_selected = null;
            }
        }

        private static bool StripMarker(ref string code)
        {
            if (code != null && code.StartsWith(Marker, StringComparison.Ordinal))
            {
                code = code.Substring(Marker.Length);
                return true;
            }
            return false;
        }

        private static void Select(TitleMenuLine line)
        {
            if (s_selected == line)
            {
                return;
            }
            s_selected = line;
            SelectionChanged?.Invoke();
        }

        private void OnSelectionChanged() => Refresh(force: true);

        private void OnEnable()
        {
            SelectionChanged += OnSelectionChanged;
            _hovered = false;
            if (_caret != null)
            {
                _caret.gameObject.SetActive(false);
            }
            Refresh(force: true);
        }

        private void OnDisable()
        {
            SelectionChanged -= OnSelectionChanged;
            if (_running != null)
            {
                StopCoroutine(_running);
                _running = null;
                if (_view != null)
                {
                    _view.IsExecuting = false;
                }
            }
        }

        private void Update()
        {
            // TitleUI 가 Start 에서 interactable 을 바꾸므로 바뀌었을 때만 다시 그린다
            Refresh(force: false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            Refresh(force: true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Refresh(force: true);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            if (IsLocked || _running != null)
            {
                return;
            }
            if (_view != null && (_view.IsExecuting || !_view.MenuEnabled))
            {
                return;
            }
            // 누른 줄 앞으로 ▶ 표시를 옮긴 뒤 실행 연출을 시작한다
            Select(this);
            _running = StartCoroutine(ExecuteRoutine());
        }

        private void Refresh(bool force)
        {
            bool locked = IsLocked;
            if (!force && _initialized && locked == _lastLocked)
            {
                return;
            }
            _initialized = true;
            _lastLocked = locked;

            bool hot = _hovered && !locked;
            if (_codeText != null)
            {
                string marker = IsSelected && !locked ? Marker : string.Empty;
                _codeText.text = _indent + marker + (locked ? _lockedCode : _code);
                _codeText.color = locked ? _lockedColor : (hot ? _hoverColor : _normalColor);
            }
            if (_lineNumberText != null)
            {
                _lineNumberText.color = hot ? _hoverColor : _lineNumberColor;
            }
            if (_highlight != null)
            {
                _highlight.color = _highlightColor;
                _highlight.enabled = hot;
            }
        }

        private IEnumerator ExecuteRoutine()
        {
            if (_view != null)
            {
                _view.IsExecuting = true;
                _view.PlaySfx(TitleSfx.Keyboard);
            }

            // 1) 커서가 줄 처음에서 끝으로 이동
            if (_caret != null && _codeText != null)
            {
                GetTextRange(out float startX, out float endX);
                _caret.gameObject.SetActive(true);
                float t = 0f;
                while (t < _caretMoveDuration)
                {
                    t += Time.unscaledDeltaTime;
                    float k = _caretMoveDuration <= 0f ? 1f : Mathf.Clamp01(t / _caretMoveDuration);
                    SetCaretX(Mathf.Lerp(startX, endX, 1f - (1f - k) * (1f - k)));
                    yield return null;
                }
                SetCaretX(endX);
            }

            // 2) 짧은 깜빡임 (줄 배경과 커서)
            if (_view != null)
            {
                _view.PlaySfx(TitleSfx.MenuExecute);
            }
            for (int i = 0; i < _blinkCount * 2; i++)
            {
                bool on = i % 2 == 0;
                if (_highlight != null)
                {
                    _highlight.enabled = true;
                    _highlight.color = on ? _executeFlashColor : _highlightColor;
                }
                if (_caret != null)
                {
                    _caret.gameObject.SetActive(!on);
                }
                yield return new WaitForSecondsRealtime(_blinkInterval);
            }

            if (_caret != null)
            {
                _caret.gameObject.SetActive(false);
            }
            _running = null;
            if (_view != null)
            {
                _view.IsExecuting = false;
            }
            Refresh(force: true);

            // 3) 기능 실행 (TitleUI 가 연결한 버튼 로직)
            if (_targetButton != null && _targetButton.interactable)
            {
                _targetButton.onClick.Invoke();
            }
        }

        /// <summary>코드 글자가 실제로 그려진 x 범위 (커서 부모 기준 로컬 좌표).</summary>
        private void GetTextRange(out float startX, out float endX)
        {
            _codeText.ForceMeshUpdate();
            TMP_TextInfo info = _codeText.textInfo;
            RectTransform textRect = _codeText.rectTransform;
            float localStart = textRect.rect.xMin;
            float localEnd = localStart;

            bool found = false;
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo ch = info.characterInfo[i];
                if (!ch.isVisible)
                {
                    continue;
                }
                if (!found)
                {
                    localStart = ch.bottomLeft.x;
                    found = true;
                }
                localEnd = ch.topRight.x;
            }

            Transform parent = _caret.parent;
            startX = parent.InverseTransformPoint(textRect.TransformPoint(new Vector3(localStart, 0f, 0f))).x;
            endX = parent.InverseTransformPoint(textRect.TransformPoint(new Vector3(localEnd, 0f, 0f))).x + 4f;
        }

        private void SetCaretX(float x)
        {
            Vector3 p = _caret.localPosition;
            p.x = x;
            _caret.localPosition = p;
        }
    }
}
