using System;
using System.Collections.Generic;
using UnityEngine;
using CoffeeSpec.Rules.Effects;
using CoffeeSpec.Maps;
using CoffeeSpec.Rules.Entities;
using CoffeeSpec.UI;

namespace CoffeeSpec.Rules
{
    /// <summary>
    /// CSV의 문장 틀을 골라 태그를 채운 문장을 보관하고, 문장에 든 태그를 보고 효과를 실행한다. 씬에 하나만 둔다.
    /// - 명사|명사: 앞 명사 대상이 뒤 명사로 영구 변신 (태그와 모습)
    /// - 명사|Move: 명사 대상이 문장이 있는 동안 보는 쪽으로 쭉 가다가 부딪히면 반대로 돌아섬
    /// - 명사|Fly: 명사 대상이 문장이 있는 동안 랜덤 높이로 떠서 둥실거림
    /// - 명사|Control: 명사 대상이 문장이 있는 동안 플레이어 조작을 받음 (여럿이면 같이 움직임)
    /// - 명사|Win: 문장이 있는 동안 명사 대상이 클리어 지점이 됨
    /// - 조건 문장(문장 틀에 {n@If} 빈칸이 있는 문장. 예: 사람은 날면 비둘기이다):
    ///   주어 대상 중 조건이 맞는 대상에게만 나머지 단어의 효과를 준다.
    ///   동사 조건은 그 대상이 지금 그 상태(Move/Fly/Control/Win)일 때, 명사 조건은 지금 그 태그일 때 참.
    ///   조건 재료(태그, 상태)는 모두 이 매니저가 바꾸므로 매 프레임 확인하지 않고,
    ///   문장이나 대상이 바뀌어 다시 적용할 때만 결과가 더 안 바뀔 때까지 계산한다.
    ///   조건으로 생긴 변신도 다른 변신처럼 영구다.
    /// - 사망: Die 문장은 대상을 사망 상태(숨김)로 만든다. 사망 상태는 계속 남아서
    ///   "사망하면" 조건에 걸린다. 사망한 대상은 다른 조건과 효과는 받지 않지만, "사망하면" 조건으로 변신하면
    ///   새 모습으로 다시 살아나고 사망 상태는 풀린다.
    /// - 플레이어 입력: 조종 대상을 좌우 이동·점프로 움직이는 동안은 "움직이면" 조건에 걸린다
    ///   (Move 이동은 켜지 않는다). 움직이기 시작할 때와 멈출 때만 규칙을 다시 적용한다.
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

        /// <summary>
        /// 문장 없이도 항상 붙는 기본 속성. 명사 태그를 따라간다 (변신해서 태그가 바뀌면 새 태그의 기본 속성을 받는다).
        /// 출구는 도착지점, 사람은 조종 가능처럼 모든 스테이지에 늘 필요한 규칙을 기획서에서 빼려고 둔다.
        /// </summary>
        [Serializable]
        public class InnateEffect
        {
            [Tooltip("기본 속성을 받을 명사 (예: Exit)")]
            public WordId Noun;
            [Tooltip("문장 없이도 항상 받는 동사 (예: Win)")]
            public List<WordId> Verbs = new();

            public InnateEffect() { }

            public InnateEffect(string noun, params string[] verbs)
            {
                Noun = new WordId(noun);
                foreach (string verb in verbs) Verbs.Add(new WordId(verb));
            }
        }
        [Tooltip("단어 목록 CSV (Id,DisplayName,Type). Data/WordsCSV.csv")]
        [SerializeField] private TextAsset _wordCsv;
        [Tooltip("문장 틀 CSV (Id,Text,Pattern)")]
        [SerializeField] private TextAsset _sentenceCsv;
        [Tooltip("현재 레벨에서 효력이 있는 문장. 실행 중에 바꿔도 즉시 반영된다")]
        [SerializeField] private List<SentenceSetup> _sentenceSetups = new();
        [Tooltip("문장 없이도 항상 붙는 기본 속성 (명사 → 동사). 기획서·인스펙터 문장보다 먼저 들어간다")]
        [SerializeField] private List<InnateEffect> _innateEffects = new()
        {
            new InnateEffect("Exit", "Win"),
            new InnateEffect("Person", "Control"),
        };        [Tooltip("기획서 화면. 비워 두면 씬에서 찾는다. 빌드하면 문장을 적용하고, 리트하면 맵을 다시 깐다")]
        [SerializeField] private SpecPanel _specPanel;
        [Tooltip("리트할 때 Level을 다시 까는 로더. 비워 두면 씬에서 찾는다")]
        [SerializeField] private MapLoader _mapLoader;

