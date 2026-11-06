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
    /// 기획서 화면 (코드 에디터 스타일). 스테이지 시작 시 열려서 하단의 단어를 끌어다 빈칸에 놓고,
    /// 빌드하면 닫히며 플레이가 시작된다. 문장 틀(SentenceTemplate)과 단어(WordId)는 규칙 시스템의 것을 읽기만 한다.
    ///
    /// 끌어다 놓기 규칙
    ///   - 단어 바의 단어를 빈칸에 놓으면 채워진다 (빈칸에 있던 단어는 단어 바로 돌아감)
    ///   - 이미 빈칸에 쓰인 단어는 단어 바에서 반투명하게 보이고 끌 수 없다
    ///   - 빈칸의 단어를 다른 빈칸에 놓으면 옮겨진다 (놓은 곳에 있던 단어는 단어 바로 돌아감)
    ///   - 빈칸의 단어를 빈칸이 아닌 곳에 놓으면 빈칸이 비고 단어는 단어 바로 돌아간다
    ///   - 모든 빈칸이 차 있고 품사가 맞지 않는 문장이 없어야 빌드할 수 있다
    ///
    /// 바깥(규칙 시스템)과 주고받는 것 — docs/decisions/004-spec-panel-flow.md 참고
    ///   - Built 이벤트: 빌드 시 완성된 문장(Sentence) 목록 전달
    ///   - RetryRequested 이벤트: 플레이 중 BTN_Retry 또는 R키
    ///   - Reopen(): 사망·리트 후 레벨을 초기화한 쪽에서 호출. 이전에 고른 값이 그대로 채워진 채 열린다
    ///
    /// 규칙 적용, 레벨 초기화, 사망 처리는 이 컴포넌트가 하지 않는다.
    /// </summary>
    public class SpecPanel : MonoBehaviour
    {
        private const string BlankLinkPrefix = "blank_";

        // 문장 틀의 빈칸 표기: {0}, {1} ...

        [Header("데이터")]
        [SerializeField] private StageSpec _spec;

        [Header("기획서 화면")]
        [Tooltip("열고 닫히는 기획서 화면 전체. 이 컴포넌트가 붙은 오브젝트의 자식이어야 한다 (닫혀 있어도 R키를 받아야 하므로)")]
        [SerializeField] private GameObject _documentRoot;
        [Tooltip("기획서 문장이 표시될 텍스트. 줄바꿈(Wrapping)은 자동으로 끈다")]
        [SerializeField] private TMP_Text _documentText;
        [Tooltip("줄 번호 텍스트 (선택). 할당하면 기획서 줄 수에 맞춰 번호를 자동으로 쓴다")]
        [SerializeField] private TMP_Text _lineNumberText;
        [Tooltip("줄 번호를 최소 몇 줄까지 보여 줄지. 코드 에디터처럼 아래쪽에 빈 줄 번호가 이어지게 한다")]
        [SerializeField, Min(1)] private int _minLineCount = 10;
        [SerializeField] private Button _btnBuild;
        [Tooltip("빌드 버튼 글자 (선택). 할당하면 빌드 가능 여부에 따라 글자 색이 바뀐다")]
        [SerializeField] private TMP_Text _buildLabel;

        [Header("하단 단어 바")]
        [Tooltip("하단 단어 영역. 기획서가 열려 있는 동안 항상 보인다")]
        [SerializeField] private GameObject _optionBar;
        [Tooltip("단어가 생성될 부모 (Horizontal Layout Group 권장)")]
        [SerializeField] private Transform _optionContainer;
        [SerializeField] private SpecOptionButton _optionButtonPrefab;

        [Header("플레이 중 화면")]
        [Tooltip("우측 상단 리트 버튼. 기획서가 닫혀 있을 때만 보인다")]
        [SerializeField] private Button _btnRetry;

        [Header("스테이지 보기")]
        [Tooltip("미리보기 버튼 (Button_PreView). 기획서를 접고 펼친다. 접으면 레벨을 초기화하고 고른 문장으로 시간이 흐르며, 스테이지를 드래그로 둘러볼 수 있다. 접힌 동안에도 보여야 하므로 Document Root의 자식이면 안 된다")]
        [SerializeField] private Button _btnPreview;

        [Header("빈칸 상자")]
        [Tooltip("빈칸 뒤에 깔리는 상자 (UI_RoundRect_Fill, 9-slice). 비우면 각진 상자")]
        [SerializeField] private Sprite _boxSprite;
        [Tooltip("빈칸 상자 테두리 (UI_RoundRect_Outline, 9-slice). 비우면 테두리 없음")]
        [SerializeField] private Sprite _boxOutlineSprite;
        [Tooltip("빈칸 글자와 상자 사이 여백 (가로, 세로)")]
        [SerializeField] private Vector2 _boxPadding = new Vector2(10f, 4f);

        [Header("연출")]
        [Tooltip("타이핑 연출에서 글자 하나가 찍히는 간격(초). 0이면 한 번에 표시")]
        [SerializeField, Min(0f)] private float _typingInterval = 0.05f;
        [Tooltip("빈칸에 쓰인 단어의 투명도 (단어 바)")]
        [SerializeField, Range(0f, 1f)] private float _usedOptionAlpha = 0.4f;
        [Tooltip("비어 있는 빈칸에 표시할 글자")]
        [SerializeField] private string _emptyBlankText = "______";

        [Header("색 (코드 에디터 다크 테마)")]
        [SerializeField] private Color _commentColor = Hex("#4EC9B0");
        [SerializeField] private Color _bracketColor = Hex("#C586C0");
        [SerializeField] private Color _lineNumberColor = Hex("#5C6370");
        [SerializeField] private Color _currentLineNumberColor = Hex("#E8913B");
        [Tooltip("빈 빈칸: 글자 / 상자")]
        [SerializeField] private Color _blankEmptyTextColor = Hex("#5C6370");
        [SerializeField] private Color _blankEmptyBoxColor = Hex("#2D3340");
        [Tooltip("채운 빈칸: 글자 / 상자 / 테두리")]
        [SerializeField] private Color _blankFilledTextColor = Hex("#8FB8F0");
        [SerializeField] private Color _blankFilledBoxColor = Hex("#26344D");
        [SerializeField] private Color _blankFilledOutlineColor = Hex("#4F8BD6");
        [Tooltip("단어를 끌고 빈칸 위에 올라갔을 때: 글자·테두리 / 상자")]
        [SerializeField] private Color _blankHoverColor = Hex("#E8913B");
        [SerializeField] private Color _blankHoverBoxColor = new Color(0.91f, 0.57f, 0.23f, 0.25f);
        [Tooltip("빈칸을 다 채웠는데 품사가 맞지 않는 문장의 색. 이런 문장이 있으면 빌드할 수 없다")]
        [SerializeField] private Color _invalidSentenceColor = Hex("#F06A6A");
        [Tooltip("끌고 있는 단어 칩: 배경 / 테두리 / 글자")]
        [SerializeField] private Color _dragChipColor = Hex("#3A2C1E");
        [SerializeField] private Color _dragChipOutlineColor = Hex("#E8913B");
        [SerializeField] private Color _dragChipTextColor = Hex("#F5C189");
        [Tooltip("빌드 버튼: 가능할 때 배경 / 글자")]
        [SerializeField] private Color _buildEnabledColor = Hex("#2EA043");
        [SerializeField] private Color _buildEnabledTextColor = Color.white;
        [Tooltip("빌드 버튼: 불가능할 때 배경 / 글자")]
        [SerializeField] private Color _buildDisabledColor = Hex("#2D3340");
        [SerializeField] private Color _buildDisabledTextColor = Hex("#6E7681");

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

        /// <summary>
        /// 기획서 본문을 접어 미리보기를 시작했을 때. 지금 빈칸이 다 채워진 문장들을 전달한다.
        /// 받는 쪽은 레벨을 초기화하고 이 문장들을 적용한다. 미리보기 중에는 시간이 흐른다.
        /// </summary>
        public event Action<IReadOnlyList<Sentence>> PreviewStarted;

        /// <summary>기획서가 열려 있는 동안(미리보기 포함) true. 플레이어 조작을 막는 데 쓴다.</summary>
        public static bool IsAnyOpen { get; private set; }

        public bool IsOpen { get; private set; }

        /// <summary>기획서를 연 채(시간 정지) 본문을 접고 스테이지를 보고 있는 중. 이때도 IsOpen은 true다.</summary>
        public bool IsViewingStage { get; private set; }

        private List<StageSpec.Line> _lines;
        private List<SentenceTemplate> _templates;   // 문장 줄만 순서대로
        private int[] _blankOffsets;                 // 문장마다 첫 빈칸의 전체 번호

        private int[] _blankToOption;   // 빈칸 → 들어 있는 단어(묶음에서의 번호). 비었으면 -1
        private int[] _optionToBlank;   // 단어(묶음에서의 번호) → 들어가 있는 빈칸. 안 쓰였으면 -1
        private int[] _typedLength;     // 빈칸마다 지금까지 타이핑된 글자 수
        private Coroutine[] _typingRoutines;

        private readonly List<SpecOptionButton> _optionButtons = new List<SpecOptionButton>();
        private readonly StringBuilder _textBuilder = new StringBuilder();

        // 빈칸 상자 (텍스트 뒤에 깔림)
        private RectTransform _boxLayer;
        private readonly List<BlankBox> _boxes = new List<BlankBox>();

        // 끌기 상태
        private int _dragOption = -1;      // 끌고 있는 단어. 없으면 -1
        private int _dragFromBlank = -1;   // 빈칸에서 끌기 시작했으면 그 빈칸, 단어 바에서 시작했으면 -1
        private int _hoverBlank = -1;      // 끌고 있는 단어 아래의 빈칸
        private RectTransform _dragGhost;  // 마우스를 따라다니는 단어 칩
        private TextMeshProUGUI _dragGhostText;

        private bool _initialized;

        private bool IsDragging => _dragOption >= 0;

        private sealed class BlankBox
        {
            public RectTransform Rect;
            public Image Fill;
            public Image Outline;
        }

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
                IsAnyOpen = false;
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

            _lines = _spec.LoadLines();
            _templates = new List<SentenceTemplate>();
            foreach (StageSpec.Line line in _lines)
            {
                if (line.Kind == StageSpec.LineKind.Sentence)
                {
                    _templates.Add(line.Template);
                }
            }

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
            ApplyPresetWords();

            CreateOptionButtons();
            SetupTextAppearance();
            CreateBoxLayer();
            SetupBuildButton();

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
            if (_btnPreview != null)
            {
                _btnPreview.onClick.AddListener(() => SetStageView(!IsViewingStage));
            }

            return true;
        }

        /// <summary>
        /// StageSpec 줄에 적힌 처음 단어("NounIsNoun, Person _")를 빈칸에 미리 넣어 둔다.
        /// 단어 묶음에서 아직 안 쓰인 같은 단어를 앞에서부터 쓴다. 미리 넣은 단어도 보통 단어처럼 빼고 옮길 수 있다.
        /// 타이핑 연출 없이 바로 보인다 (Open에서 타이핑을 끝내 둔다).
        /// </summary>
        private void ApplyPresetWords()
        {
            IReadOnlyList<WordId> pool = _spec.WordPool;
            int sentenceIndex = 0;
            foreach (StageSpec.Line line in _lines)
            {
                if (line.Kind != StageSpec.LineKind.Sentence)
                {
                    continue;
                }

                int slotCount = _templates[sentenceIndex].Pattern.Count;
                for (int slot = 0; slot < line.Preset.Count && slot < slotCount; slot++)
                {
                    WordId word = line.Preset[slot];
                    if (word.IsEmpty)
                    {
                        continue;
                    }

                    int option = FindUnusedOption(pool, word);
                    if (option < 0)
                    {
                        Debug.LogWarning($"[SpecPanel] 처음 단어 '{word}'가 단어 묶음에 남아 있지 않아 빈칸으로 둡니다.", this);
                        continue;
                    }

                    int blank = _blankOffsets[sentenceIndex] + slot;
                    _blankToOption[blank] = option;
                    _optionToBlank[option] = blank;
                }
                sentenceIndex++;
            }
        }

        private int FindUnusedOption(IReadOnlyList<WordId> pool, WordId word)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == word && _optionToBlank[i] < 0)
                {
                    return i;
                }
            }
            return -1;
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

            if (_btnPreview == null)
            {
                Debug.LogWarning("[SpecPanel] Button_PreView가 할당되지 않았습니다. 기획서를 접고 미리보기를 할 수 없습니다.", this);
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

        /// <summary>
        /// 줄 번호가 기획서 줄과 정확히 맞도록 줄바꿈을 끄고, 줄 번호 텍스트의 글꼴·크기·줄 간격을 기획서와 똑같이 맞춘다.
        /// </summary>
        private void SetupTextAppearance()
        {
            _documentText.textWrappingMode = TextWrappingModes.NoWrap;

            if (_lineNumberText != null)
            {
                _lineNumberText.font = _documentText.font;
                _lineNumberText.fontSize = _documentText.fontSize;
                _lineNumberText.lineSpacing = _documentText.lineSpacing;
                _lineNumberText.textWrappingMode = TextWrappingModes.NoWrap;
                _lineNumberText.raycastTarget = false;
            }
        }

        /// <summary>
        /// 빈칸 상자를 그릴 층을 기획서 텍스트 바로 뒤에 만든다. 텍스트와 같은 위치·크기라서 글자 좌표를 그대로 쓸 수 있다.
        /// </summary>
        private void CreateBoxLayer()
        {
            var textRect = _documentText.rectTransform;
            var layerObject = new GameObject("BlankBoxes", typeof(RectTransform));
            _boxLayer = (RectTransform)layerObject.transform;
            _boxLayer.SetParent(textRect.parent, false);
            _boxLayer.SetSiblingIndex(textRect.GetSiblingIndex()); // 텍스트보다 먼저 그려져 뒤에 깔림
            _boxLayer.anchorMin = textRect.anchorMin;
            _boxLayer.anchorMax = textRect.anchorMax;
            _boxLayer.pivot = textRect.pivot;
            _boxLayer.offsetMin = textRect.offsetMin;
            _boxLayer.offsetMax = textRect.offsetMax;
        }

        /// <summary>
        /// 빌드 버튼의 색은 이 컴포넌트가 직접 정한다. Button의 비활성 색이 덧칠되어 흐려지지 않도록 비활성 색을 흰색으로 둔다.
        /// </summary>
        private void SetupBuildButton()
        {
            ColorBlock colors = _btnBuild.colors;
            colors.disabledColor = Color.white;
            _btnBuild.colors = colors;
        }

        // ---------------- 열기 / 닫기 ----------------

        private void Open()
        {
            IsOpen = true;
            IsAnyOpen = true;
            IsViewingStage = false;
            Time.timeScale = 0f;

            // 다시 열 때는 타이핑 연출 없이 이전 값을 그대로 보여 준다
            FinishAllTyping();
            CancelDrag();

            _documentRoot.SetActive(true);
            _optionBar.SetActive(true);
            SetRetryButtonVisible(false);
            SetPreviewButtonVisible(true);

            RefreshAll();
        }

        private void Close()
        {
            IsOpen = false;
            IsAnyOpen = false;
            IsViewingStage = false;
            Time.timeScale = 1f;

            FinishAllTyping();
            CancelDrag();

            _optionBar.SetActive(false);
            _documentRoot.SetActive(false);
            SetRetryButtonVisible(true);
            SetPreviewButtonVisible(false);
        }

        /// <summary>
        /// 기획서를 연 채 본문과 단어 바만 접어서 스테이지를 미리 본다.
        /// 접을 때 레벨을 초기화하고 지금 고른 문장을 적용한 뒤 시간을 흐르게 한다 (플레이어 조작은 계속 막힘).
        /// 다시 펼치면 그 상태 그대로 시간만 멈춘다. 고른 단어는 그대로 남는다.
        /// </summary>
        private void SetStageView(bool viewing)
        {
            if (!IsOpen || IsViewingStage == viewing)
            {
                return;
            }
            IsViewingStage = viewing;

            FinishAllTyping();
            CancelDrag();

            _documentRoot.SetActive(!viewing);
            _optionBar.SetActive(!viewing);

            if (viewing)
            {
                Time.timeScale = 1f;
                PreviewStarted?.Invoke(CollectFilledSentences());
            }
            else
            {
                Time.timeScale = 0f;
                RefreshAll();
            }
        }

        // 기획서가 열려 있는 동안(접힌 상태 포함)만 보인다. 플레이 중에는 숨긴다
        private void SetPreviewButtonVisible(bool visible)
        {
            if (_btnPreview != null)
            {
                _btnPreview.gameObject.SetActive(visible);
            }
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
            _dragGhostText.ForceMeshUpdate();
            Vector2 textSize = _dragGhostText.GetPreferredValues();
            _dragGhost.sizeDelta = new Vector2(textSize.x + 32f, textSize.y + 14f);
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

        /// <summary>
        /// 마우스를 따라다닐 단어 칩을 처음 한 번 만든다. 어두운 배경에서도 잘 보이도록 배경·테두리를 갖춘 칩 모양이다.
        /// 클릭을 가로채지 않도록 입력은 받지 않는다.
        /// </summary>
        private void EnsureDragGhost()
        {
            if (_dragGhost != null)
            {
                return;
            }

            var ghostObject = new GameObject("DragGhost", typeof(RectTransform));
            ghostObject.transform.SetParent(_documentText.canvas.rootCanvas.transform, false);
            _dragGhost = (RectTransform)ghostObject.transform;

            Image background = ghostObject.AddComponent<Image>();
            ApplySprite(background, _boxSprite);
            background.color = _dragChipColor;
            background.raycastTarget = false;

            if (_boxOutlineSprite != null)
            {
                Image outline = CreateChildImage(_dragGhost, "Outline", _boxOutlineSprite);
                outline.color = _dragChipOutlineColor;
            }

            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(_dragGhost, false);
            StretchFull((RectTransform)textObject.transform);
            _dragGhostText = textObject.AddComponent<TextMeshProUGUI>();
            _dragGhostText.font = _documentText.font;
            _dragGhostText.fontSize = _documentText.fontSize * 0.9f;
            _dragGhostText.color = _dragChipTextColor;
            _dragGhostText.alignment = TextAlignmentOptions.Center;
            _dragGhostText.textWrappingMode = TextWrappingModes.NoWrap;
            _dragGhostText.raycastTarget = false;

            ghostObject.SetActive(false);
        }

        private void MoveDragGhost(PointerEventData eventData)
        {
            var parent = (RectTransform)_dragGhost.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                // 마우스 바로 아래가 아니라 살짝 오른쪽 아래에 두어 놓을 빈칸이 가려지지 않게 한다
                _dragGhost.anchoredPosition = localPoint + new Vector2(_dragGhost.sizeDelta.x * 0.5f + 12f, -_dragGhost.sizeDelta.y * 0.5f - 12f);
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
        /// - 단어 바에서 끌어오면 빈칸에 있던 원래 단어는 단어 바로 돌아간다
        /// - 다른 빈칸에서 끌어오면 두 빈칸의 단어가 서로 자리를 바꾼다 (놓은 빈칸이 비어 있었으면 끌어온 빈칸은 비워진다)
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

            // 빈칸끼리 옮긴 경우: 놓은 빈칸에 있던 단어를 끌어온 빈칸으로 보내 자리를 맞바꾼다
            if (otherBlank >= 0 && previousOption >= 0)
            {
                _blankToOption[otherBlank] = previousOption;
                _optionToBlank[previousOption] = otherBlank;
                StartTyping(otherBlank);
            }
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
            RefreshBuildButton();
        }

        private void RefreshOptionButtons()
        {
            for (int i = 0; i < _optionButtons.Count; i++)
            {
                _optionButtons[i].SetUsed(_optionToBlank[i] >= 0, _usedOptionAlpha);
            }
        }

        private void RefreshBuildButton()
        {
            bool canBuild = CanBuild();
            _btnBuild.interactable = canBuild;

            if (_btnBuild.targetGraphic != null)
            {
                _btnBuild.targetGraphic.color = canBuild ? _buildEnabledColor : _buildDisabledColor;
            }
            if (_buildLabel != null)
            {
                _buildLabel.color = canBuild ? _buildEnabledTextColor : _buildDisabledTextColor;
            }
        }

        /// <summary>
        /// 기획서를 코드 에디터처럼 그린다.
        /// - 주석 줄은 주석 색, 빈 줄은 빈 줄
        /// - 문장 틀의 {0}, {1} 자리는 [ 빈칸 ] 으로 바꾸고, 빈칸에는 끌어다 놓을 위치를 찾기 위한 링크를 단다
        /// 그린 뒤 줄 번호와 빈칸 상자를 맞춘다.
        /// </summary>
        private void RenderDocument()
        {
            _textBuilder.Clear();
            int sentenceIndex = 0;
            int currentLine = -1;

            for (int l = 0; l < _lines.Count; l++)
            {
                StageSpec.Line line = _lines[l];

                if (line.Kind == StageSpec.LineKind.Comment)
                {
                    AppendColored(line.Comment, _commentColor, true);
                }
                else if (line.Kind == StageSpec.LineKind.Sentence)
                {
                    if (AppendSentence(sentenceIndex))
                    {
                        currentLine = l;
                    }
                    sentenceIndex++;
                }

                if (l < _lines.Count - 1)
                {
                    _textBuilder.Append('\n');
                }
            }

            _documentText.text = _textBuilder.ToString();
            RenderLineNumbers(currentLine);
            LayoutBlankBoxes();
        }

        /// <returns>끌고 있는 단어가 이 문장의 빈칸 위에 있으면 true (현재 줄로 강조)</returns>
        private bool AppendSentence(int sentenceIndex)
        {
            SentenceTemplate template = _templates[sentenceIndex];
            string text = template.Text ?? string.Empty;
            int cursor = 0;
            bool hasHover = false;

            // 품사가 맞지 않는 문장은 문장 전체를 붉게 보여준다. 단어를 빼면 원래 색으로 돌아온다
            bool invalid = IsSentenceInvalid(sentenceIndex);
            if (invalid)
            {
                _textBuilder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(_invalidSentenceColor)).Append('>');
            }

            foreach (Match match in SentenceTemplate.TokenPattern.Matches(text))
            {
                _textBuilder.Append(text, cursor, match.Index - cursor);

                int slot = int.Parse(match.Groups[1].Value);
                if (match.Groups[2].Success)
                {
                    // 끝말: 빈칸 단어가 다 써진 뒤에 그 단어 품사에 맞는 말을 붙인다
                    if (slot < template.Pattern.Count)
                    {
                        AppendEnding(_blankOffsets[sentenceIndex] + slot, match.Groups[2].Value);
                    }
                    else
                    {
                        _textBuilder.Append(match.Value);
                    }
                }
                else if (slot < template.Pattern.Count)
                {
                    int blankIndex = _blankOffsets[sentenceIndex] + slot;
                    hasHover |= blankIndex == _hoverBlank;

                    AppendColored("[", _bracketColor, false);
                    _textBuilder.Append(' ');
                    AppendBlank(blankIndex, invalid);
                    _textBuilder.Append(' ');
                    AppendColored("]", _bracketColor, false);
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
            return hasHover;
        }

        private void AppendEnding(int blankIndex, string rule)
        {
            int option = _blankToOption[blankIndex];
            if (option < 0 || _typedLength[blankIndex] < GetWordText(blankIndex).Length)
            {
                return;
            }
            _textBuilder.Append(SentenceTemplate.ResolveEnding(rule, _spec.WordPool[option]));
        }

        private void AppendBlank(int blankIndex, bool invalidSentence)
        {
            bool filled = _blankToOption[blankIndex] >= 0;
            string content = filled
                ? GetWordText(blankIndex).Substring(0, _typedLength[blankIndex])
                : _emptyBlankText;

            Color color;
            if (blankIndex == _hoverBlank)
            {
                color = _blankHoverColor;
            }
            else if (invalidSentence)
            {
                color = _invalidSentenceColor;
            }
            else
            {
                color = filled ? _blankFilledTextColor : _blankEmptyTextColor;
            }

            _textBuilder.Append("<link=\"").Append(BlankLinkPrefix).Append(blankIndex).Append("\">");
            AppendColored(content, color, true);
            _textBuilder.Append("</link>");
        }

        private void AppendColored(string text, Color color, bool noParse)
        {
            _textBuilder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(color)).Append('>');
            if (noParse)
            {
                _textBuilder.Append("<noparse>").Append(text).Append("</noparse>");
            }
            else
            {
                _textBuilder.Append(text);
            }
            _textBuilder.Append("</color>");
        }

        private void RenderLineNumbers(int currentLine)
        {
            if (_lineNumberText == null)
            {
                return;
            }

            int count = Mathf.Max(_minLineCount, _lines.Count);
            var builder = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                Color color = i == currentLine ? _currentLineNumberColor : _lineNumberColor;
                builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(color)).Append('>')
                       .Append((i + 1).ToString("00"))
                       .Append("</color>");
                if (i < count - 1)
                {
                    builder.Append('\n');
                }
            }
            _lineNumberText.text = builder.ToString();
        }

        // ---------------- 빈칸 상자 ----------------

        /// <summary>
        /// 각 빈칸 글자의 위치를 읽어 그 뒤에 상자를 깐다. 텍스트가 바뀔 때마다(타이핑 포함) 다시 맞춘다.
        /// </summary>
        private void LayoutBlankBoxes()
        {
            _documentText.ForceMeshUpdate();
            TMP_TextInfo info = _documentText.textInfo;
            int used = 0;

            for (int i = 0; i < info.linkCount; i++)
            {
                TMP_LinkInfo link = info.linkInfo[i];
                string linkId = link.GetLinkID();
                if (!linkId.StartsWith(BlankLinkPrefix, StringComparison.Ordinal)
                    || !int.TryParse(linkId.Substring(BlankLinkPrefix.Length), out int blankIndex))
                {
                    continue;
                }

                if (!TryGetLinkBounds(info, link, out Vector2 min, out Vector2 max))
                {
                    continue; // 타이핑이 막 시작되어 아직 글자가 없는 경우
                }

                BlankBox box = GetBox(used++);
                box.Rect.gameObject.SetActive(true);
                box.Rect.anchoredPosition = min - _boxPadding;
                box.Rect.sizeDelta = (max - min) + _boxPadding * 2f;
                ApplyBoxColors(box, blankIndex);
            }

            for (int i = used; i < _boxes.Count; i++)
            {
                _boxes[i].Rect.gameObject.SetActive(false);
            }
        }

        private static bool TryGetLinkBounds(TMP_TextInfo info, TMP_LinkInfo link, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;

            int end = link.linkTextfirstCharacterIndex + link.linkTextLength;
            for (int c = link.linkTextfirstCharacterIndex; c < end && c < info.characterCount; c++)
            {
                TMP_CharacterInfo ch = info.characterInfo[c];
                TMP_LineInfo line = info.lineInfo[ch.lineNumber];
                // 높이는 글자마다 다르지 않도록 줄의 위·아래 기준을 쓴다
                min = Vector2.Min(min, new Vector2(ch.bottomLeft.x, line.descender));
                max = Vector2.Max(max, new Vector2(ch.topRight.x, line.ascender));
                any = true;
            }
            return any;
        }

        private void ApplyBoxColors(BlankBox box, int blankIndex)
        {
            bool filled = _blankToOption[blankIndex] >= 0;
            bool invalid = filled && IsSentenceInvalid(SentenceOfBlank(blankIndex));

            Color fill;
            Color outline;
            if (blankIndex == _hoverBlank)
            {
                fill = _blankHoverBoxColor;
                outline = _blankHoverColor;
            }
            else if (invalid)
            {
                fill = _blankFilledBoxColor;
                outline = _invalidSentenceColor;
            }
            else if (filled)
            {
                fill = _blankFilledBoxColor;
                outline = _blankFilledOutlineColor;
            }
            else
            {
                fill = _blankEmptyBoxColor;
                outline = Color.clear;
            }

            box.Fill.color = fill;
            if (box.Outline != null)
            {
                box.Outline.color = outline;
            }
        }

        private BlankBox GetBox(int index)
        {
            while (_boxes.Count <= index)
            {
                var boxObject = new GameObject($"Box_{_boxes.Count}", typeof(RectTransform));
                var rect = (RectTransform)boxObject.transform;
                rect.SetParent(_boxLayer, false);

                // 상자 층의 기준점(= 텍스트 좌표의 원점)에서부터 위치를 잡는다
                rect.anchorMin = _boxLayer.pivot;
                rect.anchorMax = _boxLayer.pivot;
                rect.pivot = Vector2.zero;

                Image fill = boxObject.AddComponent<Image>();
                ApplySprite(fill, _boxSprite);
                fill.raycastTarget = false;

                Image outline = _boxOutlineSprite != null ? CreateChildImage(rect, "Outline", _boxOutlineSprite) : null;

                _boxes.Add(new BlankBox { Rect = rect, Fill = fill, Outline = outline });
            }
            return _boxes[index];
        }

        private int SentenceOfBlank(int blankIndex)
        {
            for (int s = _blankOffsets.Length - 1; s >= 0; s--)
            {
                if (blankIndex >= _blankOffsets[s])
                {
                    return s;
                }
            }
            return 0;
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

        /// <summary>빈칸을 채운 단어로 문장(Sentence)을 만든다.</summary>
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

        /// <summary>미리보기용. 빈칸이 하나라도 비어 있는 문장은 빼고 만든다.</summary>
        private List<Sentence> CollectFilledSentences()
        {
            var sentences = new List<Sentence>(_templates.Count);
            for (int s = 0; s < _templates.Count; s++)
            {
                SentenceTemplate template = _templates[s];
                var words = new List<WordId>(template.Pattern.Count);
                bool filled = true;
                for (int slot = 0; slot < template.Pattern.Count; slot++)
                {
                    int option = _blankToOption[_blankOffsets[s] + slot];
                    if (option < 0)
                    {
                        filled = false;
                        break;
                    }
                    words.Add(_spec.WordPool[option]);
                }
                if (filled)
                {
                    sentences.Add(new Sentence(template, words));
                }
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

        private static void ApplySprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        }

        private static Image CreateChildImage(RectTransform parent, string name, Sprite sprite)
        {
            var child = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            StretchFull(rect);
            Image image = child.AddComponent<Image>();
            ApplySprite(image, sprite);
            image.raycastTarget = false;
            return image;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // 필드 초기값에서 쓰므로 Unity API(ColorUtility)를 부르지 않고 직접 읽는다 ("#RRGGBB")
        private static Color Hex(string hex)
        {
            int value = Convert.ToInt32(hex.TrimStart('#'), 16);
            return new Color(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f, 1f);
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
