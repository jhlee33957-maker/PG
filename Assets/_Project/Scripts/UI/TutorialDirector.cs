using System.Collections;
using System.Collections.Generic;
using CoffeeSpec.Core;
using CoffeeSpec.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// Stage1 첫 플레이 튜토리얼. 기획서 다루는 법을 한 단계씩 직접 해 보게 한다.
    ///
    /// 연결 방식
    ///   - 씬이나 프리팹에 붙일 필요 없다. 씬이 불릴 때마다(sceneLoaded) Stage1이고 세이브의 TutorialDone이 1이 아니면 스스로 만들어진다
    ///   - 화면(가림막, 안내 상자)도 실행 중에 코드로 만든다
    ///   - SpecPanel의 이벤트(OptionAssigned, StageViewChanged, WordsReset, Closed, RetryRequested)로 통과를 판단하고,
    ///     ActionGate·PlacementFilter로 지금 단계에서 할 일 말고는 버튼·키·단어 놓기를 막는다
    ///
    /// 단계 (스킵 없음, 끝까지 하면 TutorialDone=1 저장)
    ///   1~2. 시작할 때 리셋 상태(고정 단어 말고 모두 단어 바로)로 만든 뒤, 처음 채워져 있던 칸을 위 줄부터 왼쪽부터 하나씩 끌어 넣기
    ///   3. 칸 색깔 설명 (확인)                  4. 고정 단어 설명 (확인)
    ///   5. PreView 누르기 → Return으로 돌아오기  6. Reset 누르기 (칸이 빈 채로 확인 → 원래 단어로 되돌림)
    ///   7. Build                                8. 게임 화면에서 R (기획서 다시 열기)
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        private const int TutorialStageNumber = 1;
        // 단어 층(80)보다 위, 끌고 있는 칩(120)보다 아래. 끄는 단어는 가림막 위에 보인다
        private const int SortingOrder = 110;

        private const string TextDragFirst = "단어를 드래그해서 문장에 넣어보자.\n넣은 문장대로 맵이 바뀌어.";
        // 두 번째부터의 끌기 안내: "이번엔 {단어 칩 이름}을/를 {칸 색} 칸에 넣어보자."
        private const string TextDragFollowFormat = "이번엔 {0}{1} {2} 칸에 넣어보자.";
        private const string TextColors = "파랑 칸엔 명사, 빨강 칸엔 동사,\n보라 칸엔 명사와 동사 둘 다 들어가.";
        private const string TextLocked = "흐리게 표시된 단어는 고정돼 있어서 바꿀 수 없어.";
        private const string TextPreview = "적용하기 전에 결과를 미리 볼 수 있어.";
        private const string TextPreviewReturn = "다 봤으면 Return (R)을 눌러 기획서로 돌아가.";
        private const string TextReset = "처음 상태로 되돌리고 싶으면 누르면 돼.";
        private const string TextBuild = "다 됐으면 Build로 맵에 적용해.";
        private const string TextRetry = "R을 누르면 기획서를 다시 열 수 있어.";
        private const string TextConfirm = "확인";


        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color LightDimColor = new Color(0f, 0f, 0f, 0.38f);   // 스테이지를 봐야 하는 단계
        private static readonly Color FrameColor = new Color(0.91f, 0.57f, 0.23f, 1f);  // 기획서 강조색(#E8913B)과 같게
        private static readonly Color BoxColor = new Color(0.118f, 0.133f, 0.169f, 0.97f);
        private static readonly Color BoxTextColor = new Color(0.9f, 0.92f, 0.95f, 1f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.627f, 0.263f, 1f); // 빌드 버튼 초록(#2EA043)
        private static readonly Color KeyColor = new Color(0.2f, 0.22f, 0.27f, 1f);
        private static readonly Color KeyPressedColor = new Color(0.91f, 0.57f, 0.23f, 0.55f);

        private const float HolePadding = 6f;
        // 위아래 여백은 작게 둔다. 크면 이웃 줄 빈칸의 강조 테두리끼리 겹친다
        private const float HolePaddingY = 3f;
        private const float BoxGap = 22f;
        private const float BoxPadding = 22f;
        private const float BoxMaxTextWidth = 720f;
        private const float FontSize = 30f;
        private const float KeySize = 58f;
        // 다시 채운 단어의 타이핑 연출(글자당 0.05초)이 끝날 만큼 기다린 뒤 Build 단계로 간다
        private const float ResetAfterRestoreSeconds = 0.5f;

        private enum Step
        {
            None,
            Drag,             // _dragTasks[_dragIndex]를 끌어 넣는 중
            Colors,
            Locked,
            Preview,
            PreviewReturn,
            Reset,
            ResetConfirm,     // 비워진 채로 확인을 기다림
            ResetRestoring,   // 확인 뒤 다시 채우는 중
            Build,
            Retry,
            Done
        }

        private SpecPanel _panel;
        private Step _step = Step.None;
        // 끌어 넣어야 하는 칸, 단어, 안내 문구 (시작할 때 채워져 있던 고정 아닌 칸, 위 줄부터 왼쪽부터)
        private readonly List<(int Blank, WordId Word, string Text)> _dragTasks = new List<(int Blank, WordId Word, string Text)>();
        private int _dragIndex;

        /// <summary>R 안내 단계 동안 true. 캐릭터 조작(이동·점프)을 막는다 (PlatformerPlayer가 본다).</summary>
        public static bool BlocksPlayerInput { get; private set; }
        private int[] _snapshot;         // 리셋 전 빈칸 상태

        // 화면
        private RectTransform _canvasRect;
        private TutorialDimOverlay _dim;
        private RectTransform _frameLayer;
        private readonly List<UILineBorder> _frames = new List<UILineBorder>();
        private RectTransform _box;
        private TextMeshProUGUI _boxText;
        private RectTransform _key;
        private Image _keyFill;
        private RectTransform _confirmButton;
        private TMP_FontAsset _font;

        private readonly List<Rect> _screenHoles = new List<Rect>();
        private readonly List<Rect> _localHoles = new List<Rect>();

        // ---------------- 자동 생성 ----------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            BlocksPlayerInput = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!StageScenes.TryGetStageNumber(scene.name, out int stage) || stage != TutorialStageNumber)
            {
                return;
            }
            if (SaveDataStore.IsTutorialDone())
            {
                return;
            }

            var go = new GameObject("TutorialDirector");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<TutorialDirector>();
        }

        // ---------------- 생명주기 ----------------

        private void Start()
        {
            StartCoroutine(Begin());
        }

        private IEnumerator Begin()
        {
            // SpecPanel이 Start에서 기획서를 열 때까지 기다린다 (기획서가 열린 동안은 timeScale 0이라 프레임 단위로 기다린다)
            float waited = 0f;
            while (_panel == null)
            {
                _panel = FindAnyObjectByType<SpecPanel>();
                if (_panel != null)
                {
                    break;
                }
                waited += Time.unscaledDeltaTime;
                if (waited > 5f)
                {
                    Debug.LogWarning("[TutorialDirector] SpecPanel이 없어 튜토리얼을 시작하지 않습니다.", this);
                    Destroy(gameObject);
                    yield break;
                }
                yield return null;
            }

            while (_panel != null && (!_panel.IsOpen || _panel.IsViewingStage || _panel.SentenceCount == 0))
            {
                yield return null;
            }
            // 기획서 글자와 단어 바 배치가 한 번 끝난 뒤에 위치를 읽는다
            yield return null;
            yield return null;

            if (_panel == null || !ResolveTargets())
            {
                Destroy(gameObject);
                yield break;
            }

            BuildUI();
            _panel.ActionGate = AllowAction;
            _panel.PlacementFilter = AllowPlacement;
            _panel.OptionAssigned += OnOptionAssigned;
            _panel.StageViewChanged += OnStageViewChanged;
            _panel.WordsReset += OnWordsReset;
            _panel.Closed += OnPanelClosed;
            _panel.RetryRequested += OnRetryRequested;

            _dragIndex = 0;
            EnterStep(Step.Drag);
        }

        private void OnDestroy()
        {
            BlocksPlayerInput = false;
            Unhook();
            if (_canvasRect != null)
            {
                Destroy(_canvasRect.gameObject);
            }
        }

        private void Unhook()
        {
            if (_panel == null)
            {
                return;
            }
            if (_panel.ActionGate == (System.Func<SpecPanel.GuardedAction, bool>)AllowAction) _panel.ActionGate = null;
            if (_panel.PlacementFilter == (System.Func<int, int, bool>)AllowPlacement) _panel.PlacementFilter = null;
            _panel.OptionAssigned -= OnOptionAssigned;
            _panel.StageViewChanged -= OnStageViewChanged;
            _panel.WordsReset -= OnWordsReset;
            _panel.Closed -= OnPanelClosed;
            _panel.RetryRequested -= OnRetryRequested;
        }

        /// <summary>
        /// 시작할 때 고정 아닌 칸에 들어 있던 단어를 끌기 단계로 정해 두고(위 줄부터, 왼쪽부터),
        /// 리셋 버튼과 같은 상태(고정 단어 말고 모두 단어 바로)로 만든다. 플레이어가 그 단어들을 다시 채운다.
        /// </summary>
        private bool ResolveTargets()
        {
            int[] start = _panel.SnapshotAssignments();
            if (start == null)
            {
                return false;
            }

            IReadOnlyList<WordId> pool = _panel.WordPool;
            _dragTasks.Clear();
            for (int s = 0; s < _panel.SentenceCount; s++)
            {
                if (_panel.IsSentenceHidden(s))
                {
                    continue;
                }
                for (int slot = 0; ; slot++)
                {
                    int blank = _panel.GetBlankIndex(s, slot);
                    if (blank < 0)
                    {
                        break;
                    }
                    int option = start[blank];
                    if (_panel.IsBlankLocked(blank) || option < 0 || option >= pool.Count)
                    {
                        continue;
                    }
                    string text = _dragTasks.Count == 0 ? TextDragFirst : FollowText(pool[option], blank);
                    _dragTasks.Add((blank, pool[option], text));
                }
            }

            // 리셋 상태로: 고정 아닌 칸을 모두 비운다 (RestoreAssignments가 빠진 단어를 단어 바로 돌려보낸다)
            var cleared = (int[])start.Clone();
            for (int blank = 0; blank < cleared.Length; blank++)
            {
                if (!_panel.IsBlankLocked(blank))
                {
                    cleared[blank] = -1;
                }
            }
            _panel.RestoreAssignments(cleared);
            return true;
        }

        // "이번엔 가시를 파랑 칸에 넣어보자." 단어는 단어 칩에 보이는 이름, 조사는 받침에 맞춘다
        private string FollowText(WordId word, int blank)
        {
            string name = WordTags.DisplayName(word);
            string josa = WordTags.EndsWithFinalConsonant(name) ? "을" : "를";
            IReadOnlyList<WordType> types = _panel.GetSlotTypes(blank);
            string color = IsNounOrVerb(types) ? "보라" : IsVerbOnly(types) ? "빨강" : "파랑";
            return string.Format(TextDragFollowFormat, name, josa, color);
        }

        private static bool IsVerbOnly(IReadOnlyList<WordType> types)
        {
            if (types == null || types.Count == 0) return false;
            foreach (WordType type in types)
            {
                if (type != WordType.Verb) return false;
            }
            return true;
        }

        private static bool IsNounOnly(IReadOnlyList<WordType> types)
        {
            if (types == null || types.Count == 0) return false;
            foreach (WordType type in types)
            {
                if (type != WordType.Noun) return false;
            }
            return true;
        }

        private static bool IsNounOrVerb(IReadOnlyList<WordType> types)
        {
            if (types == null) return false;
            bool noun = false, verb = false;
            foreach (WordType type in types)
            {
                if (type == WordType.Noun) noun = true;
                else if (type == WordType.Verb) verb = true;
            }
            return noun && verb;
        }

        private bool IsWordInBlank(WordId word, int blank)
        {
            int option = _panel.GetOptionInBlank(blank);
            return option >= 0 && option < _panel.WordPool.Count && _panel.WordPool[option] == word;
        }

        // ---------------- 단계 진행 ----------------

        private void EnterStep(Step step)
        {
            _step = step;

            // 끌 단어가 남지 않았으면 다음 단계로, 이미 원하는 자리에 들어가 있는 칸은 건너뛴다
            if (step == Step.Drag)
            {
                if (_dragIndex >= _dragTasks.Count)
                {
                    EnterStep(Step.Colors);
                    return;
                }
                if (IsWordInBlank(_dragTasks[_dragIndex].Word, _dragTasks[_dragIndex].Blank))
                {
                    _dragIndex++;
                    EnterStep(Step.Drag);
                    return;
                }
            }

            if (step == Step.Reset)
            {
                _snapshot = _panel.SnapshotAssignments();
            }

            bool lightDim = step == Step.PreviewReturn || step == Step.Retry;
            _dim.color = lightDim ? LightDimColor : DimColor;
            // 미리보기·플레이 중에는 눌러서 될 것이 해당 버튼뿐이고, 화면을 끌어 둘러볼 수 있어야 해서 입력을 막지 않는다
            // R 안내 단계는 화면은 옅게 두되 클릭과 캐릭터 조작을 막는다 (R키와 강조한 Retry 버튼만 된다)
            _dim.BlocksInput = !lightDim || step == Step.Retry;
            BlocksPlayerInput = step == Step.Retry;

            switch (step)
            {
                case Step.Drag: ShowBox(_dragTasks[_dragIndex].Text, false, false); break;
                case Step.Colors: ShowBox(TextColors, true, false); break;
                case Step.Locked: ShowBox(TextLocked, true, false); break;
                case Step.Preview: ShowBox(TextPreview, false, false); break;
                case Step.PreviewReturn: ShowBox(TextPreviewReturn, false, false); break;
                case Step.Reset: ShowBox(TextReset, false, false); break;
                case Step.ResetConfirm: ShowBox(TextReset, true, false); break;    // 비워진 칸을 보여 주며 확인을 기다린다
                case Step.ResetRestoring: _box.gameObject.SetActive(false); break; // 다시 채우는 동안은 강조·안내 없이 화면 전체를 어둡게
                case Step.Build: ShowBox(TextBuild, false, false); break;
                case Step.Retry: ShowBox(TextRetry, false, true); break;
            }
            UpdateLayout();
        }

        private void OnConfirmClicked()
        {
            if (_step == Step.Colors)
            {
                EnterStep(Step.Locked);
            }
            else if (_step == Step.Locked)
            {
                EnterStep(Step.Preview);
            }
            else if (_step == Step.ResetConfirm)
            {
                StartCoroutine(RestoreAfterConfirm());
            }
        }

        private void OnOptionAssigned(int blank, int option)
        {
            if (_step == Step.Drag && _dragIndex < _dragTasks.Count
                && blank == _dragTasks[_dragIndex].Blank && IsWordInBlank(_dragTasks[_dragIndex].Word, blank))
            {
                _dragIndex++;
                EnterStep(Step.Drag);
            }
        }

        private void OnStageViewChanged(bool viewing)
        {
            if (_step == Step.Preview && viewing)
            {
                EnterStep(Step.PreviewReturn);
            }
            else if (_step == Step.PreviewReturn && !viewing)
            {
                EnterStep(Step.Reset);
            }
        }

        private void OnWordsReset()
        {
            if (_step == Step.Reset)
            {
                // 칸은 빈 채로 두고, 확인을 누르면 다시 채운다
                EnterStep(Step.ResetConfirm);
            }
        }

        // 확인을 누르면 리셋 전 단어(비둘기, 가시 등)를 다시 채우고, 타이핑이 끝나면 Build 단계로 간다
        private IEnumerator RestoreAfterConfirm()
        {
            EnterStep(Step.ResetRestoring);
            if (_panel == null)
            {
                yield break;
            }
            _panel.RestoreAssignments(_snapshot);
            yield return new WaitForSecondsRealtime(ResetAfterRestoreSeconds);
            EnterStep(Step.Build);
        }

        private void OnPanelClosed()
        {
            if (_step == Step.Build)
            {
                EnterStep(Step.Retry);
            }
        }

        private void OnRetryRequested()
        {
            if (_step == Step.Retry)
            {
                Finish();
            }
        }

        private void Finish()
        {
            _step = Step.Done;
            BlocksPlayerInput = false;
            SaveDataStore.RecordTutorialDone();
            Unhook();
            Destroy(gameObject);   // OnDestroy에서 화면도 지운다
        }

        // ---------------- 막기 ----------------

        private bool AllowAction(SpecPanel.GuardedAction action)
        {
            switch (_step)
            {
                case Step.Preview: return action == SpecPanel.GuardedAction.Preview;
                case Step.PreviewReturn: return action == SpecPanel.GuardedAction.Return;
                case Step.Reset: return action == SpecPanel.GuardedAction.Reset;
                case Step.Build: return action == SpecPanel.GuardedAction.Build;
                case Step.Retry: return action == SpecPanel.GuardedAction.Retry;
                case Step.Done: return true;
                default: return false;
            }
        }

        private bool AllowPlacement(int option, int targetBlank)
        {
            IReadOnlyList<WordId> pool = _panel.WordPool;
            if (option < 0 || option >= pool.Count)
            {
                return false;
            }
            switch (_step)
            {
                case Step.Drag:
                    return _dragIndex < _dragTasks.Count
                        && targetBlank == _dragTasks[_dragIndex].Blank && pool[option] == _dragTasks[_dragIndex].Word;
                case Step.Done: return true;
                default: return false;
            }
        }

        // ---------------- 강조 위치 ----------------

        private void LateUpdate()
        {
            if (_canvasRect == null || _panel == null || _step == Step.None || _step == Step.Done)
            {
                return;
            }
            UpdateKey();
            UpdateLayout();
        }

        private void CollectHoles(List<Rect> holes)
        {
            holes.Clear();
            Rect rect;
            switch (_step)
            {
                // 끌기 단계는 빈칸을 먼저 넣는다. 안내 상자는 첫 구멍(빈칸) 옆에 둔다
                case Step.Drag:
                    if (_dragIndex < _dragTasks.Count)
                    {
                        if (_panel.TryGetBlankScreenRect(_dragTasks[_dragIndex].Blank, out rect)) holes.Add(rect);
                        if (TryGetWordRect(_dragTasks[_dragIndex].Word, _dragTasks[_dragIndex].Blank, out rect)) holes.Add(rect);
                    }
                    break;
                case Step.Colors:
                case Step.ResetConfirm:
                    if (_panel.TryGetSentenceLinesScreenRect(out rect)) holes.Add(rect);
                    break;
                case Step.Locked:
                    for (int s = 0; s < _panel.SentenceCount; s++)
                    {
                        if (_panel.IsSentenceHidden(s)) continue;
                        for (int slot = 0; ; slot++)
                        {
                            int blank = _panel.GetBlankIndex(s, slot);
                            if (blank < 0) break;
                            if (_panel.IsBlankLocked(blank) && _panel.GetOptionInBlank(blank) >= 0
                                && _panel.TryGetBlankScreenRect(blank, out rect))
                            {
                                holes.Add(rect);
                            }
                        }
                    }
                    break;
                case Step.Preview:
                case Step.PreviewReturn:
                    if (_panel.TryGetButtonScreenRect(SpecPanel.PanelButton.Preview, out rect)) holes.Add(rect);
                    break;
                case Step.Reset:
                    if (_panel.TryGetButtonScreenRect(SpecPanel.PanelButton.Reset, out rect)) holes.Add(rect);
                    break;
                case Step.Build:
                    if (_panel.TryGetButtonScreenRect(SpecPanel.PanelButton.Build, out rect)) holes.Add(rect);
                    break;
                case Step.Retry:
                    if (_panel.TryGetButtonScreenRect(SpecPanel.PanelButton.Retry, out rect)) holes.Add(rect);
                    break;
            }
        }

        // 옮길 수 있는 그 단어의 위치. 단어 바·화면 위 칩을 먼저, 없으면 그 단어가 들어 있는 빈칸 (목표 칸은 뺀다)
        private bool TryGetWordRect(WordId word, int targetBlank, out Rect rect)
        {
            rect = default;
            bool found = false;
            IReadOnlyList<WordId> pool = _panel.WordPool;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != word)
                {
                    continue;
                }
                int blank = _panel.GetBlankOfOption(i);
                if (blank < 0)
                {
                    if (_panel.TryGetOptionScreenRect(i, out rect))
                    {
                        return true;
                    }
                }
                else if (!found && blank != targetBlank && !_panel.IsBlankLocked(blank)
                         && _panel.TryGetBlankScreenRect(blank, out Rect blankRect))
                {
                    rect = blankRect;
                    found = true;
                }
            }
            return found;
        }

        private void UpdateLayout()
        {
            CollectHoles(_screenHoles);

            _localHoles.Clear();
            foreach (Rect screen in _screenHoles)
            {
                if (TryScreenToLocal(screen, out Rect local))
                {
                    _localHoles.Add(Rect.MinMaxRect(local.xMin - HolePadding, local.yMin - HolePaddingY,
                                                    local.xMax + HolePadding, local.yMax + HolePaddingY));
                }
            }
            _dim.SetHoles(_localHoles);
            LayoutFrames();
            LayoutBox();
        }

        private bool TryScreenToLocal(Rect screen, out Rect local)
        {
            local = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen.min, null, out Vector2 a)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen.max, null, out Vector2 b))
            {
                return false;
            }
            local = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return true;
        }

        private void LayoutFrames()
        {
            while (_frames.Count < _localHoles.Count)
            {
                var go = new GameObject($"Frame_{_frames.Count}", typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(_frameLayer, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = Vector2.zero;
                var border = go.AddComponent<UILineBorder>();
                border.color = FrameColor;
                border.raycastTarget = false;
                _frames.Add(border);
            }
            for (int i = 0; i < _frames.Count; i++)
            {
                bool used = i < _localHoles.Count;
                _frames[i].gameObject.SetActive(used);
                if (!used) continue;
                RectTransform rect = _frames[i].rectTransform;
                rect.anchoredPosition = _localHoles[i].min;
                rect.sizeDelta = _localHoles[i].size;
            }
        }

        // 강조한 곳 아래(자리가 없으면 위)에 안내 상자를 둔다. 화면 밖으로 나가지 않게 맞춘다
        private void LayoutBox()
        {
            if (!_box.gameObject.activeSelf)
            {
                return;
            }

            Rect area = _canvasRect.rect;
            Vector2 size = _box.sizeDelta;
            const float margin = 16f;

            Vector2 center;
            if (_localHoles.Count == 0)
            {
                center = area.center;
            }
            else
            {
                // 끌기 단계는 빈칸(첫 구멍)만 기준으로, 나머지는 구멍 전체를 감싼 범위를 기준으로 한다
                bool anchorFirst = _step == Step.Drag;
                Rect union = _localHoles[0];
                for (int i = 1; i < _localHoles.Count && !anchorFirst; i++)
                {
                    union = Rect.MinMaxRect(Mathf.Min(union.xMin, _localHoles[i].xMin), Mathf.Min(union.yMin, _localHoles[i].yMin),
                                            Mathf.Max(union.xMax, _localHoles[i].xMax), Mathf.Max(union.yMax, _localHoles[i].yMax));
                }

                float belowY = union.yMin - BoxGap - size.y * 0.5f;
                float aboveY = union.yMax + BoxGap + size.y * 0.5f;
                if (belowY - size.y * 0.5f >= area.yMin + margin)
                {
                    center = new Vector2(union.center.x, belowY);
                }
                else if (aboveY + size.y * 0.5f <= area.yMax - margin)
                {
                    center = new Vector2(union.center.x, aboveY);
                }
                else
                {
                    center = new Vector2(union.center.x, area.center.y);
                }
            }

            center.x = Mathf.Clamp(center.x, area.xMin + margin + size.x * 0.5f, Mathf.Max(area.xMin + margin + size.x * 0.5f, area.xMax - margin - size.x * 0.5f));
            center.y = Mathf.Clamp(center.y, area.yMin + margin + size.y * 0.5f, Mathf.Max(area.yMin + margin + size.y * 0.5f, area.yMax - margin - size.y * 0.5f));
            _box.anchoredPosition = center;
        }

        // R을 누르고 있는 동안 키 그림을 눌린 색으로
        private void UpdateKey()
        {
            if (_key == null || !_key.gameObject.activeSelf)
            {
                return;
            }
            bool pressed = Keyboard.current != null && Keyboard.current.rKey.isPressed;
            _keyFill.color = pressed ? KeyPressedColor : KeyColor;
        }

        // ---------------- 화면 만들기 ----------------

        private void BuildUI()
        {
            _font = FindFont();

            var canvasGo = new GameObject("TutorialCanvas", typeof(RectTransform));
            // 이 오브젝트와 같은 씬에 두어 씬이 바뀌면 같이 사라지게 한다
            SceneManager.MoveGameObjectToScene(canvasGo, gameObject.scene);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0f;   // 기획서 화면(SpecUI)과 같은 기준
            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = (RectTransform)canvasGo.transform;

            var dimGo = new GameObject("Dim", typeof(RectTransform));
            dimGo.transform.SetParent(_canvasRect, false);
            Stretch((RectTransform)dimGo.transform);
            _dim = dimGo.AddComponent<TutorialDimOverlay>();
            _dim.color = DimColor;
            _dim.raycastTarget = true;

            var frameGo = new GameObject("Frames", typeof(RectTransform));
            frameGo.transform.SetParent(_canvasRect, false);
            _frameLayer = (RectTransform)frameGo.transform;
            Stretch(_frameLayer);

            BuildBox();
        }

        private void BuildBox()
        {
            var boxGo = new GameObject("GuideBox", typeof(RectTransform));
            boxGo.transform.SetParent(_canvasRect, false);
            _box = (RectTransform)boxGo.transform;
            _box.anchorMin = _box.anchorMax = _box.pivot = new Vector2(0.5f, 0.5f);
            var background = boxGo.AddComponent<Image>();
            background.color = BoxColor;
            background.raycastTarget = true;   // 상자 뒤로 클릭이 새지 않게

            var border = CreateChild(_box, "Border").gameObject.AddComponent<UILineBorder>();
            Stretch(border.rectTransform);
            border.color = FrameColor;
            border.raycastTarget = false;

            // R 키 그림 (8단계에서만)
            _key = CreateChild(_box, "KeyR");
            _key.anchorMin = _key.anchorMax = new Vector2(0f, 0.5f);
            _key.pivot = new Vector2(0f, 0.5f);
            _key.sizeDelta = new Vector2(KeySize, KeySize);
            _keyFill = _key.gameObject.AddComponent<Image>();
            _keyFill.color = KeyColor;
            _keyFill.raycastTarget = false;
            var keyBorder = CreateChild(_key, "Border").gameObject.AddComponent<UILineBorder>();
            Stretch(keyBorder.rectTransform);
            keyBorder.color = BoxTextColor;
            keyBorder.raycastTarget = false;
            TextMeshProUGUI keyLabel = CreateText(_key, "Label", "R", FontSize);
            Stretch(keyLabel.rectTransform);
            keyLabel.alignment = TextAlignmentOptions.Midline;
            keyLabel.fontStyle = FontStyles.Bold;

            _boxText = CreateText(_box, "Text", string.Empty, FontSize);
            _boxText.rectTransform.anchorMin = _boxText.rectTransform.anchorMax = new Vector2(0f, 1f);
            _boxText.rectTransform.pivot = new Vector2(0f, 1f);
            // D2Coding은 위쪽 여유(ascender)가 커서 줄 높이 기준으로 두면 글자가 아래로 처져 보인다.
            // Midline은 실제 글자 모양의 위아래 끝을 기준으로 가운데에 놓는다
            _boxText.alignment = TextAlignmentOptions.MidlineLeft;
            _boxText.textWrappingMode = TextWrappingModes.Normal;

            // 확인 버튼 (3, 4단계에서만)
            _confirmButton = CreateChild(_box, "BTN_Confirm");
            _confirmButton.anchorMin = _confirmButton.anchorMax = _confirmButton.pivot = new Vector2(1f, 0f);
            _confirmButton.sizeDelta = new Vector2(132f, 50f);
            var buttonImage = _confirmButton.gameObject.AddComponent<Image>();
            buttonImage.color = ButtonColor;
            var button = _confirmButton.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(OnConfirmClicked);
            TextMeshProUGUI buttonLabel = CreateText(_confirmButton, "Label", TextConfirm, FontSize * 0.85f);
            Stretch(buttonLabel.rectTransform);
            buttonLabel.alignment = TextAlignmentOptions.Midline;
            buttonLabel.color = Color.white;
        }

        // 안내 상자 내용과 크기를 맞춘다
        private void ShowBox(string text, bool withConfirm, bool withKey)
        {
            _box.gameObject.SetActive(true);
            _key.gameObject.SetActive(withKey);
            _confirmButton.gameObject.SetActive(withConfirm);
            _boxText.text = text;

            float left = BoxPadding + (withKey ? KeySize + 18f : 0f);
            Vector2 textSize = _boxText.GetPreferredValues(text, BoxMaxTextWidth, 0f);
            float textWidth = Mathf.Min(BoxMaxTextWidth, Mathf.Ceil(textSize.x) + 2f);
            textSize = _boxText.GetPreferredValues(text, textWidth, 0f);
            float textHeight = Mathf.Ceil(textSize.y);

            float contentHeight = Mathf.Max(textHeight, withKey ? KeySize : 0f);
            float buttonArea = withConfirm ? _confirmButton.sizeDelta.y + 16f : 0f;
            float width = left + textWidth + BoxPadding;
            if (withConfirm)
            {
                width = Mathf.Max(width, _confirmButton.sizeDelta.x + BoxPadding * 2f);
            }
            float height = BoxPadding + contentHeight + buttonArea + BoxPadding;
            _box.sizeDelta = new Vector2(width, height);

            // 글자 칸은 내용 영역(확인 버튼 위) 전체. 그 안에서 Midline 정렬로 세로 가운데에 놓인다
            _boxText.rectTransform.anchoredPosition = new Vector2(left, -BoxPadding);
            _boxText.rectTransform.sizeDelta = new Vector2(textWidth, contentHeight);
            _key.anchoredPosition = new Vector2(BoxPadding, (height * 0.5f) - BoxPadding - contentHeight * 0.5f);
            _confirmButton.anchoredPosition = new Vector2(-BoxPadding, BoxPadding);
        }

        private TextMeshProUGUI CreateText(RectTransform parent, string name, string text, float size)
        {
            RectTransform rect = CreateChild(parent, name);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) label.font = _font;
            label.fontSize = size;
            label.color = BoxTextColor;
            label.text = text;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        private static RectTransform CreateChild(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // 기획서와 같은 D2Coding이 불러와져 있으면 쓰고, 없으면 TMP 기본 글꼴을 쓴다 (PauseMenu와 같은 방식)
        private static TMP_FontAsset FindFont()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font != null && font.name.Contains("D2Coding")) return font;
            }
            return TMP_Settings.defaultFontAsset;
        }
    }
}