        [Tooltip("맵에 원본이 없을 때 변신 모습을 가져올 오브젝트 프리팹. 비어 있으면 에디터에서 Prefabs/Object 폴더를 자동으로 채운다")]
        [SerializeField] private List<RuleEntity> _transformPrefabs = new();
        // 프리팹에서 만든 숨김 원본. Awake에서 원래 모습을 기록해야 변신에 쓸 수 있어서 한 번 만들어 꺼 둔다
        private readonly Dictionary<WordId, RuleEntity> _prefabTemplates = new();
        private Transform _templateRoot;

        private Dictionary<string, SentenceTemplate> _templates = new();
        private readonly List<Sentence> _sentences = new();
        private readonly List<RuleEntity> _entities = new();
        private VerbEffectCollection _effects;
        // 재진입은 ReapplyAll에서 대기시킨다. 단계별 버퍼는 서로의 순회를 덮어쓰지 않는다.
        private readonly List<RuleEntity> _effectSnapshot = new();
        private readonly List<RuleEntity> _conditionalSnapshot = new();
        private readonly List<RuleEntity> _transformSnapshot = new();
        private readonly List<RuleEntity> _touchSnapshot = new();
        private readonly List<RuleEntity> _touchers = new();
        private readonly List<RuleEntity> _reverseTouch = new();
        // 대상 -> 그 대상을 만지고 있는 대상들. 만지면 문장 한 번에 한 번만 만들어 주어마다 다시 훑지 않는다
        private readonly Dictionary<RuleEntity, List<RuleEntity>> _touchedBy = new();
        private readonly Stack<List<RuleEntity>> _touchListPool = new();
        // 한 번의 판정에서 모인 만지면 결과. 문장마다 바로 적용하면 앞 문장의 변신 때문에 뒤 문장이 대상을 못 찾는다
        private readonly List<(RuleEntity target, WordId word)> _pendingTouch = new();
        // 이번 적용(ReapplyOnce) 동안 만지면으로 받은 동사 효과. 변신으로 계산을 처음부터 다시 해도 닿은 순간의 결과가 남게 한다
        private readonly List<(RuleEntity target, WordId word)> _latchedTouch = new();
        // 한쪽만 Exit가 온 쌍. 상대 목록은 그대로 두고, 만지면 판정에서만 이미 떨어진 것으로 본다
        private readonly HashSet<(RuleEntity, RuleEntity)> _endedTouchPairs = new();
        private readonly HashSet<WordId> _conditionWords = new();
        private readonly HashSet<WordId> _touchSubjects = new();
        private static readonly WordId TreadCondition = new WordId("Tread");
        private bool _usesTouchCondition;
        private bool _pendingControlledDeath;
        private bool _started;
        private bool _rebuildPending;
        private bool _warnedMissingSpecPanel;
        private bool _cleared;

        private bool _controlledDied;    // 이번 적용에서 조종받던 대상이 Die로 죽었는지
        private bool _allControlledDead; // 클리어를 막기 위한 최소 규칙 상태


        public static RuleManager Instance { get; private set; }
        public static event Action<RuleManager> Available;
        public event Action<bool> ControlledDeathChanged;
        public event Action Reset;
        public bool IsCleared => _cleared;
        public bool AllControlledDead => _allControlledDead;

        /// <summary>스테이지를 클리어했을 때 알린다. 저장, 다음 스테이지 이동 등은 여기에 구독한다.</summary>
        public event Action Cleared;

        public IReadOnlyList<Sentence> Sentences => _sentences;
        public IReadOnlyCollection<string> TemplateIds => _templates.Keys;

        /// <summary>지금 조종받는 대상 전부. 카메라가 이 대상들을 한 화면에 담는다.</summary>
        public EntitySelection GetControlled() => new EntitySelection(_entities, default, true);

