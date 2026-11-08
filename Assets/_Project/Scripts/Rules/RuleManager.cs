using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoffeeSpec.Core;
using CoffeeSpec.Maps;
using CoffeeSpec.Entities;
using CoffeeSpec.UI;

namespace CoffeeSpec.Rules
{
    /// <summary>
    /// CSV의 문장 틀을 골라 태그를 채운 문장을 보관하고, 문장에 든 태그를 보고 효과를 실행한다. 씬에 하나만 둔다.
    /// - 명사|명사: 앞 명사 대상이 뒤 명사로 영구 변신 (태그와 모습)
    /// - 명사|Move: 명사 대상이 문장이 있는 동안 랜덤 이동
    /// - 명사|Fly: 명사 대상이 문장이 있는 동안 랜덤 높이로 떠서 둥실거림
    /// - 명사|Control: 명사 대상이 문장이 있는 동안 플레이어 조작을 받음 (여럿이면 같이 움직임)
    /// - 명사|Win: 문장이 있는 동안 명사 대상이 클리어 지점이 됨
    /// - 조건 문장(문장 틀에 {n@If} 빈칸이 있는 문장. 예: 사람은 날면 비둘기이다):
    ///   주어 대상 중 조건이 맞는 대상에게만 나머지 단어의 효과를 준다.
    ///   동사 조건은 그 대상이 지금 그 상태(Move/Fly/Control/Win)일 때, 명사 조건은 지금 그 태그일 때 참.
    ///   조건 재료(태그, 상태)는 모두 이 매니저가 바꾸므로 매 프레임 확인하지 않고,
    ///   문장이나 대상이 바뀌어 다시 적용할 때만 결과가 더 안 바뀔 때까지 계산한다.
    ///   조건으로 생긴 변신도 다른 변신처럼 영구다.
    /// - 사망: Die 문장과 플레이어 공격은 똑같이 대상을 사망 상태(숨김)로 만든다. 사망 상태는 계속 남아서
    ///   "사망하면" 조건에 걸린다. 사망한 대상은 다른 조건과 효과는 받지 않지만, "사망하면" 조건으로 변신하면
    ///   새 모습으로 다시 살아나고 사망 상태는 풀린다. 공격으로 죽으면 그 순간에만 규칙을 다시 적용한다.
    /// - 플레이어 입력: 조종 대상을 좌우 이동·대쉬·점프·공격으로 움직이는 동안은 "움직이면" 조건에 걸린다
    ///   (랜덤 이동은 켜지 않는다). 움직이기 시작할 때와 멈출 때만 규칙을 다시 적용한다.
    /// - 밟힘: 다른 대상이 머리 위에 올라서 있는 동안 "밟히면" 조건에 걸린다. 결과 쪽에 쓰면 효과가 없다.
    ///   출구처럼 통과되는 대상도 "밟히면" 문장의 주어인 동안은 부딪히는 몸이 된다 (부딪혀도 클리어는 된다).
    ///   올라설 때와 내려갈 때만 규칙을 다시 적용한다.
    /// - 만짐(Touch): 결과로 받으면(출구는 만져진다) 통과되는 대상도 그동안 부딪히는 몸이 된다.
    ///   조건으로 쓰면(만지면) 그 대상이 벽·바닥을 포함해 무언가에 닿아 있는 동안 참이다.
    ///   닿기 시작할 때와 모두 떨어질 때만, 그리고 '만지면' 문장이 있을 때만 규칙을 다시 적용한다.
    /// - 닿으면 바꾸기(문장 틀에 {n@ac} 빈칸이 있는 문장. 예: 사람은 만지면 비둘기가 된다):
    ///   첫 칸 대상에 지금 닿아 있는 다른 대상(벽·바닥 대상 포함)이 ac 칸의 효과를 받는다.
    ///   명사면 닿은 대상이 그 태그로 영구 변신하고, 동사면 닿아 있는 동안만 그 상태가 된다.
    ///   첫 칸 대상의 접촉이 바뀔 때만 규칙을 다시 적용한다.
    /// - 접촉: 조종 대상이 Win을 받은 대상에 닿으면 클리어
    /// 실행 중 인스펙터에서 문장 목록을 바꾸면 전체 규칙을 다시 적용한다.
    /// </summary>
    public class RuleManager : MonoBehaviour
    {
        [Serializable]
        public class SentenceSetup : ISerializationCallbackReceiver
        {
            [Tooltip("CSV의 Id 열 값 (예: NounIsNoun)")]
            public string TemplateId;
            [Tooltip("CSV Pattern 순서대로 채울 태그")]
            public List<WordId> WordIds = new();
            // 예전 enum 숫자 목록. 불러올 때 WordIds로 옮기고 비운다
            [HideInInspector] public List<int> Words = new();
            [Tooltip("플레이어에게 보일 문장 (자동 표시)")]
            public string Preview;

