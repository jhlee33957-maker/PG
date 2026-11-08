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
    /// - 사망: Die 문장과 플레이어 공격은 똑같이 대상을 사망 상태(숨김)로 만든다. 사망 상태는 계속 남아서
    ///   "사망하면" 조건에 걸린다. 사망한 대상은 다른 조건과 효과는 받지 않지만, "사망하면" 조건으로 변신하면
    ///   새 모습으로 다시 살아나고 사망 상태는 풀린다. 공격으로 죽으면 그 순간에만 규칙을 다시 적용한다.
    /// - 플레이어 입력: 조종 대상을 좌우 이동·대쉬·점프·공격으로 움직이는 동안은 "움직이면" 조건에 걸린다
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
            _effects.Clear();

            foreach (var s in _sentences)
                if (s.Kind == SentenceKind.Effect) ApplyEffect(s);

            // 조건이 다른 조건 문장의 결과에 기댈 수 있어서(날면 움직인다 + 움직이면 조종) 더 바뀌지 않을 때까지 몇 번 돈다
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                foreach (var s in _sentences)
                {
                    if (s.Kind == SentenceKind.Conditional) changed |= ApplyConditional(s);
                    else if (s.Kind == SentenceKind.OnTouch) changed |= ApplyTouchSentence(s);
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
        private bool ApplyResult(RuleEntity entity, WordId word)
        {
            if (entity == null || entity.transform.root.name != "Level") return false;
            if (!WordTags.Is(word, WordType.Noun)) return AddEffect(entity, word);
            if (entity.Id == word) return false;
            var source = FindOriginalEntity(word);
            if (source == null) return false;
            entity.TransformInto(word, source);
            _conditionalTransformed = true;
            return true;
        }

        /// <summary>
        /// 사람은 만지면 비둘기가 된다: 첫 칸(명사) 대상에 지금 닿아 있는 다른 대상이 ac 칸의 효과를 받는다.
        /// 명사면 영구 변신, 동사면 이번 계산에서만 효과를 줘서 떨어지면 다음 적용 때 풀린다.
        /// </summary>
        /// <returns>새로 효과를 받은 대상이 있거나 변신이 일어났으면 true</returns>
        private bool ApplyTouchSentence(Sentence s)
        {
            int acSlot = s.AcSlot;
            if (acSlot <= 0 || !WordTags.Is(s.Words[0], WordType.Noun)) return false;
            WordId subject = s.Words[0], word = s.Words[acSlot];

            bool changed = false;
            _touchSnapshot.Clear();
            _touchSnapshot.AddRange(_entities);

            // 변신하면 목록이 달라지므로 먼저 복사한다
            foreach (var e in _touchSnapshot)
            {
                if (e == null || e.IsRemoved || e.Id != subject) continue;
                _touchers.Clear();
                e.GetTouchingEntities(_touchers);
                foreach (var t in _touchers)
                {
                    if (t.IsRemoved) continue;
                    changed |= ApplyResult(t, word);
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
            int condSlot = s.ConditionSlot;
            bool hasSubject = condSlot > 0;
            if (hasSubject && !WordTags.Is(s.Words[0], WordType.Noun)) return false;
            WordId subject = hasSubject ? s.Words[0] : default, condition = s.Words[condSlot];
            int firstResult = hasSubject ? 1 : 0;
            bool changed = false;

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
                    changed |= ApplyResult(e, word);
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
            _transformSnapshot.Clear();
            foreach (var e in GetEntities(from)) _transformSnapshot.Add(e);
            foreach (var e in _transformSnapshot) e.TransformInto(to, source);
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
            // 프리팹 원본이나 UI의 대상은 규칙에 섞지 않는다. 씬 루트 Level 아래만 등록한다.
            if (entity == null || entity.transform.root.name != "Level" || _entities.Contains(entity)) return;
            _entities.Add(entity);
            if (_started) ReapplyAll();
        }

        public void Unregister(RuleEntity entity) => _entities.Remove(entity);

        /// <summary>공격으로 대상이 죽었다. Die 문장으로 죽은 것처럼 "사망하면" 조건을 다시 따진다.</summary>
        public void NotifyDied(RuleEntity entity)
        {
            if (!_started) return;
            // Remove로 조종이 풀린 뒤에도 직전 효과 할당으로 조종 대상의 사망을 놓치지 않는다.
            bool controlled = entity != null && (entity.IsControlled || _effects.WillControl(entity));
            // 사망은 접촉과 입력도 바꾸므로 조건 또는 닿음 문장이 하나라도 있으면 안전하게 재계산한다.
            if (_conditionWords.Count > 0 || _touchSubjects.Count > 0)
            {
                if (_reapplying) _controlledDied |= controlled;
                else _pendingControlledDeath |= controlled;
                ReapplyAll();
            }
            else
            {
                _controlledDied |= controlled;
                if (!_reapplying) CheckControlledDeath();
            }
        }

        /// <summary>누가 머리 위에 올라섰거나 내려갔다. "밟히면" 조건을 다시 따진다.</summary>
        public void NotifyTreadChanged(RuleEntity entity)
        {
            if (_started && _conditionWords.Contains(TreadCondition)) ReapplyAll();
        }

        /// <summary>
        /// 닿은 콜라이더가 늘거나 줄었다. touchedChanged는 '닿아 있음'이 바뀌었는지.
        /// '만지면' 조건이 바뀌었거나, 이 대상이 닿으면 바꾸는 문장의 첫 칸일 때만 다시 따진다.
        /// </summary>
        public void NotifyTouchChanged(RuleEntity entity, bool touchedChanged)
        {
            if (!_started) return;
            if ((touchedChanged && _usesTouchCondition) || _touchSubjects.Contains(entity.Id)) ReapplyAll();
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
                if (s.AcSlot > 0) _touchSubjects.Add(s.Words[0]);
            }
        }
        /// <summary>플레이어가 움직이기 시작했거나 멈췄다. "움직이면" 조건을 다시 따진다.</summary>
        public void NotifyInputMovingChanged(RuleEntity entity)
        {
            if (_started && _conditionWords.Contains(WordId.Move)) ReapplyAll();
        }

        // 공격으로 사라진 대상은 규칙 효과를 받지 않는다. 변신 원본 찾기(FindOriginalEntity)에서는 그대로 쓴다
        public EntitySelection GetEntities(WordId id) => new EntitySelection(_entities, id, false);
    }
}
