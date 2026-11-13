using System;
using System.Collections.Generic;
using CoffeeSpec.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 스테이지에서 ESC를 누르면 뜨는 2단 메뉴. 1단: 설정 / 스테이지 선택 / 메인화면 / 게임종료. 스테이지 선택은 지금 스테이지 위에 타이틀과 같은 목록을 띄운다. 2단(설정): 배경음악·효과음 볼륨, 화면모드, 해상도.
    /// 씬·프리팹을 고치지 않아도 되게 게임이 켜질 때 한 번 만들어 씬이 바뀌어도 남기고, 화면도 전부 코드로 그린다.
    /// 디자인은 귀 없는 둥근 창 + 선택한 줄 옆에 앉아 있는 플레이어 고양이(커서) 시안(C안 수정)을 따른다.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        // ---------------- 시안 색 ----------------
        private static readonly Color PanelColor = Hex(0x22252E);
        private static readonly Color EdgeColor = Hex(0x7FA3C8);
        private static readonly Color RowColor = Hex(0x2F3A4C);
        private static readonly Color AccentColor = Hex(0xE8913B);
        private static readonly Color TextColor = Hex(0xDCDFE4);
        private static readonly Color HintColor = Hex(0x5C6370);
        private static readonly Color TrackColor = Hex(0x3A3F4B);
        private static readonly Color KnobColor = Hex(0xF5C189);
        private static readonly Color DimColor = new Color(10f / 255f, 10f / 255f, 14f / 255f, 0.67f);
        // 타이틀 스테이지 선택 팝업과 같은 색
        private static readonly Color StageWindowColor = Hex(0x252526);
        private static readonly Color StageTitleBarColor = Hex(0x2D2F34);
        private static readonly Color StageCardColor = Hex(0x3A3D44);
        private static readonly Color StageOutlineColor = Hex(0x3C3F45);
        private static readonly Color StageLabelColor = Hex(0xD4D4D4);
        private static readonly Color StageCloseColor = Hex(0xA0A4AC);

        private const string TitleSceneName = "Title";
        private const string CatSheet = "PlayerCat";
        // 화면 전환 페이드(1000)보다는 아래, 기획서·클리어 화면(100)보다는 위
        private const int SortingOrder = 900;
        private const float VolumeStep = 0.05f;
        private const float CatFrameTime = 0.5f;

        private static PauseMenu _instance;

        /// <summary>메뉴가 열려 있으면 true. 다른 스크립트가 입력을 막을 때 쓸 수 있다.</summary>
        public static bool IsOpen => _instance != null && _instance._page != Page.Closed;

        private enum Page { Closed, Main, Settings, StageSelect }

        private Page _page = Page.Closed;
        private float _timeScaleBeforeOpen = 1f;
        // 타이틀의 Settings 줄에서 열었으면 true. 설정 창만 보여 주고, 뒤로(ESC)를 누르면 1단 메뉴 대신 바로 닫는다
        private bool _settingsOnly;

        private GameObject _root;
        private GameObject _mainPanel;
        private GameObject _settingsPanel;
        private GameObject _stagePanel;
        private TMP_FontAsset _font;
        private Sprite _roundFill;
        private Sprite _roundEdge;
        private Sprite _circle;

        private readonly List<Row> _mainRows = new List<Row>();
        private readonly List<Row> _settingRows = new List<Row>();
        private readonly List<Row> _stageRows = new List<Row>();
        private int _mainIndex;
        private int _settingIndex;
        private int _stageIndex;

        private Image _cat;
        private Sprite[] _catFrames;
        private float _catTimer;
        private int _catFrame;

        private VolumeSlider _bgmSlider;
        private VolumeSlider _sfxSlider;
        private ModeButton _fullButton;
        private ModeButton _windowButton;
        private TextMeshProUGUI _resolutionText;
        private Image _applyButton;

        private readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
        private bool _pendingFullscreen;
        private int _pendingResolution;

        // 한 줄. 마우스를 올리면 선택되고, 클릭·Enter면 Submit, 좌우 키면 Adjust
        private sealed class Row
        {
            public RectTransform Rect;
            public Image Highlight;
            public TextMeshProUGUI Label;
            public Action Submit;
            public Action<int> Adjust;
            // 회색으로 보이고 선택·실행되지 않는 줄 (웹 빌드의 게임종료)
            public bool Disabled;
            // 스테이지 선택 칸. 잠기면 칸 색을 검게 하고 섬네일을 숨긴다
            public Image Card;
            public GameObject Thumb;
        }

        // ---------------- 생성 ----------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("PauseMenu");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PauseMenu>();
        }

        private void Awake()
        {
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            GameSettings.VolumeChanged -= RefreshSettingVisuals;
            if (_instance == this) _instance = null;
        }

        // 메뉴가 열린 채 씬이 바뀌면(리트 등) 다음 씬이 멈춰 있지 않게 닫고 시간을 되돌린다
        private void OnSceneChanged(Scene from, Scene to)
        {
            if (_page == Page.Closed) return;
            _settingsOnly = false;
            SetPage(Page.Closed);
            Time.timeScale = 1f;
        }

        /// <summary>
        /// 타이틀 화면의 Settings 줄에서 부른다. 1단 메뉴(설정/스테이지 선택/메인화면/게임종료) 없이 설정 창만 연다.
        /// 타이틀에는 멈출 게임이 없으므로 시간은 건드리지 않는다.
        /// </summary>
        public static void OpenSettings()
        {
            if (_instance == null) Create();
            _instance.OpenSettingsOnly();
        }

        private void OpenSettingsOnly()
        {
            if (_page != Page.Closed) return;
            EnsureBuilt();
            EnsureEventSystem();
            _settingsOnly = true;
            // Close가 이 값으로 되돌리므로, 지금 시간을 그대로 넣어 두면 닫아도 시간은 바뀌지 않는다
            _timeScaleBeforeOpen = Time.timeScale;
            SetPage(Page.Settings);
        }

        // 설정 창의 뒤로(ESC, 'ESC 뒤로' 클릭). 타이틀에서 연 설정 창이면 닫고, 아니면 1단 메뉴로 돌아간다
        private void BackFromSettings()
        {
            if (_settingsOnly) Close();
            else SetPage(Page.Main);
        }

        // ---------------- 입력 ----------------

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                OnEscape();
                return;
            }
            if (_page == Page.Closed) return;

            AnimateCat();

            bool up = kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
            bool down = kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame;
            bool right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            bool submit = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;

            var rows = CurrentRows();
            int index = CurrentIndex();
            if (_page == Page.StageSelect)
            {
                if (up || left) Select(NextEnabled(rows, index, -1));
                if (down || right) Select(NextEnabled(rows, index, 1));
            }
            else
            {
                if (up) Select(NextEnabled(rows, index, -1));
                if (down) Select(NextEnabled(rows, index, 1));
                index = CurrentIndex();
                if (left) rows[index].Adjust?.Invoke(-1);
                if (right) rows[index].Adjust?.Invoke(1);
            }
            index = CurrentIndex();
            if (submit) rows[index].Submit?.Invoke();
        }

        private void OnEscape()
        {
            switch (_page)
            {
                case Page.Closed:
                    // 일시정지는 스테이지에서만. 화면이 넘어가는 중에는 열지 않는다
                    if (SceneFader.IsLoading) return;
                    if (!StageScenes.TryGetStageNumber(SceneManager.GetActiveScene().name, out _)) return;
                    Open();
                    break;
                case Page.Main:
                    Close();
                    break;
                case Page.StageSelect:
                    SetPage(Page.Main);
                    break;
                case Page.Settings:
                    // 적용 안 한 화면 설정은 버리고 1단으로 돌아간다 (볼륨은 움직이는 즉시 저장됨). 타이틀에서 열었으면 닫는다
                    BackFromSettings();
                    break;
            }
        }

        private void Open()
        {
            EnsureBuilt();
            EnsureEventSystem();
            // 기획서가 열려 이미 0이었으면 닫을 때도 0으로 돌려놓아야 기획서 상태가 안 깨진다
            _timeScaleBeforeOpen = Time.timeScale;
            Time.timeScale = 0f;
            _mainIndex = 0;
            SetPage(Page.Main);
        }

        private void Close()
        {
            _settingsOnly = false;
            SetPage(Page.Closed);
            Time.timeScale = _timeScaleBeforeOpen;
        }

        private void SetPage(Page page)
        {
            _page = page;
            if (_root == null) return;
            _root.SetActive(page != Page.Closed);
            _mainPanel.SetActive(page == Page.Main);
            _settingsPanel.SetActive(page == Page.Settings);
            if (_stagePanel != null) _stagePanel.SetActive(page == Page.StageSelect);
            if (page == Page.Settings)
            {
                LoadPendingDisplay();
                _settingIndex = 0;
            }
            if (page != Page.Closed) Select(CurrentIndex());
        }

        // 막힌 줄은 건너뛰고 다음 줄을 고른다
        private static int NextEnabled(List<Row> rows, int index, int step)
        {
            for (int n = 1; n <= rows.Count; n++)
            {
                int next = ((index + step * n) % rows.Count + rows.Count) % rows.Count;
                if (!rows[next].Disabled) return next;
            }
            return index;
        }

        private void Select(int index)
        {
            var rows = CurrentRows();
            if (rows[index].Disabled) return;
            SetCurrentIndex(index);
            for (int i = 0; i < rows.Count; i++)
            {
                bool on = i == index;
                if (rows[i].Highlight != null) rows[i].Highlight.enabled = on;
                if (rows[i].Label != null) rows[i].Label.color = rows[i].Disabled ? HintColor : on ? AccentColor : TextColor;
            }
            RefreshSettingVisuals();

            // 고양이 커서를 선택한 줄 왼쪽에 앉힌다. 적용 버튼 줄은 커서 대신 버튼 색으로 보여 준다
            var row = rows[index];
            // 스테이지 선택 창은 타이틀 팝업처럼 고양이 커서 대신 칸 테두리로 고른 자리를 보여 준다
            bool showCat = row.Label != null && _page != Page.StageSelect;
            _cat.gameObject.SetActive(showCat);
            if (showCat)
            {
                _cat.rectTransform.SetParent(row.Rect, false);
                _cat.rectTransform.anchoredPosition = new Vector2(_page == Page.Main ? 40f : 20f, 0f);
            }
        }

        private void AnimateCat()
        {
            if (_catFrames == null || _catFrames.Length < 2) return;
            _catTimer += Time.unscaledDeltaTime;
            if (_catTimer < CatFrameTime) return;
            _catTimer = 0f;
            _catFrame = 1 - _catFrame;
            _cat.sprite = _catFrames[_catFrame];
        }

        // ---------------- 1단 메뉴 동작 ----------------

        private List<Row> CurrentRows()
        {
            if (_page == Page.Main) return _mainRows;
            if (_page == Page.StageSelect) return _stageRows;
            return _settingRows;
        }

        private int CurrentIndex()
        {
            if (_page == Page.Main) return _mainIndex;
            if (_page == Page.StageSelect) return _stageIndex;
            return _settingIndex;
        }

        private void SetCurrentIndex(int index)
        {
            if (_page == Page.Main) _mainIndex = index;
            else if (_page == Page.StageSelect) _stageIndex = index;
            else _settingIndex = index;
        }

        private void GoToTitle()
        {
            if (SceneFader.IsLoading) return;
            SetPage(Page.Closed);
            // 타이틀이 멈춘 채로 뜨지 않게 시간부터 되돌린다 (페이드는 실제 시간으로 돈다)
            Time.timeScale = 1f;
            SceneFader.LoadScene(TitleSceneName);
        }

        private static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------- 2단 설정 동작 ----------------

        private void LoadPendingDisplay()
        {
            _resolutions.Clear();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (size.x < 1280 || size.y < 720 || _resolutions.Contains(size)) continue;
                _resolutions.Add(size);
            }
            // 에디터 등에서 목록이 비면 흔한 16:9 해상도로 채운다
            if (_resolutions.Count == 0)
            {
                _resolutions.Add(new Vector2Int(1280, 720));
                _resolutions.Add(new Vector2Int(1600, 900));
                _resolutions.Add(new Vector2Int(1920, 1080));
                _resolutions.Add(new Vector2Int(2560, 1440));
            }
            var current = GameSettings.IsFullscreen ? GameSettings.SavedResolution : new Vector2Int(Screen.width, Screen.height);
            if (!_resolutions.Contains(current)) _resolutions.Add(current);
            _resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            _pendingResolution = _resolutions.IndexOf(current);
            _pendingFullscreen = GameSettings.IsFullscreen;
            RefreshSettingVisuals();
        }

        // 웹 빌드는 캔버스 크기를 브라우저 창이 정하므로 해상도는 못 바꾸고 화면모드(전체화면/창모드)만 바꾼다