            public void OnBeforeSerialize() { }

            public void OnAfterDeserialize()
            {
                if (Words == null || Words.Count == 0) return;
                if (WordIds == null || WordIds.Count == 0)
                {
                    WordIds = new List<WordId>();
                    foreach (int legacy in Words) WordIds.Add(WordId.FromLegacy(legacy));
                }
                Words.Clear();
            }
        }

        [Tooltip("단어 목록 CSV (Id,DisplayName,Type). Data/WordsCSV.csv")]
        [SerializeField] private TextAsset _wordCsv;
        [Tooltip("문장 틀 CSV (Id,Text,Pattern)")]
        [SerializeField] private TextAsset _sentenceCsv;
        [Tooltip("현재 레벨에서 효력이 있는 문장. 실행 중에 바꿔도 즉시 반영된다")]
        [SerializeField] private List<SentenceSetup> _sentenceSetups = new();
        [Tooltip("기획서 화면. 비워 두면 씬에서 찾는다. 빌드하면 문장을 적용하고, 리트하면 맵을 다시 깐다")]
        [SerializeField] private SpecPanel _specPanel;
        [Tooltip("리트할 때 Level을 다시 까는 로더. 비워 두면 씬에서 찾는다")]
        [SerializeField] private MapLoader _mapLoader;
        [Tooltip("클리어하고 다음 스테이지로 넘어가기까지 기다리는 시간(초). 시간이 멈춰 있어도 흐른다")]
        [SerializeField, Min(0f)] private float _nextStageDelay = 2f;
        [Tooltip("다음 스테이지 씬이 없을 때 돌아갈 씬 이름")]
        [SerializeField] private string _titleSceneName = "Title";

        private Dictionary<string, SentenceTemplate> _templates = new();
        private readonly List<Sentence> _sentences = new();
        private readonly List<RuleEntity> _entities = new();
        // Win 문장을 받은 대상. 조종 대상이 닿으면 클리어된다
        private readonly HashSet<RuleEntity> _winTargets = new();
        private bool _started;
        private bool _rebuildPending;
        private bool _cleared;
        private Coroutine _nextStageRoutine;
        private bool _controlledDied;    // 이번 적용에서 조종받던 대상이 Die로 죽었는지
        private bool _gameOver;          // You Die!가 떠 있는지


        private static readonly WordId Fly = new("Fly");
        private static readonly WordId Control = new("Control");
        private static readonly WordId Win = new("Win");
        private static readonly WordId Die = new("Die");
        private static readonly WordId Tread = new("Tread");
        private static readonly WordId Touch = new("Touch");

        /// <summary>스테이지를 클리어했을 때 알린다. 저장, 다음 스테이지 이동 등은 여기에 구독한다.</summary>
        public event Action Cleared;

        public IReadOnlyList<Sentence> Sentences => _sentences;
        public IReadOnlyCollection<string> TemplateIds => _templates.Keys;

        /// <summary>지금 조종받는 대상 전부. 카메라가 이 대상들을 한 화면에 담는다.</summary>
        public IEnumerable<RuleEntity> GetControlled()
        {
            foreach (var e in _entities)
                if (e != null && !e.IsRemoved && e.IsPlayer) yield return e;
        }

        private void Awake()
        {
            // 빌드에서는 경로로 에셋을 읽을 수 없어 참조로 넘겨준다
            WordTags.Initialize(_wordCsv);
            LoadTemplates();
        }