        // 직접 foreach 하면 열거자까지 값 형식이라 할당하지 않는다. 기존 IEnumerable 사용도 지원한다.
        public readonly struct EntitySelection : IEnumerable<RuleEntity>
        {
            private readonly List<RuleEntity> _source;
            private readonly WordId _id;
            private readonly bool _controlled;
            internal EntitySelection(List<RuleEntity> source, WordId id, bool controlled)
            { _source = source; _id = id; _controlled = controlled; }
            public Enumerator GetEnumerator() => new Enumerator(_source, _id, _controlled);
            IEnumerator<RuleEntity> IEnumerable<RuleEntity>.GetEnumerator() => GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            public struct Enumerator : IEnumerator<RuleEntity>
            {
                private List<RuleEntity>.Enumerator _inner;
                private readonly WordId _id;
                private readonly bool _controlled;
                internal Enumerator(List<RuleEntity> source, WordId id, bool controlled)
                { _inner = source.GetEnumerator(); _id = id; _controlled = controlled; Current = null; }
                public RuleEntity Current { get; private set; }
                object System.Collections.IEnumerator.Current => Current;
                public bool MoveNext()
                {
                    while (_inner.MoveNext())
                    {
                        var e = _inner.Current;
                        if (e == null || e.IsRemoved || (_controlled ? !e.IsControlled : e.Id != _id)) continue;
                        Current = e;
                        return true;
                    }
                    Current = null;
                    return false;
                }
                public void Dispose() => _inner.Dispose();
                public void Reset() => throw new NotSupportedException();
            }
        }

        private void Awake()
        {
            _effects = new VerbEffectCollection(OnEffectDeath);
            Instance = this;
            Available?.Invoke(this);
            // 빌드에서는 경로로 에셋을 읽을 수 없어 참조로 넘겨준다
            WordTags.Initialize(_wordCsv);
            LoadTemplates();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEffectDeath(RuleEntity entity)
        {
            if (entity.IsControlled || _effects.WillControl(entity)) _controlledDied = true;
        }

        private void Start()
        {
            _started = true;
            Rebuild();
        }

        private void OnEnable()
        {
            if (_specPanel == null) _specPanel = FindAnyObjectByType<SpecPanel>();
            if (_specPanel == null)
            {
                if (!_warnedMissingSpecPanel) Debug.LogWarning("[RuleManager] SpecPanel이 없어 빌드·리트 이벤트를 구독하지 못함", this);
                _warnedMissingSpecPanel = true;
                return;
            }
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
            ResetSession();
            // 다시 깔린 객체가 등록될 때 이전 문장으로 변신하지 않도록 문장부터 바꾼다
            FillSentences(sentences);
            if (_mapLoader != null) _mapLoader.Reload();
            else Debug.LogError("[RuleManager] 씬에 MapLoader가 없어 레벨을 초기화하지 못함");
            ReapplyAll();
        }

        private void ResetSession()
        {
            _cleared = false;
            _allControlledDead = false;
            Reset?.Invoke();
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
            CacheSentenceDependencies();
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
            ResetSession();
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
            CacheSentenceDependencies();
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
            _controlledDied = _pendingControlledDeath;
            _pendingControlledDeath = false;
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
                _endedTouchPairs.Clear();
            }
            // 사망하면 문장으로 되살아나 조종을 받을 수도 있으니 적용이 다 끝난 뒤에 따진다
            CheckControlledDeath();
        }

        // UI 처리는 하지 않고, 모든 규칙 적용 후 조종 대상의 사망·부활 상태만 알린다.
        private void CheckControlledDeath()
        {
            bool anyControlled = false;
            foreach (var _ in GetControlled()) { anyControlled = true; break; }
            if (_allControlledDead && anyControlled)
            {
                _allControlledDead = false;
                ControlledDeathChanged?.Invoke(false);
                return;
            }
            if (_allControlledDead || _cleared || !_controlledDied || anyControlled) return;
            _allControlledDead = true;
            ControlledDeathChanged?.Invoke(true);
        }

        private void ReapplyOnce()
        {
            foreach (var e in _entities) if (e != null) e.ResetState();
            _latchedTouch.Clear();
            // 변신을 먼저 해야 '사람은 비둘기다 + 비둘기는 움직인다'에서 바뀐 사람도 움직인다
            // 조건 변신이 일어나면 바뀐 태그로 변신·효과 문장을 처음부터 다시 따진다 (변신은 영구라 반드시 끝난다)
            for (int round = 0; round < 8; round++)
            {
                foreach (var s in _sentences) if (s.Kind == SentenceKind.Transform) ApplyTransform(s);
                if (!CollectEffects()) break;
            }
            ApplyEffects();
        }