// const가 아니라 readonly: 플랫폼마다 한쪽 분기가 '도달할 수 없는 코드' 경고(CS0162)로 잡히지 않게 한다
#if UNITY_WEBGL && !UNITY_EDITOR
        private static readonly bool ResolutionLocked = true;
#else
        private static readonly bool ResolutionLocked = false;
#endif
        private const string LockedResolutionText = "창 크기에 맞춤";

        private void ChangeResolution(int dir)
        {
            if (ResolutionLocked || _resolutions.Count == 0) return;
            _pendingResolution = Mathf.Clamp(_pendingResolution + dir, 0, _resolutions.Count - 1);
            RefreshSettingVisuals();
        }

        private void SetPendingFullscreen(bool fullscreen)
        {
            _pendingFullscreen = fullscreen;
            RefreshSettingVisuals();
        }

        private void ApplyDisplay()
        {
            if (_resolutions.Count == 0) return;
            var size = _resolutions[_pendingResolution];
            GameSettings.ApplyDisplay(size.x, size.y, _pendingFullscreen);
        }

        // 적용 버튼: 화면 설정을 바꾸고 창을 닫는다 (화면모드·해상도 줄의 Enter로는 적용하지 않는다)
        private void ApplyAndClose()
        {
            ApplyDisplay();
            Close();
        }

        private void RefreshSettingVisuals()
        {
            if (_settingsPanel == null) return;
            _bgmSlider.Refresh(GameSettings.BgmVolume, _settingIndex == 0 && _page == Page.Settings);
            _sfxSlider.Refresh(GameSettings.SfxVolume, _settingIndex == 1 && _page == Page.Settings);
            _fullButton.Refresh(_pendingFullscreen);
            _windowButton.Refresh(!_pendingFullscreen);
            if (ResolutionLocked)
            {
                _resolutionText.text = LockedResolutionText;
            }
            else if (_resolutions.Count > 0 && _pendingResolution >= 0)
            {
                var size = _resolutions[_pendingResolution];
                _resolutionText.text = $"{size.x} × {size.y}";
            }
            bool applySelected = _page == Page.Settings && _settingIndex == _settingRows.Count - 1;
            _applyButton.color = applySelected ? KnobColor : AccentColor;
        }

        // ---------------- 화면 만들기 ----------------

        private void EnsureBuilt()
        {
            if (_root != null) return;

            _font = FindFont();
            _roundFill = MakeRoundSprite(64, 16, 0);
            _roundEdge = MakeRoundSprite(64, 16, 3);
            _circle = MakeRoundSprite(64, 32, 0);
            _catFrames = LoadCatFrames();

            var canvasGo = new GameObject("PauseMenuCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            _root = canvasGo;

            // 화면 전체를 어둡게 덮어 뒤쪽 기획서·버튼이 눌리지 않게 막는다
            var dim = NewImage("Dim", canvasGo.transform, null, DimColor);
            Stretch(dim.rectTransform);

            BuildMainPanel(canvasGo.transform);
            BuildSettingsPanel(canvasGo.transform);
            BuildStageSelectPanel(canvasGo.transform);

            _cat = NewImage("CatCursor", canvasGo.transform, _catFrames.Length > 0 ? _catFrames[0] : null, Color.white);
            _cat.preserveAspect = true;
            _cat.raycastTarget = false;
            var catRect = _cat.rectTransform;
            catRect.anchorMin = catRect.anchorMax = new Vector2(0f, 0.5f);
            catRect.pivot = new Vector2(0f, 0.5f);
            catRect.sizeDelta = new Vector2(72f, 60f);

            _root.SetActive(false);
        }

        private void BuildMainPanel(Transform parent)
        {
            _mainPanel = BuildPanel(parent, "MainPanel", new Vector2(640f, 640f), "PAUSED");
            string[] labels = { "설정", "스테이지 선택", "메인화면", "게임종료" };
            Action[] actions = { () => SetPage(Page.Settings), OpenStageSelect, GoToTitle, QuitGame };
            for (int i = 0; i < labels.Length; i++)
            {
                var row = NewRow(_mainPanel.transform, i, new Vector2(560f, 92f), -150f - i * 120f, _mainRows, true);
                row.Label = NewText("Label", row.Rect, labels[i], 48f, TextColor, TextAlignmentOptions.MidlineLeft);
                Place(row.Label.rectTransform, new Vector2(140f, 0f), new Vector2(380f, 80f));
                row.Submit = actions[i];
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            // 브라우저에서는 게임을 끌 수 없으므로 게임종료 줄을 막는다
            var quitRow = _mainRows[_mainRows.Count - 1];
            quitRow.Submit = null;
            quitRow.Disabled = true;
            quitRow.Label.color = HintColor;
#endif
        }

        private void OpenStageSelect()
        {
            EnsureBuilt();
            RefreshStageCards();
            _stageIndex = 0;
            if (StageScenes.TryGetStageNumber(SceneManager.GetActiveScene().name, out int current))
            {
                int i = current - 1;
                if (i >= 0 && i < _stageRows.Count && !_stageRows[i].Disabled)
                    _stageIndex = i;
            }
            SetPage(Page.StageSelect);
        }

        private void LoadSelectedStage(int stageNumber)
        {
            if (SceneFader.IsLoading) return;
            SetPage(Page.Closed);
            Time.timeScale = 1f;
            SceneFader.LoadScene(StageScenes.GetSceneName(stageNumber));
        }

        private void RefreshStageCards()
        {
            int count = _stageRows.Count;
            SaveData save = SaveDataStore.Load();
            int unlocked = GetUnlockedStageCount(save, count);
            for (int i = 0; i < count; i++)
            {
                bool on = i < unlocked;
                var row = _stageRows[i];
                row.Disabled = !on;
                int stageNumber = i + 1;
                row.Submit = on ? () => LoadSelectedStage(stageNumber) : (Action)null;
                if (row.Card != null) row.Card.color = on ? StageCardColor : Color.black;
                if (row.Thumb != null) row.Thumb.SetActive(on);
            }
        }

        // 타이틀의 스테이지 선택과 같은 해제 규칙. 세이브가 없으면 1만, 전부 클리어면 전부, 아니면 가장 높이 깬 스테이지의 다음까지.
        private static int GetUnlockedStageCount(SaveData save, int stageCount)
        {
            if (stageCount <= 0) return 0;
            if (save == null) return 1;
            if (save.HighestClearedStage >= stageCount) return stageCount;
            return Mathf.Clamp(save.HighestClearedStage + 1, 1, stageCount);
        }

        private static int CountBuiltStages()
        {
            int max = 0;
            int n = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < n; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (StageScenes.TryGetStageNumber(name, out int number) && number > max)
                    max = number;
            }
            return max > 0 ? max : 5;
        }

        // 타이틀의 스테이지 선택 팝업과 같은 배치·색. 그림(썸네일)은 에디터나 이미 메모리에 있을 때만 넣고, 없으면 문구만 보여 준다.
        private void BuildStageSelectPanel(Transform parent)
        {
            int stageCount = CountBuiltStages();
            const float cardW = 308f;
            const float cardH = 252f;
            const float gap = 30f;
            const float padX = 45f;
            const float titleBarH = 78.4f;
            float width = padX * 2f + stageCount * cardW + Mathf.Max(0, stageCount - 1) * gap;

            var panelImage = NewImage("StageSelectPanel", parent, _roundFill, StageWindowColor);
            panelImage.type = Image.Type.Sliced;
            panelImage.pixelsPerUnitMultiplier = 0.6f;
            _stagePanel = panelImage.gameObject;
            var rect = panelImage.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 490f);

            var edge = NewImage("Edge", rect, _roundEdge, StageOutlineColor);
            edge.type = Image.Type.Sliced;
            edge.pixelsPerUnitMultiplier = 0.6f;
            edge.raycastTarget = false;
            Stretch(edge.rectTransform);

            var titleBar = NewImage("TitleBar", rect, _roundFill, StageTitleBarColor);
            titleBar.type = Image.Type.Sliced;
            titleBar.pixelsPerUnitMultiplier = 0.6f;
            titleBar.raycastTarget = false;
            var titleBarRect = titleBar.rectTransform;
            titleBarRect.anchorMin = new Vector2(0f, 1f);
            titleBarRect.anchorMax = new Vector2(1f, 1f);
            titleBarRect.pivot = new Vector2(0.5f, 1f);
            titleBarRect.anchoredPosition = Vector2.zero;
            titleBarRect.sizeDelta = new Vector2(0f, titleBarH);

            var title = NewText("Title", titleBarRect, "SelectStage.cs", 39f, StageLabelColor, TextAlignmentOptions.MidlineLeft);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(32f, 0f);
            title.rectTransform.offsetMax = new Vector2(-90f, 0f);
            title.raycastTarget = false;

            var close = NewText("Close", titleBarRect, "\u00D7", 48f, StageCloseColor, TextAlignmentOptions.Center);
            close.rectTransform.anchorMin = close.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            close.rectTransform.pivot = new Vector2(1f, 0.5f);
            close.rectTransform.anchoredPosition = new Vector2(-5.6f, 0f);
            close.rectTransform.sizeDelta = new Vector2(titleBarH, titleBarH);
            AddClick(close.gameObject, () => SetPage(Page.Main));

            var body = new GameObject("Body", typeof(RectTransform));
            var bodyRect = (RectTransform)body.transform;
            bodyRect.SetParent(rect, false);
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.pivot = new Vector2(0.5f, 0.5f);
            bodyRect.offsetMin = Vector2.zero;
            bodyRect.offsetMax = new Vector2(0f, -titleBarH);

            for (int i = 0; i < stageCount; i++)
            {
                int stageNumber = i + 1;
                var card = NewImage("Stage" + stageNumber, bodyRect, _roundFill, StageCardColor);
                card.type = Image.Type.Sliced;
                card.pixelsPerUnitMultiplier = 1f;
                var cardRect = card.rectTransform;
                cardRect.anchorMin = cardRect.anchorMax = new Vector2(0f, 1f);
                cardRect.pivot = new Vector2(0f, 1f);
                cardRect.anchoredPosition = new Vector2(padX + i * (cardW + gap), -33.6f);
                cardRect.sizeDelta = new Vector2(cardW, cardH);

                var highlight = NewImage("Highlight", cardRect, _roundEdge, AccentColor);
                highlight.type = Image.Type.Sliced;
                highlight.pixelsPerUnitMultiplier = 1f;
                highlight.raycastTarget = false;
                highlight.enabled = false;
                Stretch(highlight.rectTransform);

                Sprite thumbSprite = LoadStageThumb(stageNumber);
                GameObject thumbGo = null;
                if (thumbSprite != null)
                {
                    var thumb = NewImage("Thumb", cardRect, thumbSprite, Color.white);
                    thumb.preserveAspect = true;
                    thumb.raycastTarget = false;
                    var thumbRect = thumb.rectTransform;
                    thumbRect.anchorMin = new Vector2(0f, 1f);
                    thumbRect.anchorMax = new Vector2(1f, 1f);
                    thumbRect.pivot = new Vector2(0.5f, 1f);
                    thumbRect.anchoredPosition = new Vector2(0f, -11.2f);
                    thumbRect.sizeDelta = new Vector2(-22.4f, 120.4f);
                    thumbGo = thumb.gameObject;
                }

                var label = NewText("Label", cardRect, "LoadStage(" + stageNumber + ");", 36f, StageLabelColor, TextAlignmentOptions.Bottom);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                label.rectTransform.anchoredPosition = new Vector2(0f, -58.8f);
                label.rectTransform.sizeDelta = new Vector2(0f, -156.8f);
                label.raycastTarget = false;

                var row = new Row
                {
                    Rect = cardRect,
                    Highlight = highlight,
                    Label = label,
                    Card = card,
                    Thumb = thumbGo,
                    Submit = () => LoadSelectedStage(stageNumber)
                };
                _stageRows.Add(row);
                AddHover(card.gameObject, () => Select(_stageRows.IndexOf(row)), () => row.Submit?.Invoke());
            }

            var back = NewText("Back", rect, "ESC 뒤로", 28f, HintColor, TextAlignmentOptions.MidlineLeft);
            back.rectTransform.anchorMin = back.rectTransform.anchorMax = new Vector2(0f, 0f);
            back.rectTransform.pivot = new Vector2(0f, 0f);
            back.rectTransform.anchoredPosition = new Vector2(48f, 16f);
            back.rectTransform.sizeDelta = new Vector2(200f, 48f);
            AddClick(back.gameObject, () => SetPage(Page.Main));
        }

        private static Sprite LoadStageThumb(int stageNumber)
        {
            // Resources의 StageThumbnails 에셋이 섬네일을 직접 참조한다. 빌드에서도 확실히 불러와진다
            var fromLibrary = StageThumbnails.Get(stageNumber);
            if (fromLibrary != null) return fromLibrary;
            string spriteName = "Thumb_Stage" + stageNumber;
#if UNITY_EDITOR
            var fromAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Sprite/UI/" + spriteName + ".png");
            if (fromAsset != null) return fromAsset;
#endif
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite != null && sprite.name == spriteName) return sprite;
            }
            return null;
        }
        private void BuildSettingsPanel(Transform parent)
        {
            _settingsPanel = BuildPanel(parent, "SettingsPanel", new Vector2(1000f, 680f), "SETTINGS");
            string[] labels = { "배경음악", "효과음", "화면모드", "해상도" };
            for (int i = 0; i < labels.Length; i++)
            {
                var row = NewRow(_settingsPanel.transform, i, new Vector2(960f, 88f), -130f - i * 108f, _settingRows, false);
                row.Label = NewText("Label", row.Rect, labels[i], 40f, TextColor, TextAlignmentOptions.MidlineLeft);
                Place(row.Label.rectTransform, new Vector2(110f, 0f), new Vector2(260f, 70f));
            }

            // 볼륨: 좌우 키 5%씩, 마우스로 잡고 끌기. 움직이는 즉시 소리에 반영하고 저장한다
            _bgmSlider = new VolumeSlider(this, _settingRows[0].Rect, GameSettings.SetBgmVolume);
            _settingRows[0].Adjust = d => GameSettings.SetBgmVolume(GameSettings.BgmVolume + d * VolumeStep);
            // 효과음은 바꾸는 순간 고양이 울음소리로 바뀐 크기를 들려 준다
            _sfxSlider = new VolumeSlider(this, _settingRows[1].Rect, v => { GameSettings.SetSfxVolume(v); CatSfx.PlayPreview(); });
            _settingRows[1].Adjust = d => { GameSettings.SetSfxVolume(GameSettings.SfxVolume + d * VolumeStep); CatSfx.PlayPreview(); };
            GameSettings.VolumeChanged += RefreshSettingVisuals;

            // 화면모드: 두 버튼 중 하나. 좌우 키로 바꾸고 적용을 눌러야 실제로 바뀐다
            _fullButton = new ModeButton(this, _settingRows[2].Rect, "전체화면", new Vector2(400f, 0f), () => SetPendingFullscreen(true));
            _windowButton = new ModeButton(this, _settingRows[2].Rect, "창모드", new Vector2(640f, 0f), () => SetPendingFullscreen(false));
            _settingRows[2].Adjust = d => SetPendingFullscreen(d < 0);

            // 해상도: < 값 > 화살표. 좌우 키로 고르고 적용을 눌러야 바뀐다
            var resRow = _settingRows[3].Rect;
            var leftArrow = NewText("Prev", resRow, "<", 40f, TextColor, TextAlignmentOptions.Center);
            Place(leftArrow.rectTransform, new Vector2(400f, 0f), new Vector2(60f, 70f));
            AddClick(leftArrow.gameObject, () => { Select(3); ChangeResolution(-1); });
            _resolutionText = NewText("Value", resRow, "", 36f, TextColor, TextAlignmentOptions.Center);
            Place(_resolutionText.rectTransform, new Vector2(460f, 0f), new Vector2(300f, 70f));
            _resolutionText.raycastTarget = false;
            var rightArrow = NewText("Next", resRow, ">", 40f, TextColor, TextAlignmentOptions.Center);
            Place(rightArrow.rectTransform, new Vector2(760f, 0f), new Vector2(60f, 70f));
            AddClick(rightArrow.gameObject, () => { Select(3); ChangeResolution(1); });
            _settingRows[3].Adjust = ChangeResolution;
            if (ResolutionLocked)
            {
                // 화살표를 숨기고 줄을 흐리게 해서 바꿀 수 없다는 걸 보여 준다
                leftArrow.gameObject.SetActive(false);
                rightArrow.gameObject.SetActive(false);
                _settingRows[3].Label.color = HintColor;
                _resolutionText.color = HintColor;
            }

            // 아래쪽: 왼쪽 'ESC 뒤로'(클릭해도 뒤로), 오른쪽 '적용' 버튼(키보드로는 마지막 줄)
            var panelRect = (RectTransform)_settingsPanel.transform;
            var back = NewText("Back", panelRect, "ESC 뒤로", 28f, HintColor, TextAlignmentOptions.MidlineLeft);
            back.rectTransform.anchorMin = back.rectTransform.anchorMax = new Vector2(0f, 0f);
            back.rectTransform.pivot = new Vector2(0f, 0f);
            back.rectTransform.anchoredPosition = new Vector2(48f, 36f);
            back.rectTransform.sizeDelta = new Vector2(200f, 60f);
            AddClick(back.gameObject, BackFromSettings);

            _applyButton = NewImage("Apply", panelRect, _roundFill, AccentColor);
            _applyButton.type = Image.Type.Sliced;
            _applyButton.pixelsPerUnitMultiplier = 1f;
            var applyRect = _applyButton.rectTransform;
            applyRect.anchorMin = applyRect.anchorMax = new Vector2(1f, 0f);
            applyRect.pivot = new Vector2(1f, 0f);
            applyRect.anchoredPosition = new Vector2(-48f, 32f);
            applyRect.sizeDelta = new Vector2(212f, 68f);
            var applyText = NewText("Text", applyRect, "적용", 36f, PanelColor, TextAlignmentOptions.Center);
            Stretch(applyText.rectTransform);
            applyText.raycastTarget = false;
            var applyRow = new Row { Rect = applyRect, Submit = ApplyAndClose };
            _settingRows.Add(applyRow);
            AddHover(_applyButton.gameObject, () => Select(_settingRows.IndexOf(applyRow)), ApplyAndClose);
        }

        private GameObject BuildPanel(Transform parent, string name, Vector2 size, string title)
        {
            var panel = NewImage(name, parent, _roundFill, PanelColor);
            panel.type = Image.Type.Sliced;
            panel.pixelsPerUnitMultiplier = 0.6f;
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;

            var edge = NewImage("Edge", rect, _roundEdge, EdgeColor);
            edge.type = Image.Type.Sliced;
            edge.pixelsPerUnitMultiplier = 0.6f;
            edge.raycastTarget = false;
            Stretch(edge.rectTransform);

            var titleText = NewText("Title", rect, title, 30f, EdgeColor, TextAlignmentOptions.TopLeft);
            titleText.rectTransform.anchorMin = titleText.rectTransform.anchorMax = new Vector2(0f, 1f);
            titleText.rectTransform.pivot = new Vector2(0f, 1f);
            titleText.rectTransform.anchoredPosition = new Vector2(44f, -30f);
            titleText.rectTransform.sizeDelta = new Vector2(400f, 50f);
            titleText.raycastTarget = false;
            return panel.gameObject;
        }

        private Row NewRow(Transform panel, int index, Vector2 size, float y, List<Row> list, bool clickSubmits)
        {
            var go = new GameObject($"Row{index}", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = size;

            // 마우스가 올라가는 영역은 줄 전체. 선택되지 않았을 때는 투명이라 안 보인다
            var hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            var highlight = NewImage("Highlight", rect, _roundFill, RowColor);
            highlight.type = Image.Type.Sliced;
            highlight.pixelsPerUnitMultiplier = 1f;
            highlight.raycastTarget = false;
            Stretch(highlight.rectTransform);

            var row = new Row { Rect = rect, Highlight = highlight };
            list.Add(row);
            // 설정 줄은 빈 곳을 눌러도 적용되면 헷갈리니, 클릭은 1단 메뉴 줄에서만 실행으로 받는다
            AddHover(go, () => Select(list.IndexOf(row)), clickSubmits ? () => row.Submit?.Invoke() : (Action)(() => Select(list.IndexOf(row))));
            return row;
        }

        // ---------------- 작은 부품 ----------------

        // 가로 막대 + 채움 + 손잡이 + 숫자. 막대를 누르거나 끌면 그 자리 값으로 바뀐다
        private sealed class VolumeSlider
        {
            private const float Width = 400f;
            private readonly RectTransform _fill;
            private readonly Image _fillImage;
            private readonly RectTransform _knob;
            private readonly TextMeshProUGUI _value;

            public VolumeSlider(PauseMenu menu, RectTransform row, Action<float> onDrag)
            {
                var track = NewImage("Track", row, menu._roundFill, TrackColor);
                track.type = Image.Type.Sliced;
                track.pixelsPerUnitMultiplier = 4f;
                Place(track.rectTransform, new Vector2(400f, 0f), new Vector2(Width, 8f));
                track.raycastTarget = false;

                _fillImage = NewImage("Fill", track.rectTransform, menu._roundFill, EdgeColor);
                _fillImage.type = Image.Type.Sliced;
                _fillImage.pixelsPerUnitMultiplier = 4f;
                _fillImage.raycastTarget = false;
                _fill = _fillImage.rectTransform;
                _fill.anchorMin = new Vector2(0f, 0f);
                _fill.anchorMax = new Vector2(0f, 1f);
                _fill.pivot = new Vector2(0f, 0.5f);
                _fill.anchoredPosition = Vector2.zero;

                var knob = NewImage("Knob", track.rectTransform, menu._circle, KnobColor);
                knob.raycastTarget = false;
                _knob = knob.rectTransform;
                _knob.anchorMin = _knob.anchorMax = new Vector2(0f, 0.5f);
                _knob.pivot = new Vector2(0.5f, 0.5f);
                _knob.sizeDelta = new Vector2(34f, 34f);

                _value = menu.NewText("Value", row, "", 36f, TextColor, TextAlignmentOptions.MidlineLeft);
                Place(_value.rectTransform, new Vector2(830f, 0f), new Vector2(100f, 70f));
                _value.raycastTarget = false;

                // 손잡이가 작아서 잡기 쉽게 막대보다 위아래로 넉넉한 투명 영역을 둔다
                var hit = NewImage("Hit", row, null, new Color(0f, 0f, 0f, 0f));
                Place(hit.rectTransform, new Vector2(384f, 0f), new Vector2(Width + 32f, 60f));
                var drag = hit.gameObject.AddComponent<DragArea>();
                drag.OnValue = screen =>
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(track.rectTransform, screen, null, out var local);
                    // 막대는 Place로 왼쪽 끝이 기준점(pivot x=0)이라 local.x가 곧 왼쪽 끝에서 떨어진 거리다. 반 폭을 더하면 누른 곳보다 반 칸 오른쪽 값이 된다
                    onDrag(Mathf.Clamp01(local.x / Width));
                };
            }

            public void Refresh(float value, bool selected)
            {
                _fill.sizeDelta = new Vector2(Width * value, 0f);
                _fillImage.color = selected ? AccentColor : EdgeColor;
                _knob.anchoredPosition = new Vector2(Width * value, 0f);
                _value.text = Mathf.RoundToInt(value * 100f).ToString();
            }
        }

        // 전체화면 / 창모드 버튼. 고른 쪽은 꽉 찬 색, 아닌 쪽은 테두리만
        private sealed class ModeButton
        {
            private readonly Image _fill;
            private readonly TextMeshProUGUI _text;

            public ModeButton(PauseMenu menu, RectTransform row, string label, Vector2 pos, Action onClick)
            {
                _fill = NewImage(label, row, menu._roundFill, EdgeColor);
                _fill.type = Image.Type.Sliced;
                _fill.pixelsPerUnitMultiplier = 1.4f;
                Place(_fill.rectTransform, pos, new Vector2(220f, 64f));

                var edge = NewImage("Edge", _fill.rectTransform, menu._roundEdge, EdgeColor);
                edge.type = Image.Type.Sliced;
                edge.pixelsPerUnitMultiplier = 1.4f;
                edge.raycastTarget = false;
                Stretch(edge.rectTransform);

                _text = menu.NewText("Text", _fill.rectTransform, label, 34f, PanelColor, TextAlignmentOptions.Center);
                Stretch(_text.rectTransform);
                _text.raycastTarget = false;
                AddClick(_fill.gameObject, () => { menu.Select(2); onClick(); });
            }

            public void Refresh(bool on)
            {
                _fill.color = on ? EdgeColor : new Color(0f, 0f, 0f, 0f);
                _text.color = on ? PanelColor : EdgeColor;
            }
        }

        private sealed class PointerArea : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
        {
            public Action Enter;
            public Action Click;
            public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
            public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Click?.Invoke(); }
        }

        private sealed class DragArea : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public Action<Vector2> OnValue;
            public void OnPointerDown(PointerEventData e) => OnValue?.Invoke(e.position);
            public void OnDrag(PointerEventData e) => OnValue?.Invoke(e.position);
        }

        private static void AddHover(GameObject go, Action enter, Action click)
        {
            var area = go.AddComponent<PointerArea>();
            area.Enter = enter;
            area.Click = click;
        }

        private static void AddClick(GameObject go, Action click) => AddHover(go, null, click);

        // ---------------- 도우미 ----------------

        private TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return image;
        }

        // 줄 왼쪽 끝에서 x만큼 떨어진 곳에, 세로 가운데 맞춰 놓는다
        private static void Place(RectTransform rect, Vector2 leftOffset, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = leftOffset;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // 기획서와 같은 D2Coding을 쓰고 싶지만 Resources에 없어서, 이미 불러와진 게 있으면 쓰고 없으면 TMP 기본 글꼴(한글 됨)을 쓴다
        private static TMP_FontAsset FindFont()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font != null && font.name.Contains("D2Coding")) return font;
            }
            return TMP_Settings.defaultFontAsset;
        }

        // 자동 분할된 PlayerCat 시트의 0, 1번(가만히 있기) 칸을 커서로 쓴다
        private static Sprite[] LoadCatFrames()
        {
            var all = SpriteSheetLibrary.GetFrames(CatSheet) ?? Array.Empty<Sprite>();
            var frames = new List<Sprite>();
            foreach (string n in new[] { $"{CatSheet}_0", $"{CatSheet}_1" })
            {
                foreach (var s in all) if (s.name == n) { frames.Add(s); break; }
            }
            if (frames.Count == 0 && all.Length > 0) frames.Add(all[0]);
            return frames.ToArray();
        }

        // 둥근 사각형(또는 원) 스프라이트를 코드로 만든다. thickness가 0이면 꽉 찬 모양, 아니면 그 두께의 테두리만
        private static Sprite MakeRoundSprite(int size, int radius, int thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = RoundRectDistance(x + 0.5f, y + 0.5f, size, radius);
                    float outer = Mathf.Clamp01(0.5f - d);
                    float a = thickness > 0 ? outer * Mathf.Clamp01(d + thickness + 0.5f) : outer;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            float border = Mathf.Min(radius, size / 2 - 1);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        // 둥근 사각형 가장자리까지의 거리. 안쪽은 음수
        private static float RoundRectDistance(float x, float y, int size, int radius)
        {
            float half = size * 0.5f;
            float qx = Mathf.Abs(x - half) - (half - radius);
            float qy = Mathf.Abs(y - half) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        // 스테이지 씬에 EventSystem이 없으면 마우스가 안 먹으니, 그 씬에 하나 만들어 준다 (씬이 바뀌면 같이 사라짐)
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