        private void Start()
        {
            _started = true;
            Rebuild();
        }

        private void OnEnable()
        {
            if (_specPanel == null) _specPanel = FindAnyObjectByType<SpecPanel>();
            if (_specPanel == null) return;
            _specPanel.Built += ResetAndApply;
            _specPanel.PreviewStarted += ResetAndApply;
            _specPanel.RetryRequested += Retry;
        }

        private void OnDisable()
        {
            if (_specPanel == null) return;
            _specPanel.Built -= ResetAndApply;
            _specPanel.PreviewStarted -= ResetAndApply;
            _specPanel.RetryRequested -= Retry;
        }

        /// <summary>
        /// 실행 중에 문장 목록을 통째로 바꿔 적용한다. 기획서 화면의 빌드 결과가 여기로 들어온다.
        /// 품사가 맞지 않는 문장은 경고만 남기고 건너뛴다.
        /// </summary>
        public void ApplySentences(IReadOnlyList<Sentence> sentences)
        {
            FillSentences(sentences);
            ReapplyAll();
        }

        // 빌드와 미리보기: Level을 시작 배치로 다시 깔고 받은 문장을 적용한다.
        // 이전 판에서 움직이거나 변신한 상태가 남지 않도록 항상 초기화부터 한다
        private void ResetAndApply(IReadOnlyList<Sentence> sentences)
        {
            if (_mapLoader == null) _mapLoader = FindAnyObjectByType<MapLoader>();
            _cleared = false;
            _gameOver = false;
            CancelNextStage();
            ClearScreen.Hide();
            // 다시 깔린 객체가 등록될 때 이전 문장으로 변신하지 않도록 문장부터 바꾼다
            FillSentences(sentences);
            if (_mapLoader != null) _mapLoader.Reload();
            else Debug.LogError("[RuleManager] 씬에 MapLoader가 없어 레벨을 초기화하지 못함");
            ReapplyAll();
        }

        private void FillSentences(IReadOnlyList<Sentence> sentences)
        {
            _sentences.Clear();
            if (sentences != null)
            {
                foreach (var sentence in sentences)
                {
                    if (sentence == null) continue;
                    if (!sentence.IsValid())
                    {
                        Debug.LogWarning($"[RuleManager] 품사 순서가 맞지 않는 문장: {sentence}");
                        continue;
                    }
                    _sentences.Add(sentence);
                }
            }
        }

        // 리트는 씬을 바꾸지 않고 Level 밑 프리팹만 맵 데이터대로 다시 깐다.
        // 빌드로 적용했던 문장 효과는 버리고 스테이지 시작 상태(인스펙터 문장 목록)로 되돌린 뒤 기획서를 다시 연다.
        // 기획서에 고른 단어는 그대로 남아 있어서 다시 빌드하면 된다
        private void Retry()
        {
            if (_mapLoader == null) _mapLoader = FindAnyObjectByType<MapLoader>();
            if (_mapLoader == null)
            {
                Debug.LogError("[RuleManager] 씬에 MapLoader가 없어 리트할 수 없음");
                return;
            }
            _cleared = false;
            _gameOver = false;
            CancelNextStage();
            ClearScreen.Hide();
            LoadSetupSentences();
            _mapLoader.Reload();
            ReapplyAll();
            if (_specPanel != null) _specPanel.Reopen();
        }
        private void OnValidate()
        {
            // 편집 중에도 미리보기 문장을 갱신한다
            WordTags.Initialize(_wordCsv);
            LoadTemplates();
            foreach (var s in _sentenceSetups)
            {
                if (s == null) continue;
                var sentence = CreateSentence(s);
                s.Preview = sentence == null ? "(CSV에 없는 Id)" :
                            sentence.IsValid() ? sentence.DisplayText : "(품사 순서가 맞지 않음)";
            }
            // OnValidate 안에서는 컴포넌트 조작이 제한돼 다음 Update에서 처리한다
            if (Application.isPlaying && _started) _rebuildPending = true;
        }

