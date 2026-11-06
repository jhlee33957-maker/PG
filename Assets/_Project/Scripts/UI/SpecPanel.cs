using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CoffeeSpec.Rules;
using CoffeeSpec.Spec;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 기획서 화면. 스테이지 시작 시 열려서 하단의 단어를 끌어다 빈칸에 놓고, 빌드하면 닫히며 플레이가 시작된다.
    /// 문장 틀(SentenceTemplate)과 단어(WordId)는 규칙 시스템의 것을 읽기만 한다.
    ///
    /// 끌어다 놓기 규칙
    ///   - 단어 바의 단어를 빈칸에 놓으면 채워진다 (빈칸에 있던 단어는 단어 바로 돌아감)
    ///   - 이미 빈칸에 쓰인 단어는 단어 바에서 반투명하게 보이고 끌 수 없다
    ///   - 빈칸의 단어를 다른 빈칸에 놓으면 옮겨진다 (놓은 곳에 있던 단어는 단어 바로 돌아감)
    ///   - 빈칸의 단어를 빈칸이 아닌 곳에 놓으면 빈칸이 비고 단어는 단어 바로 돌아간다
    ///
    /// 바깥(규칙 시스템)과 주고받는 것 — docs/decisions/004-spec-panel-flow.md 참고
    ///   - Built 이벤트: 빌드 시 완성된 문장(Sentence) 목록 전달. 품사가 안 맞는 문장도 그대로 넘긴다
    ///   - RetryRequested 이벤트: 플레이 중 BTN_Retry 또는 R키
    ///   - Reopen(): 사망·리트 후 레벨을 초기화한 쪽에서 호출. 이전에 고른 값이 그대로 채워진 채 열린다
    ///
    /// 규칙 적용, 레벨 초기화, 사망 처리는 이 컴포넌트가 하지 않는다.
    /// </summary>
    public class SpecPanel : MonoBehaviour
    {
        private const string BlankLinkPrefix = "blank_";

        // 문장 틀의 빈칸 표기: {0}, {1} ...
        private static readonly Regex SlotPattern = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        [Header("데이터")]
        [SerializeField] private StageSpec _spec;

        [Header("기획서 화면")]
        [Tooltip("열고 닫히는 기획서 화면 전체. 이 컴포넌트가 붙은 오브젝트의 자식이어야 한다 (닫혀 있어도 R키를 받아야 하므로)")]
        [SerializeField] private GameObject _documentRoot;
        [Tooltip("기획서 문장이 표시될 텍스트")]
        [SerializeField] private TMP_Text _documentText;
        [SerializeField] private Button _btnBuild;

        [Header("하단 단어 바")]
        [Tooltip("하단 단어 영역. 기획서가 열려 있는 동안 항상 보인다")]
        [SerializeField] private GameObject _optionBar;
        [Tooltip("단어가 생성될 부모 (Horizontal Layout Group 권장)")]
        [SerializeField] private Transform _optionContainer;
        [SerializeField] private SpecOptionButton _optionButtonPrefab;

        [Header("플레이 중 화면")]
        [Tooltip("우측 상단 리트 버튼. 기획서가 닫혀 있을 때만 보인다")]
        [SerializeField] private Button _btnRetry;

        [Header("연출")]
        [Tooltip("타이핑 연출에서 글자 하나가 찍히는 간격(초). 0이면 한 번에 표시")]
        [SerializeField, Min(0f)] private float _typingInterval = 0.05f;
        [Tooltip("빈칸에 쓰인 단어의 투명도 (단어 바)")]
        [SerializeField, Range(0f, 1f)] private float _usedOptionAlpha = 0.4f;
        [Tooltip("비어 있는 빈칸에 표시할 글자")]
        [SerializeField] private string _emptyBlankText = "______";
        [SerializeField] private Color _emptyBlankColor = new Color(0.45f, 0.3f, 0.15f);
        [SerializeField] private Color _filledBlankColor = new Color(0.1f, 0.2f, 0.6f);
        [Tooltip("단어를 끌고 있을 때 놓을 수 있는 빈칸 위에 올라간 경우의 색")]
        [SerializeField] private Color _hoverBlankColor = new Color(0.85f, 0.4f, 0.1f);
        [Tooltip("빈칸을 다 채웠는데 품사가 맞지 않는 문장의 색. 이런 문장이 있으면 빌드할 수 없다")]
        [SerializeField] private Color _invalidSentenceColor = new Color(0.85f, 0.1f, 0.1f);

        [Header("시작")]
        [SerializeField] private bool _openOnStart = true;

        [Header("테스트용 (에디터에서만 동작)")]
        [Tooltip("켜면 리트 요청 시 바로 기획서를 다시 연다. 규칙 쪽이 아직 없을 때 흐름을 테스트하는 용도")]
        [SerializeField] private bool _debugReopenOnRetry;
        [Tooltip("켜면 Built, RetryRequested 이벤트 내용을 콘솔에 출력한다")]
        [SerializeField] private bool _debugLogEvents;

        /// <summary>빌드 버튼을 눌렀을 때. 기획서의 문장들을 위에서부터 순서대로 전달한다.</summary>
        public event Action<IReadOnlyList<Sentence>> Built;

        /// <summary>플레이 중 BTN_Retry를 누르거나 R키를 눌렀을 때.</summary>
        public event Action RetryRequested;

        public bool IsOpen { get; private set; }

        private List<SentenceTemplate> _templates;
        private int[] _blankOffsets;    // 문장마다 첫 빈칸의 전체 번호

        private int[] _blankToOption;   // 빈칸 → 들어 있는 단어(묶음에서의 번호). 비었으면 -1
        private int[] _optionToBlank;   // 단어(묶음에서의 번호) → 들어가 있는 빈칸. 안 쓰였으면 -1
        private int[] _typedLength;     // 빈칸마다 지금까지 타이핑된 글자 수
        private Coroutine[] _typingRoutines;

        private readonly List<SpecOptionButton> _optionButtons = new List<SpecOptionButton>();
        private readonly StringBuilder _textBuilder = new StringBuilder();

        // 끌기 상태
        private int _dragOption = -1;      // 끌고 있는 단어. 없으면 -1
        private int _dragFromBlank = -1;   // 빈칸에서 끌기 시작했으면 그 빈칸, 단어 바에서 시작했으면 -1
        private int _hoverBlank = -1;      // 끌고 있는 단어 아래의 빈칸
        private RectTransform _dragGhost;  // 마우스를 따라다니는 단어
        private TextMeshProUGUI _dragGhostText;

        private bool _initialized;

        private bool IsDragging => _dragOption >= 0;

        // ---------------- 생명주기 ----------------

        private void Start()
        {
            if (!Initialize())
            {
                return;
            }

            if (_openOnStart)
            {
                Open();
            }
            else
            {
                Close();
            }
        }

        private void Update()
        {
            // 기획서가 열려 있을 때는 무시 (플레이 중에만 리트)
            if (!IsOpen && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                RequestRetry();
            }
        }

        private void OnDestroy()
        {
            // 기획서가 열린 채로 씬이 바뀌어도 다음 씬이 멈춰 있지 않도록
            if (IsOpen)
            {
                Time.timeScale = 1f;
            }
        }

        // ---------------- 바깥에서 부르는 함수 ----------------

        /// <summary>
        /// 기획서를 다시 연다. 사망·리트 후 레벨을 초기화한 쪽에서 호출한다.
        /// 이전에 고른 값은 그대로 채워져 있다.
        /// </summary>
        public void Reopen()
        {
            if (!Initialize())
            {
                return;
            }
            Open();
        }

        // ---------------- 초기화 ----------------

        private bool Initialize()
        {
            if (_initialized)
            {
                return true;
            }

            if (!ValidateReferences())
            {
                return false;
            }
            _initialized = true;

            _templates = _spec.LoadTemplates();
            _blankOffsets = new int[_templates.Count];

            int blankCount = 0;
            for (int i = 0; i < _templates.Count; i++)
            {
                _blankOffsets[i] = blankCount;
                blankCount += _templates[i].Pattern.Count;
            }

            _blankToOption = CreateFilledArray(blankCount, -1);
            _optionToBlank = CreateFilledArray(_spec.WordPool.Count, -1);
            _typedLength = new int[blankCount];
            _typingRoutines = new Coroutine[blankCount];

            CreateOptionButtons();

            // 빈칸에서 끌기를 시작할 수 있도록 텍스트가 입력을 받게 한다
            _documentText.raycastTarget = true;
            SpecTextClickRelay relay = _documentText.GetComponent<SpecTextClickRelay>();
            if (relay == null)
            {
                relay = _documentText.gameObject.AddComponent<SpecTextClickRelay>();
            }
            relay.DragBegan += OnBlankDragBegan;
            relay.Dragged += OnDragMoved;
            relay.DragEnded += OnDragEnded;

            _btnBuild.onClick.AddListener(OnClickBuild);
            if (_btnRetry != null)
            {
                _btnRetry.onClick.AddListener(RequestRetry);
            }

            return true;
        }

        private bool ValidateReferences()
        {
            bool ok = true;
            ok &= CheckAssigned(_spec, "StageSpec");
            ok &= CheckAssigned(_documentRoot, "기획서 화면(Document Root)");
            ok &= CheckAssigned(_documentText, "기획서 텍스트(Document Text)");
            ok &= CheckAssigned(_btnBuild, "BTN_Build");
            ok &= CheckAssigned(_optionBar, "하단 단어 바(Option Bar)");
            ok &= CheckAssigned(_optionContainer, "단어 부모(Option Container)");
            ok &= CheckAssigned(_optionButtonPrefab, "단어 프리팹");

            if (_btnRetry == null)
            {
                Debug.LogWarning("[SpecPanel] BTN_Retry가 할당되지 않았습니다. R키로만 리트할 수 있습니다.", this);
            }

            if (_documentRoot != null && _documentRoot == gameObject)
            {
                Debug.LogError("[SpecPanel] Document Root는 SpecPanel이 붙은 오브젝트의 '자식'이어야 합니다. 같은 오브젝트면 기획서가 닫혔을 때 R키를 받을 수 없습니다.", this);
                ok = false;
            }

            return ok;
        }

        private bool CheckAssigned(UnityEngine.Object target, string label)
        {
            if (target != null)
            {
                return true;
            }
            Debug.LogError($"[SpecPanel] {label}이(가) 할당되지 않았습니다.", this);
            return false;
        }

        private void CreateOptionButtons()
        {
            IReadOnlyList<WordId> pool = _spec.WordPool;
            for (int i = 0; i < pool.Count; i++)
            {
                SpecOptionButton button = Instantiate(_optionButtonPrefab, _optionContainer);
                button.name = $"Word_{i}_{pool[i]}";
                button.Setup(i, WordTags.DisplayName(pool[i]));
                button.DragBegan += OnOptionDragBegan;
                button.Dragged += (_, eventData) => OnDragMoved(eventData);
                button.DragEnded += (_, eventData) => OnDragEnded(eventData);
                _optionButtons.Add(button);
            }
        }

        // ---------------- 열기 / 닫기 ----------------

        private void Open()
        {
            IsOpen = true;
            Time.timeScale = 0f;

            // 다시 열 때는 타이핑 연출 없이 이전 값을 그대로 보여 준다
            FinishAllTyping();
            CancelDrag();

            _documentRoot.SetActive(true);
            _optionBar.SetActive(true);
            SetRetryButtonVisible(false);

            RefreshAll();
        }

        private void Close()
        {
            IsOpen = false;
            Time.timeScale = 1f;

            FinishAllTyping();
            CancelDrag();

            _optionBar.SetActive(false);
            _documentRoot.SetActive(false);
            SetRetryButtonVisible(true);
        }

        private void SetRetryButtonVisible(bool visible)
        {
            if (_btnRetry != null)
            {
                _btnRetry.gameObject.SetActive(visible);
            }
        }

        // ---------------- 끌어다 놓기 ----------------

        private void OnOptionDragBegan(SpecOptionButton button, PointerEventData eventData)
        {
            if (!IsOpen)
            {
                return;
            }
            BeginDrag(button.Index, -1, eventData);
        }

        private void OnBlankDragBegan(PointerEventData eventData)
        {
            if (!IsOpen)
            {
                return;
            }

            // 단어가 들어 있는 빈칸에서 시작한 끌기만 받는다
            int blankIndex = FindBlankAt(eventData.pressPosition, eventData.pressEventCamera);
            if (blankIndex < 0 || _blankToOption[blankIndex] < 0)
            {
                eventData.pointerDrag = null;
                return;
            }
            BeginDrag(_blankToOption[blankIndex], blankIndex, eventData);
        }

        private void BeginDrag(int optionIndex, int fromBlank, PointerEventData eventData)
        {
            _dragOption = optionIndex;
            _dragFromBlank = fromBlank;
            _hoverBlank = -1;

            EnsureDragGhost();
            _dragGhostText.text = WordTags.DisplayName(_spec.WordPool[optionIndex]);
            _dragGhost.gameObject.SetActive(true);
            _dragGhost.SetAsLastSibling();
            MoveDragGhost(eventData);
        }

        private void OnDragMoved(PointerEventData eventData)
        {
            if (!IsDragging)
            {
                return;
            }

            MoveDragGhost(eventData);

            int hover = FindBlankAt(eventData.position, eventData.pressEventCamera);
            if (hover != _hoverBlank)
            {
                _hoverBlank = hover;
                RenderDocument();
            }
        }

        private void OnDragEnded(PointerEventData eventData)
        {
            if (!IsDragging)
            {
                return;
            }

            int option = _dragOption;
            int fromBlank = _dragFromBlank;
            int targetBlank = FindBlankAt(eventData.position, eventData.pressEventCamera);
            CancelDrag();

            if (!IsOpen)
            {
                return;
            }

            if (targetBlank >= 0)
            {
                // 같은 빈칸에 다시 놓으면 아무 일도 없다
                if (targetBlank != fromBlank)
                {
                    AssignOption(targetBlank, option);
                }
            }
            else if (fromBlank >= 0)
            {
                // 빈칸의 단어를 빈칸이 아닌 곳에 놓으면 비운다
                ClearBlank(fromBlank);
            }

            RefreshAll();
        }

        private void CancelDrag()
        {
            _dragOption = -1;
            _dragFromBlank = -1;
            _hoverBlank = -1;
            if (_dragGhost != null)
            {
                _dragGhost.gameObject.SetActive(false);
            }
        }

        /// <summary>마우스를 따라다닐 단어를 처음 한 번 만든다. 클릭을 가로채지 않도록 입력은 받지 않는다.</summary>
        private void EnsureDragGhost()
        {
            if (_dragGhost != null)
            {
                return;
            }

            var ghostObject = new GameObject("DragGhost", typeof(RectTransform));
            ghostObject.transform.SetParent(_documentText.canvas.rootCanvas.transform, false);

            _dragGhost = (RectTransform)ghostObject.transform;
            _dragGhost.sizeDelta = new Vector2(600f, 120f);

            _dragGhostText = ghostObject.AddComponent<TextMeshProUGUI>();
            _dragGhostText.font = _documentText.font;
            _dragGhostText.fontSize = _documentText.fontSize;
            _dragGhostText.color = _filledBlankColor;
            _dragGhostText.alignment = TextAlignmentOptions.Center;
            _dragGhostText.raycastTarget = false;

            ghostObject.SetActive(false);
        }

        private void MoveDragGhost(PointerEventData eventData)
        {
            var parent = (RectTransform)_dragGhost.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                _dragGhost.anchoredPosition = localPoint;
            }
        }

        private int FindBlankAt(Vector2 screenPosition, Camera eventCamera)
        {
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(_documentText, screenPosition, eventCamera);
            if (linkIndex < 0)
            {
                return -1;
            }

            string linkId = _documentText.textInfo.linkInfo[linkIndex].GetLinkID();
            if (linkId.StartsWith(BlankLinkPrefix, StringComparison.Ordinal)
                && int.TryParse(linkId.Substring(BlankLinkPrefix.Length), out int blankIndex))
            {
                return blankIndex;
            }
            return -1;
        }

        // ---------------- 빈칸 채우기 / 비우기 ----------------

        /// <summary>
        /// 빈칸에 단어를 넣는다.
        /// - 빈칸에 있던 원래 단어는 단어 바로 돌아간다
        /// - 다른 빈칸에서 끌어온 단어면 그 빈칸은 비워진다
        /// </summary>
        private void AssignOption(int blankIndex, int optionIndex)
        {
            if (_blankToOption[blankIndex] == optionIndex)
            {
                return;
            }

            int previousOption = _blankToOption[blankIndex];
            if (previousOption >= 0)
            {
                _optionToBlank[previousOption] = -1;
            }

            int otherBlank = _optionToBlank[optionIndex];
            if (otherBlank >= 0)
            {
                StopTyping(otherBlank);
                _blankToOption[otherBlank] = -1;
                _typedLength[otherBlank] = 0;
            }

            _blankToOption[blankIndex] = optionIndex;
            _optionToBlank[optionIndex] = blankIndex;
            StartTyping(blankIndex);
        }

        private void ClearBlank(int blankIndex)
        {
            int option = _blankToOption[blankIndex];
            if (option < 0)
            {
                return;
            }

            StopTyping(blankIndex);
            _blankToOption[blankIndex] = -1;
            _optionToBlank[option] = -1;
            _typedLength[blankIndex] = 0;
        }

        // ---------------- 빌드 / 리트 ----------------

        private void OnClickBuild()
        {
            if (!IsOpen || !CanBuild())
            {
                return;
            }

            List<Sentence> sentences = CollectSentences();
            Close();

            if (_debugLogEvents)
            {
                foreach (Sentence sentence in sentences)
                {
                    string validity = sentence.IsValid() ? "성립" : "품사 불일치";
                    Debug.Log($"[SpecPanel] Built: {sentence} \"{sentence.DisplayText}\" ({validity})", this);
                }
            }
            Built?.Invoke(sentences);
        }

        private void RequestRetry()
        {
            if (IsOpen)
            {
                return;
            }

            if (_debugLogEvents)
            {
                Debug.Log("[SpecPanel] RetryRequested", this);
            }
            RetryRequested?.Invoke();

            if (_debugReopenOnRetry && Application.isEditor)
            {
                Reopen();
            }
        }

        // ---------------- 타이핑 연출 ----------------

        private void StartTyping(int blankIndex)
        {
            StopTyping(blankIndex);
            _typedLength[blankIndex] = 0;

            if (_typingInterval <= 0f)
            {
                _typedLength[blankIndex] = GetWordText(blankIndex).Length;
                return;
            }
            _typingRoutines[blankIndex] = StartCoroutine(TypeBlank(blankIndex));
        }

        private IEnumerator TypeBlank(int blankIndex)
        {
            string text = GetWordText(blankIndex);
            while (_typedLength[blankIndex] < text.Length)
            {
                _typedLength[blankIndex]++;
                RenderDocument();

                // 기획서가 열려 있는 동안 timeScale이 0이므로 실제 시간으로 기다린다
                yield return new WaitForSecondsRealtime(_typingInterval);
            }
            _typingRoutines[blankIndex] = null;
        }

        private void StopTyping(int blankIndex)
        {
            if (_typingRoutines[blankIndex] != null)
            {
                StopCoroutine(_typingRoutines[blankIndex]);
                _typingRoutines[blankIndex] = null;
            }
        }

        private void FinishAllTyping()
        {
            for (int i = 0; i < _blankToOption.Length; i++)
            {
                StopTyping(i);
                _typedLength[i] = GetWordText(i).Length;
            }
        }

        // ---------------- 화면 갱신 ----------------

        private void RefreshAll()
        {
            RenderDocument();
            RefreshOptionButtons();
            _btnBuild.interactable = CanBuild();
        }

        private void RefreshOptionButtons()
        {
            for (int i = 0; i < _optionButtons.Count; i++)
            {
                _optionButtons[i].SetUsed(_optionToBlank[i] >= 0, _usedOptionAlpha);
            }
        }

        /// <summary>
        /// 기획서 문장을 그린다. 문장 틀의 {0}, {1} 자리에는 빈칸 링크를 넣어 놓을 위치를 찾을 수 있게 한다.
        /// </summary>
        private void RenderDocument()
        {
            _textBuilder.Clear();

            for (int s = 0; s < _templates.Count; s++)
            {
                SentenceTemplate template = _templates[s];
                string text = template.Text ?? string.Empty;
                int cursor = 0;

                // 품사가 맞지 않는 문장은 문장 전체를 붉게 보여준다. 단어를 빼면 원래 색으로 돌아온다
                bool invalid = IsSentenceInvalid(s);
                if (invalid)
                {
                    _textBuilder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(_invalidSentenceColor)).Append('>');
                }

                foreach (Match match in SlotPattern.Matches(text))
                {
                    _textBuilder.Append(text, cursor, match.Index - cursor);

                    int slot = int.Parse(match.Groups[1].Value);
                    if (slot < template.Pattern.Count)
                    {
                        AppendBlank(_blankOffsets[s] + slot, invalid);
                    }
                    else
                    {
                        // CSV의 Pattern 개수보다 큰 번호는 빈칸이 아니므로 그대로 둔다
                        _textBuilder.Append(match.Value);
                    }
                    cursor = match.Index + match.Length;
                }
                _textBuilder.Append(text, cursor, text.Length - cursor);
                if (invalid)
                {
                    _textBuilder.Append("</color>");
                }

                if (s < _templates.Count - 1)
                {
                    _textBuilder.Append('\n');
                }
            }

            _documentText.text = _textBuilder.ToString();
        }

        private void AppendBlank(int blankIndex, bool invalidSentence)
        {
            bool filled = _blankToOption[blankIndex] >= 0;
            string content = filled
                ? GetWordText(blankIndex).Substring(0, _typedLength[blankIndex])
                : _emptyBlankText;

            Color color = blankIndex == _hoverBlank
                ? _hoverBlankColor
                : invalidSentence ? _invalidSentenceColor
                : (filled ? _filledBlankColor : _emptyBlankColor);

            _textBuilder
                .Append("<link=\"").Append(BlankLinkPrefix).Append(blankIndex).Append("\">")
                .Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(color)).Append('>')
                .Append("<u><noparse>").Append(content).Append("</noparse></u>")
                .Append("</color></link>");
        }

        // ---------------- 공용 ----------------

        private bool AreAllBlanksFilled()
        {
            foreach (int option in _blankToOption)
            {
                if (option < 0)
                {
                    return false;
                }
            }
            return true;
        }

        // 모든 빈칸이 차 있고, 품사가 맞지 않는 문장이 하나도 없어야 빌드할 수 있다
        private bool CanBuild()
        {
            if (!AreAllBlanksFilled())
            {
                return false;
            }
            for (int s = 0; s < _templates.Count; s++)
            {
                if (IsSentenceInvalid(s))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>문장의 빈칸이 모두 찼는데 품사 순서가 맞지 않으면 true. 덜 채운 문장은 아직 판단하지 않는다.</summary>
        private bool IsSentenceInvalid(int sentenceIndex)
        {
            SentenceTemplate template = _templates[sentenceIndex];
            var words = new List<WordId>(template.Pattern.Count);
            for (int slot = 0; slot < template.Pattern.Count; slot++)
            {
                int option = _blankToOption[_blankOffsets[sentenceIndex] + slot];
                if (option < 0)
                {
                    return false;
                }
                words.Add(_spec.WordPool[option]);
            }
            return !new Sentence(template, words).IsValid();
        }

        /// <summary>빈칸을 채운 단어로 문장(Sentence)을 만든다. 품사 검사는 규칙 쪽이 한다.</summary>
        private List<Sentence> CollectSentences()
        {
            var sentences = new List<Sentence>(_templates.Count);
            for (int s = 0; s < _templates.Count; s++)
            {
                SentenceTemplate template = _templates[s];
                var words = new List<WordId>(template.Pattern.Count);
                for (int slot = 0; slot < template.Pattern.Count; slot++)
                {
                    words.Add(_spec.WordPool[_blankToOption[_blankOffsets[s] + slot]]);
                }
                sentences.Add(new Sentence(template, words));
            }
            return sentences;
        }

        private string GetWordText(int blankIndex)
        {
            int option = _blankToOption[blankIndex];
            if (option < 0)
            {
                return string.Empty;
            }
            return WordTags.DisplayName(_spec.WordPool[option]) ?? string.Empty;
        }

        private static int[] CreateFilledArray(int length, int value)
        {
            var array = new int[length];
            for (int i = 0; i < length; i++)
            {
                array[i] = value;
            }
            return array;
        }
    }
}