        // 지속 효과마다 받아야 할 대상을 모두 모은 뒤 한 번에 켜고 끈다.
        // 조건 계산 중에는 대상을 건드리지 않고 집합에만 모아서, 중간 상태로 켰다 껐다 하지 않는다
        // 조건의 주어가 밟힐 수 있도록 만드는 파생 상태다. 동사 결과 집합은 효과 저장소에 있다.
        private readonly HashSet<RuleEntity> _treadableSet = new();
        private bool _conditionalTransformed;

        /// <returns>조건 문장으로 변신이 일어났으면 true</returns>
        private bool CollectEffects()
        {
            _conditionalTransformed = false;
            _pendingTransforms.Clear();
            _effects.Clear();

            // 기본 속성은 문장과 상관없이 늘 먼저 넣는다. 조건 문장(도착지점이면, 조종 할 수 있으면)도 이걸 본다
            ApplyInnateEffects();
            foreach (var s in _sentences)
                if (s.Kind == SentenceKind.Effect) ApplyEffect(s);
            // 가시가 비둘기로 바뀐 뒤 다시 계산해도, 가시였을 때 사람에게 준 사망은 그대로 둔다
            foreach (var (target, word) in _latchedTouch)
                if (target != null) AddEffect(target, word);

            // 조건이 다른 조건 문장의 결과에 기댈 수 있어서(날면 움직인다 + 움직이면 조종) 더 바뀌지 않을 때까지 몇 번 돈다
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                foreach (var s in _sentences)
                {
                    if (s.Kind == SentenceKind.Conditional) changed |= ApplyConditional(s);
                    else if (s.Kind == SentenceKind.OnTouch) CollectTouchSentence(s);
                    // 조건으로 변신하면 바로 처음부터 다시 계산한다. 이번 계산의 효과는 변신 전 모습 기준이라 남겨 두면
                    // '사람은 난다'로 받은 날기가 비둘기로 바뀐 뒤에도 남아 '비둘기는 날면 사람이다'가 바로 걸린다
                }
                changed |= FlushTouchResults();
                // 사망 우선: 변신은 이번 바퀴의 사망 판정을 다 모은 뒤에 한다. 문장 순서 때문에 변신이 먼저 일어나
                // 죽어야 할 대상이 다른 모습으로 빠져나가지 않게 한다. 만지면으로 받은 동사(사망 등)는 _latchedTouch로 다음 계산에도 남는다
                // 변신하면 처음부터 다시 계산한다. 이번 계산의 효과는 변신 전 모습 기준이라 남겨 두면 안 된다
                if (_conditionalTransformed)
                {
                    if (FlushPendingTransforms()) return true;
                    _conditionalTransformed = false; // 모두 사망에 막혔다
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
            _effectSnapshot.Clear();
            _effectSnapshot.AddRange(_entities);
            foreach (var e in _effectSnapshot)
            {
                if (e == null || e.IsRemoved) continue;
                e.SetTreadable(_treadableSet.Contains(e));
                _effects.Apply(e);
            }
        }

        // 변신까지 끝난 지금 태그 기준으로 "밟히면" 문장의 주어를 모은다. 주어가 없는 문장(밟히면 …)은 모든 대상이다
        private void CollectTreadable()
        {
            _treadableSet.Clear();
            foreach (var s in _sentences)
            {
                int condSlot = s.ConditionSlot;
                if (condSlot < 0 || !_effects.RequiresSolidSubject(s.Words[condSlot])) continue;
                bool hasSubject = condSlot > 0;
                if (hasSubject && !WordTags.Is(s.Words[0], WordType.Noun)) continue;
                foreach (var e in _entities)
                    if (e != null && !e.IsRemoved && (!hasSubject || e.Id == s.Words[0])) _treadableSet.Add(e);
            }
        }

        // ---- 문장 결과를 해석한다. 새 동사는 Effects의 등록부에 추가한다 ----

        private void ApplyInnateEffects()
        {
            if (_innateEffects == null) return;
            foreach (var innate in _innateEffects)
            {
                if (innate == null || innate.Noun.IsEmpty || innate.Verbs == null) continue;
                foreach (var e in GetEntities(innate.Noun))
                    foreach (var verb in innate.Verbs)
                        if (!verb.IsEmpty) AddEffect(e, verb);
            }
        }
        private void ApplyEffect(Sentence s)
        {
            if (s.Words.Count == 0 || !WordTags.Is(s.Words[0], WordType.Noun)) return;
            var subject = s.Words[0];

            for (int i = 1; i < s.Words.Count; i++)
                foreach (var e in GetEntities(subject)) AddEffect(e, s.Words[i]);
        }

        private bool AddEffect(RuleEntity entity, WordId verb) => _effects.Add(entity, verb);

        private bool HasEffect(RuleEntity entity, WordId verb) => _effects.IsActive(entity, verb);

        // 조건과 닿음 문장이 공유하는 결과 처리. 명사는 영구 변신, 동사는 이번 계산의 효과다.
        private bool ApplyResult(RuleEntity entity, WordId word, bool fromDeath = false)
        {
            if (entity == null || entity.transform.root.name != "Level") return false;
            if (!WordTags.Is(word, WordType.Noun)) return AddEffect(entity, word);
            if (entity.Id == word) return false;
            // 변신 직후 잠금 중이면 바뀌지 않으니 변신으로 치지 않는다 (치면 같은 계산을 쓸데없이 되풀이한다)
            if (!entity.CanTransform) return false;
            if (FindOriginalEntity(word) == null) return false;
            // 한 대상에 변신이 여럿 걸리면 처음 것만 쓴다 (바로 변신하던 때도 첫 변신 뒤 다시 계산해서 처음 것이 이겼다)
            foreach (var p in _pendingTransforms) if (p.entity == entity) return false;
            _pendingTransforms.Add((entity, word, fromDeath));
            _conditionalTransformed = true;
            return true;
        }

        private readonly List<(RuleEntity entity, WordId word, bool fromDeath)> _pendingTransforms = new();

        /// <summary>예약한 변신을 한다. 이번 계산에서 죽을 대상은 변신하지 않는다(사망 우선).</summary>
        /// <returns>실제로 변신한 대상이 있으면 true</returns>
        private bool FlushPendingTransforms()
        {
            bool any = false;
            foreach (var (e, word, fromDeath) in _pendingTransforms)
            {
                if (e == null || e.Id == word || !e.CanTransform) continue;
                // "사망하면 ~가 된다"는 죽음이 변신의 조건이라 막지 않는다. 그 밖에는 사망이 걸린 산 대상이면 변신을 버린다
                if (!fromDeath && !e.IsRemoved && HasEffect(e, DieConditionId)) continue;
                var source = FindOriginalEntity(word);
                if (source == null) continue;
                e.TransformInto(word, source);
                // "사망하면" 변신은 죽기 직전(사망이 걸린 순간)에 일어나서 사망 상태를 거치지 않기도 한다.
                // 이것도 죽었다 살아난 것이라 무적을 줘야 바뀐 모습이 같은 가시에 바로 또 죽지 않는다
                if (fromDeath) e.StartTransformInvincible();
                any = true;
            }
            _pendingTransforms.Clear();
            return any;
        }

        private static readonly WordId TouchIgnoredId = new("Wall");
        private static readonly WordId DieConditionId = new("Die");

        /// <summary>
        /// 사람은 만지면 비둘기가 된다: 첫 칸(명사) 대상에 지금 닿아 있는 다른 대상이 ac 칸의 효과를 받는다.
        /// 명사면 영구 변신, 동사면 이번 계산에서만 효과를 줘서 떨어지면 다음 적용 때 풀린다.
        /// 결과는 바로 적용하지 않고 모아 둔다. 모든 만지면 문장을 닿은 순간의 모습으로 같이 판정하려는 것이다.
        /// </summary>
        private void BuildTouchedBy()
        {
            foreach (var list in _touchedBy.Values)
            {
                list.Clear();
                _touchListPool.Push(list);
            }
            _touchedBy.Clear();
            foreach (var o in _touchSnapshot)
            {
                if (o == null || o.IsRemoved) continue;
                _reverseTouch.Clear();
                o.GetTouchingEntities(_reverseTouch);
                foreach (var t in _reverseTouch)
                {
                    if (!_touchedBy.TryGetValue(t, out var list))
                    {
                        list = _touchListPool.Count > 0 ? _touchListPool.Pop() : new List<RuleEntity>();
                        _touchedBy[t] = list;
                    }
                    list.Add(o);
                }
            }
        }

        private void CollectTouchSentence(Sentence s)
        {
            int acSlot = s.AcSlot;
            if (acSlot <= 0 || !WordTags.Is(s.Words[0], WordType.Noun)) return;
            WordId subject = s.Words[0], word = s.Words[acSlot];

            _touchSnapshot.Clear();
            _touchSnapshot.AddRange(_entities);

            // 닿음 이벤트는 양쪽에 따로 들어와서 한쪽만 먼저 잡힐 수 있다. 상대가 나를 만지고 있어도 닿은 것으로 친다.
            // 주어마다 모든 대상의 접촉 목록을 다시 훑으면 주어 수 x 대상 수만큼 돌아 몬스터가 많을 때 렉이 걸리므로, 역방향 목록을 한 번만 만든다
            BuildTouchedBy();

            // 변신하면 목록이 달라지므로 먼저 복사한다
            foreach (var e in _touchSnapshot)
            {
                if (e == null || e.IsRemoved || e.Id != subject) continue;
                _touchers.Clear();
                e.GetTouchingEntities(_touchers);
                if (_touchedBy.TryGetValue(e, out var reverse))
                {
                    foreach (var o in reverse)
                        if (o != e && !o.IsRemoved && !_touchers.Contains(o)) _touchers.Add(o);
                }
                foreach (var t in _touchers)
                {
                    // 벽은 맵 어디에나 붙어 있어서 닿은 것으로 치면 걷기만 해도 조건이 걸린다. 만지면 대상에서 뺀다
                    if (t.IsRemoved || t.Id == TouchIgnoredId) continue;
                    if (_endedTouchPairs.Contains((e, t))) continue;
                    _pendingTouch.Add((t, word));
                }
            }
        }

        /// <summary>모아 둔 만지면 결과를 한꺼번에 적용한다. 동사(사망 등)를 먼저 주고 변신은 그 뒤에 한다.</summary>
        /// <returns>새로 효과를 받은 대상이 있거나 변신이 일어났으면 true</returns>
        private bool FlushTouchResults()
        {
            bool changed = false;
            foreach (var (target, word) in _pendingTouch)
            {
                if (WordTags.Is(word, WordType.Noun)) continue;
                if (!_latchedTouch.Contains((target, word))) _latchedTouch.Add((target, word));
                changed |= ApplyResult(target, word);
            }
            foreach (var (target, word) in _pendingTouch)
                if (WordTags.Is(word, WordType.Noun)) changed |= ApplyResult(target, word);
            _pendingTouch.Clear();
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
            int condSlot = s.ConditionSlot;
            bool hasSubject = condSlot > 0;
            if (hasSubject && !WordTags.Is(s.Words[0], WordType.Noun)) return false;
            WordId subject = hasSubject ? s.Words[0] : default, condition = s.Words[condSlot];
            int firstResult = hasSubject ? 1 : 0;
            bool changed = false;

            // 버튼은 밟히면 벽은 출구가 된다: 조건 칸과 ac 칸 사이에 명사 칸이 있으면 그 명사가 결과를 받는다.
            // 이 칸이 없는 기존 조건문(사람은 날면 비둘기이다 등)은 아래 원래 처리를 그대로 탄다
            int targetSlot = FindConditionTargetSlot(s);
            if (targetSlot >= 0) return ApplyConditionalToTarget(s, subject, hasSubject, condition, targetSlot);

            // 변신하면 목록이 달라지므로 먼저 복사한다
            _conditionalSnapshot.Clear();
            _conditionalSnapshot.AddRange(_entities);
            foreach (var e in _conditionalSnapshot)
            {
                if (e == null) continue;
                // 사망한 대상은 "사망하면" 조건에만 걸린다 (변신하면 그 모습으로 다시 살아난다)
                if (e.IsRemoved && !_effects.AllowsRemovedCondition(condition)) continue;
                if (hasSubject && e.Id != subject) continue;
                if (!IsConditionMet(e, condition, this)) continue;

                for (int i = firstResult; i < s.Words.Count; i++)
                {
                    if (i == condSlot) continue;
                    var word = s.Words[i];
                    changed |= ApplyResult(e, word, condition == DieConditionId);
                }
            }
            return changed;
        }

        // 조건 칸 바로 뒤에서 ac 칸 앞까지의 명사 칸. 없으면 -1
        private static int FindConditionTargetSlot(Sentence s)
        {
            int cond = s.ConditionSlot, ac = s.AcSlot;
            if (cond < 0 || ac <= cond + 1) return -1;
            for (int i = cond + 1; i < ac; i++)
                if (WordTags.Is(s.Words[i], WordType.Noun)) return i;
            return -1;
        }

        // 주어 중 하나라도 조건이 맞으면 맵의 대상 명사 전부가 ac 칸의 결과를 받는다 (명사면 변신, 동사면 이번 계산의 효과)
        private bool ApplyConditionalToTarget(Sentence s, WordId subject, bool hasSubject, WordId condition, int targetSlot)
        {
            bool met = false;
            foreach (var e in _entities)
            {
                if (e == null) continue;
                if (e.IsRemoved && !_effects.AllowsRemovedCondition(condition)) continue;
                if (hasSubject && e.Id != subject) continue;
                if (IsConditionMet(e, condition, this)) { met = true; break; }
            }
            if (!met) return false;

            WordId target = s.Words[targetSlot], word = s.Words[s.AcSlot];
            bool changed = false;
            // 변신하면 목록이 달라지므로 먼저 복사한다
            _conditionalSnapshot.Clear();
            _conditionalSnapshot.AddRange(_entities);
            foreach (var t in _conditionalSnapshot)
            {
                if (t == null || t.IsRemoved || t.Id != target) continue;
                changed |= ApplyResult(t, word);
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
            _transformSnapshot.Clear();
            foreach (var e in GetEntities(from)) _transformSnapshot.Add(e);
            foreach (var e in _transformSnapshot) if (e.CanTransform) e.TransformInto(to, source);
        }

        // ---- 접촉 규칙. RuleEntity가 닿을 때마다 보고한다 ----

        /// <summary>두 대상이 닿았다. 양쪽 모두 보고하므로 같은 접촉이 두 번 올 수 있다.</summary>
        public void ReportContact(RuleEntity a, RuleEntity b)
        {
            // 기획서가 열려 있는 동안(미리보기 포함)은 클리어하지 않는다
            if (_specPanel != null && _specPanel.IsOpen) return;
            if (a == null || b == null || a.IsRemoved || b.IsRemoved) return;
            if (IsWinContact(a, b) || IsWinContact(b, a)) Clear();
        }

        // 조종 대상이 Win을 받은 대상에 닿았는지
        private bool IsWinContact(RuleEntity player, RuleEntity other) =>
            player.IsControlled && _effects.IsGoal(other);

        private void Clear()
        {
            if (_cleared || _allControlledDead) return;
            _cleared = true;
            Cleared?.Invoke();
        }

        // ---- 대상 관리 ----

        // 이미 바뀐 객체가 아니라 원래 그 태그였던 객체의 모습을 쓴다
        public RuleEntity FindOriginalEntity(WordId id)
        {
            foreach (var e in _entities)
                if (e != null && e.OriginalId == id) return e;
            // 맵에 그 태그로 배치된 게 없어도 변신할 수 있도록 프리팹에서 모습을 가져온다
            return GetPrefabTemplate(id);
        }

        private RuleEntity GetPrefabTemplate(WordId id)
        {
            if (_prefabTemplates.TryGetValue(id, out var cached) && cached != null) return cached;
#if UNITY_EDITOR
            // 인스펙터에서 목록을 안 채웠어도 에디터에서는 바로 되게 한다 (빌드에는 채운 목록이 필요하다)
            if (_transformPrefabs.Count == 0) FillTransformPrefabs();
#endif
            RuleEntity prefab = null;
            foreach (var p in _transformPrefabs)
                if (p != null && p.Id == id) { prefab = p; break; }
            if (prefab == null) return null;

            if (_templateRoot == null)
            {
                // Level 밖에 두어야 규칙 대상으로 등록되지 않는다
                _templateRoot = new GameObject("_TransformTemplates").transform;
                _templateRoot.SetParent(transform, false);
            }
            // 켜진 채로 만들어야 Awake가 돌아 원래 모습이 기록된다. 바로 꺼서 움직이거나 보이지 않게 한다
            var clone = Instantiate(prefab, new Vector3(0f, -10000f, 0f), Quaternion.identity, _templateRoot);
            clone.gameObject.SetActive(false);
            clone.name = prefab.name + " (변신 원본)";
            _prefabTemplates[id] = clone;
            return clone;
        }

#if UNITY_EDITOR
        private const string ObjectPrefabFolder = "Assets/_Project/Prefabs/Object";

        // 오브젝트 프리팹 폴더에서 RuleEntity가 붙은 프리팹을 모두 목록에 넣는다
        [ContextMenu("변신 프리팹 목록 자동 채우기")]
        private void FillTransformPrefabs()
        {
            _transformPrefabs.Clear();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { ObjectPrefabFolder }))
            {
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (go != null && go.TryGetComponent<RuleEntity>(out var entity)) _transformPrefabs.Add(entity);
            }
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        public Sprite FindOriginalSprite(WordId id) => FindOriginalEntity(id)?.OriginalSprite;

        public void Register(RuleEntity entity)
        {
            // 프리팹 원본이나 UI의 대상은 규칙에 섞지 않는다. 씬 루트 Level 아래만 등록한다.
            if (entity == null || entity.transform.root.name != "Level" || _entities.Contains(entity)) return;
            _entities.Add(entity);
            if (_started) ReapplyAll();
        }

        public void Unregister(RuleEntity entity) => _entities.Remove(entity);


        /// <summary>변신 직후 잠금이 끝났다. 그동안 막힌 변신이 아직 해당되면 이번에 일어나도록 다시 적용한다.</summary>
        public void NotifyTransformLockEnded(RuleEntity entity)
        {
            if (_started && entity != null) ReapplyAll();
        }

        /// <summary>변신 직후 무적이 끝났다. 그동안 막아 둔 사망이 아직 해당되면 이번에 죽도록 다시 적용한다.</summary>
        public void NotifyInvincibleEnded(RuleEntity entity)
        {
            if (_started && entity != null && !entity.IsRemoved) ReapplyAll();
        }

        /// <summary>누가 머리 위에 올라섰거나 내려갔다. "밟히면" 조건을 다시 따진다.</summary>
        public void NotifyTreadChanged(RuleEntity entity)
        {
            if (_started && _conditionWords.Contains(TreadCondition)) ReapplyAll();
        }

        /// <summary>
        /// 닿은 콜라이더가 늘거나 줄었다. touchedChanged는 '닿아 있음'이 바뀌었는지.
        /// '만지면' 조건이 바뀌었거나, 이 대상이나 상대가 닿으면 바꾸는 문장의 첫 칸일 때 다시 따진다.
        /// 닿음 이벤트는 한쪽에만 올 수 있어서, 주어(가시) 쪽 Exit가 없어도 날기 같은 지속 효과가 떨어질 때 풀리게 한다.
        /// </summary>
        public void NotifyTouchChanged(RuleEntity entity, bool touchedChanged, RuleEntity other = null, bool ended = false)
        {
            if (!_started) return;
            if (ended && entity != null && other != null && _touchSubjects.Count > 0
                && (_touchSubjects.Contains(entity.Id) || _touchSubjects.Contains(other.Id)))
            {
                _endedTouchPairs.Add((entity, other));
                _endedTouchPairs.Add((other, entity));
            }
            if (touchedChanged && _usesTouchCondition)
            {
                ReapplyAll();
                return;
            }
            if (_touchSubjects.Count == 0) return;
            if (entity != null && _touchSubjects.Contains(entity.Id)) ReapplyAll();
            else if (other != null && _touchSubjects.Contains(other.Id)) ReapplyAll();
        }

        // 문장 목록이 바뀔 때만 의존성을 모은다. 입력과 접촉 이벤트에서는 집합만 조회한다.
        private void CacheSentenceDependencies()
        {
            _conditionWords.Clear();
            _touchSubjects.Clear();
            _usesTouchCondition = false;
            foreach (var s in _sentences)
            {
                if (s.ConditionSlot >= 0)
                {
                    var condition = s.Words[s.ConditionSlot];
                    _conditionWords.Add(condition);
                    _usesTouchCondition |= _effects.ObservesTouch(condition);
                }
                // 만지면 문장만 첫 칸의 접촉을 지켜본다. 조건문의 ac 칸(버튼은 밟히면 벽은 출구가 된다)은 접촉과 상관없어서,
                // 넣으면 버튼에 무엇이 닿고 떨어질 때마다 전체 재계산이 돌아 렉이 걸린다
                if (s.AcSlot > 0 && s.ConditionSlot < 0) _touchSubjects.Add(s.Words[0]);
            }
        }
        /// <summary>플레이어가 움직이기 시작했거나 멈췄다. "움직이면" 조건을 다시 따진다.</summary>
        public void NotifyInputMovingChanged(RuleEntity entity)
        {
            if (_started && _conditionWords.Contains(WordId.Move)) ReapplyAll();
        }

        // 사망해 사라진 대상은 규칙 효과를 받지 않는다. 변신 원본 찾기(FindOriginalEntity)에서는 그대로 쓴다
        public EntitySelection GetEntities(WordId id) => new EntitySelection(_entities, id, false);
    }
}