        private void Update()
        {
            if (!_rebuildPending) return;
            _rebuildPending = false;
            Rebuild();
        }


        private void LoadTemplates()
        {
            _templates = _sentenceCsv != null
                ? SentenceTemplate.ParseCsv(_sentenceCsv.text)
                : new Dictionary<string, SentenceTemplate>();
        }

        private Sentence CreateSentence(SentenceSetup setup)
        {
            if (setup == null || string.IsNullOrEmpty(setup.TemplateId)) return null;
            return _templates.TryGetValue(setup.TemplateId.Trim(), out var t)
                ? new Sentence(t, new List<WordId>(setup.WordIds)) : null;
        }

        private void Rebuild()
        {
            LoadSetupSentences();
            ReapplyAll();
        }

        // 인스펙터 문장 목록(스테이지 시작 상태)으로 문장을 채운다. 적용은 하지 않는다
        private void LoadSetupSentences()
        {
            _sentences.Clear();
            foreach (var setup in _sentenceSetups)
            {
                var sentence = CreateSentence(setup);
                if (sentence == null)
                {
                    Debug.LogWarning($"[RuleManager] CSV에 없는 문장 Id: {setup?.TemplateId}");
                    continue;
                }
                if (!sentence.IsValid())
                {
                    Debug.LogWarning($"[RuleManager] 품사 순서가 맞지 않는 문장: {sentence}");
                    continue;
                }
                _sentences.Add(sentence);
            }
        }

        private bool _reapplying;
        private bool _reapplyQueued;

        // 이동 같은 지속 효과를 끈 뒤 문장을 다시 적용한다. 변신은 영구라 되돌리지 않는다.
        // 적용하는 도중에 다시 요청이 오면(조종이 꺼져 움직임 상태가 풀리는 등) 끝난 뒤에 한 번 더 한다
        private void ReapplyAll()
        {
            if (_reapplying)
            {
                _reapplyQueued = true;
                return;
            }
            _reapplying = true;
            _controlledDied = false;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    _reapplyQueued = false;
                    ReapplyOnce();
                    if (!_reapplyQueued) break;
                }
            }
            finally
            {
                _reapplying = false;
                _reapplyQueued = false;
            }
            // 사망하면 문장으로 되살아나 조종을 받을 수도 있으니 적용이 다 끝난 뒤에 따진다
            CheckGameOver();
        }

        // 조종받던 대상이 죽고 조종받는 대상이 하나도 안 남았으면 You Die!를 띄운다.
        // 문장이 빠져 조종이 풀린 것만으로는 죽은 게 아니라 띄우지 않는다. 리트(R)·빌드로 지운다
        private void CheckGameOver()
        {
            bool anyControlled = false;
            foreach (var _ in GetControlled()) { anyControlled = true; break; }

            if (_gameOver && anyControlled)
            {
                // 죽은 뒤 되살아나 다시 조종을 받으면 지운다
                _gameOver = false;
                if (!_cleared) ClearScreen.Hide();
                return;
            }
            if (_gameOver || _cleared || !_controlledDied || anyControlled) return;
            _gameOver = true;
            ClearScreen.ShowDeath();
        }

        private void ReapplyOnce()
        {
            foreach (var e in _entities) if (e != null) e.ResetState();
            // 변신을 먼저 해야 '사람은 비둘기다 + 비둘기는 움직인다'에서 바뀐 사람도 움직인다
            // 조건 변신이 일어나면 바뀐 태그로 변신·효과 문장을 처음부터 다시 따진다 (변신은 영구라 반드시 끝난다)
            for (int round = 0; round < 8; round++)
            {
                foreach (var s in _sentences) if (IsTransform(s)) ApplyTransform(s);
                if (!CollectEffects()) break;
            }
            ApplyEffects();
        }

        // 지속 효과마다 받아야 할 대상을 모두 모은 뒤 한 번에 켜고 끈다.
        // 조건 계산 중에는 대상을 건드리지 않고 집합에만 모아서, 중간 상태로 켰다 껐다 하지 않는다
        private readonly HashSet<RuleEntity> _moveSet = new();
        private readonly HashSet<RuleEntity> _flySet = new();
        private readonly HashSet<RuleEntity> _controlSet = new();
        private readonly HashSet<RuleEntity> _dieSet = new();
        // 만져진다(Touch)를 받은 대상. 통과되는 대상도 이 동안은 부딪힌다
        private readonly HashSet<RuleEntity> _touchSet = new();
        // "밟히면" 조건 문장의 주어. 통과되는 대상도 이 동안은 부딪혀서 밟을 수 있게 한다
        private readonly HashSet<RuleEntity> _treadableSet = new();

        private bool _conditionalTransformed;

        /// <returns>조건 문장으로 변신이 일어났으면 true</returns>
        private bool CollectEffects()
        {
            _conditionalTransformed = false;
            _moveSet.Clear();
            _flySet.Clear();
            _controlSet.Clear();
            _dieSet.Clear();
            _touchSet.Clear();
            _winTargets.Clear();

            foreach (var s in _sentences)
                if (!IsTransform(s) && ConditionSlot(s) < 0 && AcSlot(s) < 0) ApplyEffect(s);

            // 조건이 다른 조건 문장의 결과에 기댈 수 있어서(날면 움직인다 + 움직이면 조종) 더 바뀌지 않을 때까지 몇 번 돈다
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                foreach (var s in _sentences)
                {
                    if (ConditionSlot(s) >= 0) changed |= ApplyConditional(s);
                    else if (AcSlot(s) >= 0) changed |= ApplyTouchSentence(s);
                }
                if (!changed) break;
            }
            return _conditionalTransformed;
        }

        // 모은 효과를 대상에 반영한다. 상태가 그대로인 대상은 각 Set 함수가 건드리지 않는다
        private void ApplyEffects()
        {
            // 사라지는 대상이 있으면 목록 순회 중에 상태가 바뀌므로 먼저 복사한다
            CollectTreadable();
            foreach (var e in new List<RuleEntity>(_entities))
            {
                if (e == null || e.IsRemoved) continue;
                e.SetTreadable(_treadableSet.Contains(e));
                e.SetTouchable(_touchSet.Contains(e));
                // Die: 공격받았을 때처럼 숨기고 상호작용을 끈다. 영구라 문장이 빠져도 돌아오지 않는다
                if (_dieSet.Contains(e))
                {
                    // 조종받던 대상이거나 이번에 조종을 받을 대상이면 플레이어가 죽은 것이다
                    if (e.IsPlayer || _controlSet.Contains(e)) _controlledDied = true;
                    e.Remove();
                    continue;
                }
                e.SetRandomMove(_moveSet.Contains(e));
                e.SetFly(_flySet.Contains(e));
                // 조종은 껐다 켜면 움직임이 끊기므로 바뀌는 대상만 켜고 끈다
                e.SetControl(_controlSet.Contains(e));
            }
        }

        // 변신까지 끝난 지금 태그 기준으로 "밟히면" 문장의 주어를 모은다. 주어가 없는 문장(밟히면 …)은 모든 대상이다
        private void CollectTreadable()
        {
            _treadableSet.Clear();
            foreach (var s in _sentences)
            {
                int condSlot = ConditionSlot(s);
                if (condSlot < 0 || s.Words[condSlot] != Tread) continue;
                bool hasSubject = condSlot > 0;
                if (hasSubject && !WordTags.Is(s.Words[0], WordType.Noun)) continue;
                foreach (var e in _entities)
                    if (e != null && !e.IsRemoved && (!hasSubject || e.Id == s.Words[0])) _treadableSet.Add(e);
            }
        }

        // ---- 태그를 보고 효과를 정하는 부분. 새 효과는 여기에 추가한다 ----

        private static bool IsTransform(Sentence s) =>
            s.Words.Count == 2 && ConditionSlot(s) < 0 && AcSlot(s) < 0 &&
            WordTags.Is(s.Words[0], WordType.Noun) && WordTags.Is(s.Words[1], WordType.Noun);

        private void ApplyEffect(Sentence s)
        {
            if (s.Words.Count == 0 || !WordTags.Is(s.Words[0], WordType.Noun)) return;
            var subject = s.Words[0];

            for (int i = 1; i < s.Words.Count; i++)
                foreach (var e in GetEntities(subject)) AddEffect(e, s.Words[i]);
        }

        // 효과가 연결된 동사만 여기서 처리한다. 새 동사 효과는 여기와 HasEffect에 추가한다
        private bool AddEffect(RuleEntity e, WordId verb)
        {
            if (verb == WordId.Move) return _moveSet.Add(e);
            if (verb == Fly) return _flySet.Add(e);
            if (verb == Control) return _controlSet.Add(e);
            if (verb == Win) return _winTargets.Add(e);
            if (verb == Die) return _dieSet.Add(e);
            if (verb == Touch) return _touchSet.Add(e);
            return false;
        }

        // 동사 조건: 그 대상이 지금 그 상태인지 (이번에 모으는 중인 효과 기준)
        private bool HasEffect(RuleEntity e, WordId verb)
        {
            // 플레이어가 직접 움직이는 중이어도 움직이는 상태다
            if (verb == WordId.Move) return _moveSet.Contains(e) || e.IsInputMoving;
            if (verb == Fly) return _flySet.Contains(e);
            if (verb == Control) return _controlSet.Contains(e);
            if (verb == Win) return _winTargets.Contains(e);
            // 공격이나 이전 Die로 이미 죽은 대상도 사망 상태다
            if (verb == Die) return e.IsRemoved || _dieSet.Contains(e);
            // 밟힘은 조건으로만 쓴다: 다른 대상이 머리 위에 올라서 있는 동안 참
            if (verb == Tread) return e.IsTreaded;
            // 만짐은 조건일 때 '지금 무언가에 닿아 있다'는 뜻이다 (결과로 받은 만져진다와는 따로 본다)
            if (verb == Touch) return e.IsTouched;
            return false;
        }

        // 문장 틀에서 {n@If}로 쓰인 빈칸 번호. 조건 문장이 아니면 -1
        private static int ConditionSlot(Sentence s)
        {
            if (s.Template == null) return -1;
            for (int i = 0; i < s.Words.Count; i++)
                if (string.Equals(s.Template.SlotForm(i), WordTags.IfForm, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // 문장 틀에서 {n@ac}로 쓰인 빈칸 번호. 닿으면 바꾸는 문장이 아니면 -1
        private static int AcSlot(Sentence s)
        {
            if (s.Template == null) return -1;
            for (int i = 0; i < s.Words.Count; i++)
                if (string.Equals(s.Template.SlotForm(i), WordTags.AcForm, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>
        /// 사람은 만지면 비둘기가 된다: 첫 칸(명사) 대상에 지금 닿아 있는 다른 대상이 ac 칸의 효과를 받는다.
        /// 명사면 영구 변신, 동사면 이번 계산에서만 효과를 줘서 떨어지면 다음 적용 때 풀린다.
        /// </summary>
        /// <returns>새로 효과를 받은 대상이 있거나 변신이 일어났으면 true</returns>
        private bool ApplyTouchSentence(Sentence s)
        {
            int acSlot = AcSlot(s);
            if (acSlot <= 0 || !WordTags.Is(s.Words[0], WordType.Noun)) return false;
            WordId subject = s.Words[0], word = s.Words[acSlot];
            bool isNoun = WordTags.Is(word, WordType.Noun);
            bool changed = false;
            var touchers = new List<RuleEntity>();

            // 변신하면 목록이 달라지므로 먼저 복사한다
            foreach (var e in new List<RuleEntity>(_entities))
            {
                if (e == null || e.IsRemoved || e.Id != subject) continue;
                touchers.Clear();
                e.GetTouchingEntities(touchers);
                foreach (var t in touchers)
                {
                    if (t.IsRemoved) continue;
                    if (isNoun)
                    {
                        if (t.Id == word) continue;
                        var source = FindOriginalEntity(word);
                        if (source == null) continue;
                        t.TransformInto(word, source);
                        _conditionalTransformed = true;
                        changed = true;
                    }
                    else
                    {
                        changed |= AddEffect(t, word);
                    }
                }
            }
            return changed;
        }

        private static bool IsConditionMet(RuleEntity e, WordId condition, RuleManager self) =>
            WordTags.Is(condition, WordType.Noun) ? e.Id == condition : self.HasEffect(e, condition);

        /// <summary>
        /// 조건 칸이 첫 칸이 아니면 첫 칸(명사)이 주어다: 사람은 날면 비둘기이다.
        /// 조건 칸이 첫 칸이면 주어가 없어서 조건이 맞는 모든 대상이 받는다: 날면 움직인다.
        /// </summary>
        /// <returns>새로 효과를 받은 대상이 있거나 변신이 일어났으면 true</returns>
        private bool ApplyConditional(Sentence s)
        {
            int condSlot = ConditionSlot(s);
            bool hasSubject = condSlot > 0;
            if (hasSubject && !WordTags.Is(s.Words[0], WordType.Noun)) return false;
            WordId subject = hasSubject ? s.Words[0] : default, condition = s.Words[condSlot];
            int firstResult = hasSubject ? 1 : 0;
            bool changed = false;

            // 변신하면 목록이 달라지므로 먼저 복사한다
            foreach (var e in new List<RuleEntity>(_entities))
            {
                if (e == null) continue;
                // 사망한 대상은 "사망하면" 조건에만 걸린다 (변신하면 그 모습으로 다시 살아난다)
                if (e.IsRemoved && condition != Die) continue;
                if (hasSubject && e.Id != subject) continue;
                if (!IsConditionMet(e, condition, this)) continue;

                for (int i = firstResult; i < s.Words.Count; i++)
                {
                    if (i == condSlot) continue;
                    var word = s.Words[i];
                    if (WordTags.Is(word, WordType.Noun))
                    {
                        if (e.Id == word) continue;
                        var source = FindOriginalEntity(word);
                        if (source == null) continue;
                        e.TransformInto(word, source);
                        _conditionalTransformed = true;
                        changed = true;
                    }
                    else
                    {
                        changed |= AddEffect(e, word);
                    }
                }
            }
            return changed;
        }

        private void ApplyTransform(Sentence s)
        {
            WordId from = s.Words[0], to = s.Words[1];
            if (from == to) return;
            var source = FindOriginalEntity(to);
            if (source == null)
            {
                Debug.LogWarning($"[RuleManager] 씬에 {to} 태그 객체가 없어 모습을 가져올 수 없음");
                return;
            }
            // 태그가 바뀌는 중에 목록이 달라지지 않도록 먼저 복사한다
            foreach (var e in new List<RuleEntity>(GetEntities(from))) e.TransformInto(to, source);
        }

        // ---- 접촉 규칙. RuleEntity가 닿을 때마다 보고한다 ----

        /// <summary>두 대상이 닿았다. 양쪽 모두 보고하므로 같은 접촉이 두 번 올 수 있다.</summary>
        public void ReportContact(RuleEntity a, RuleEntity b)
        {
            // 기획서가 열려 있는 동안(미리보기 포함)은 클리어하지 않는다
            if (_specPanel != null && _specPanel.IsOpen) return;
            if (a.IsRemoved || b.IsRemoved) return;
            if (IsWinContact(a, b) || IsWinContact(b, a)) Clear();
        }

        // 조종 대상이 Win을 받은 대상에 닿았는지
        private bool IsWinContact(RuleEntity player, RuleEntity other) =>
            player.IsPlayer && _winTargets.Contains(other);

        private void Clear()
        {
            if (_cleared || _gameOver) return;
            _cleared = true;
            ClearScreen.Show();
            Cleared?.Invoke();
            CancelNextStage();
            _nextStageRoutine = StartCoroutine(GoToNextStage());
        }

        // 리트나 미리보기로 판이 다시 시작되면 넘어가지 않는다
        private void CancelNextStage()
        {
            if (_nextStageRoutine == null) return;
            StopCoroutine(_nextStageRoutine);
            _nextStageRoutine = null;
        }

        private IEnumerator GoToNextStage()
        {
            yield return new WaitForSecondsRealtime(_nextStageDelay);
            _nextStageRoutine = null;
            SceneFader.LoadScene(NextSceneName());
        }

        /// <summary>
        /// 지금 씬 이름의 마지막 숫자를 하나 올린 씬 (Stage1Scene → Stage2Scene).
        /// 숫자가 없거나 그 씬이 Build Settings에 없으면 타이틀 씬.
        /// </summary>
        private string NextSceneName()
        {
            string current = SceneManager.GetActiveScene().name;
            var match = Regex.Match(current, @"(\d+)(?!.*\d)");
            if (match.Success && int.TryParse(match.Value, out int number))
            {
                string next = current.Substring(0, match.Index) + (number + 1) + current.Substring(match.Index + match.Length);
                if (Application.CanStreamedLevelBeLoaded(next)) return next;
                Debug.Log($"[RuleManager] 다음 스테이지 씬 '{next}'이 Build Settings에 없어 {_titleSceneName}(으)로 돌아감");
            }
            return _titleSceneName;
        }

        // ---- 대상 관리 ----

        // 이미 바뀐 객체가 아니라 원래 그 태그였던 객체의 모습을 쓴다
        public RuleEntity FindOriginalEntity(WordId id)
        {
            foreach (var e in _entities)
                if (e != null && e.OriginalId == id) return e;
            return null;
        }

        public Sprite FindOriginalSprite(WordId id)
        {
            foreach (var e in _entities)
                if (e != null && e.OriginalId == id) return e.OriginalSprite;
            return null;
        }

        public void Register(RuleEntity entity)
        {
            if (_entities.Contains(entity)) return;
            _entities.Add(entity);
            if (_started) ReapplyAll();
        }

        public void Unregister(RuleEntity entity) => _entities.Remove(entity);

        /// <summary>공격으로 대상이 죽었다. Die 문장으로 죽은 것처럼 "사망하면" 조건을 다시 따진다.</summary>
        public void NotifyDied(RuleEntity entity)
        {
            if (_started) ReapplyAll();
        }

        /// <summary>누가 머리 위에 올라섰거나 내려갔다. "밟히면" 조건을 다시 따진다.</summary>
        public void NotifyTreadChanged(RuleEntity entity)
        {
            if (_started) ReapplyAll();
        }

        /// <summary>
        /// 닿은 콜라이더가 늘거나 줄었다. touchedChanged는 '닿아 있음'이 바뀌었는지.
        /// '만지면' 조건이 바뀌었거나, 이 대상이 닿으면 바꾸는 문장의 첫 칸일 때만 다시 따진다.
        /// </summary>
        public void NotifyTouchChanged(RuleEntity entity, bool touchedChanged)
        {
            if (!_started) return;
            if ((touchedChanged && UsesCondition(Touch)) || IsTouchSubject(entity.Id)) ReapplyAll();
        }

        // 이 태그를 첫 칸으로 쓴 닿으면 바꾸는 문장이 있는지
        private bool IsTouchSubject(WordId id)
        {
            foreach (var s in _sentences)
                if (AcSlot(s) > 0 && s.Words[0] == id) return true;
            return false;
        }

        // 그 단어를 조건 칸에 쓴 문장이 있는지. 없으면 접촉이 바뀌어도 다시 적용할 필요가 없다
        private bool UsesCondition(WordId word)
        {
            foreach (var s in _sentences)
            {
                int condSlot = ConditionSlot(s);
                if (condSlot >= 0 && s.Words[condSlot] == word) return true;
            }
            return false;
        }

        /// <summary>플레이어가 움직이기 시작했거나 멈췄다. "움직이면" 조건을 다시 따진다.</summary>
        public void NotifyInputMovingChanged(RuleEntity entity)
        {
            if (_started) ReapplyAll();
        }

        // 공격으로 사라진 대상은 규칙 효과를 받지 않는다. 변신 원본 찾기(FindOriginalEntity)에서는 그대로 쓴다
        public IEnumerable<RuleEntity> GetEntities(WordId id)
        {
            foreach (var e in _entities)
                if (e != null && !e.IsRemoved && e.Id == id) yield return e;
        }
    }
}