using System;
using System.Collections.Generic;
using UnityEngine;
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
        // Win 문장을 받은 대상. 조종 대상이 닿으면 클리어된다
        private readonly HashSet<RuleEntity> _winTargets = new();
        private bool _started;
        private bool _rebuildPending;
        private bool _cleared;


        private static readonly WordId Fly = new("Fly");
        private static readonly WordId Control = new("Control");
        private static readonly WordId Win = new("Win");
        private static readonly WordId Die = new("Die");

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

        // 이동 같은 지속 효과를 끈 뒤 문장을 다시 적용한다. 변신은 영구라 되돌리지 않는다
        private void ReapplyAll()
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

        private bool _conditionalTransformed;

        /// <returns>조건 문장으로 변신이 일어났으면 true</returns>
        private bool CollectEffects()
        {
            _conditionalTransformed = false;
            _moveSet.Clear();
            _flySet.Clear();
            _controlSet.Clear();
            _dieSet.Clear();
            _winTargets.Clear();

            foreach (var s in _sentences)
                if (!IsTransform(s) && ConditionSlot(s) < 0) ApplyEffect(s);

            // 조건이 다른 조건 문장의 결과에 기댈 수 있어서(날면 움직인다 + 움직이면 조종) 더 바뀌지 않을 때까지 몇 번 돈다
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                foreach (var s in _sentences)
                    if (ConditionSlot(s) >= 0) changed |= ApplyConditional(s);
                if (!changed) break;
            }
            return _conditionalTransformed;
        }

        // 모은 효과를 대상에 반영한다. 상태가 그대로인 대상은 각 Set 함수가 건드리지 않는다
        private void ApplyEffects()
        {
            // 사라지는 대상이 있으면 목록 순회 중에 상태가 바뀌므로 먼저 복사한다
            foreach (var e in new List<RuleEntity>(_entities))
            {
                if (e == null || e.IsRemoved) continue;
                // Die: 공격받았을 때처럼 숨기고 상호작용을 끈다. 영구라 문장이 빠져도 돌아오지 않는다
                if (_dieSet.Contains(e))
                {
                    e.Remove();
                    continue;
                }
                e.SetRandomMove(_moveSet.Contains(e));
                e.SetFly(_flySet.Contains(e));
                // 조종은 껐다 켜면 움직임이 끊기므로 바뀌는 대상만 켜고 끈다
                e.SetControl(_controlSet.Contains(e));
            }
        }

        // ---- 태그를 보고 효과를 정하는 부분. 새 효과는 여기에 추가한다 ----

        private static bool IsTransform(Sentence s) =>
            s.Words.Count == 2 && ConditionSlot(s) < 0 &&
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
            return false;
        }

        // 동사 조건: 그 대상이 지금 그 상태인지 (이번에 모으는 중인 효과 기준)
        private bool HasEffect(RuleEntity e, WordId verb)
        {
            if (verb == WordId.Move) return _moveSet.Contains(e);
            if (verb == Fly) return _flySet.Contains(e);
            if (verb == Control) return _controlSet.Contains(e);
            if (verb == Win) return _winTargets.Contains(e);
            if (verb == Die) return _dieSet.Contains(e);
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
                if (e == null || e.IsRemoved) continue;
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
            if (_cleared) return;
            _cleared = true;
            ClearScreen.Show();
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
            if (_entities.Contains(entity)) return;
            _entities.Add(entity);
            if (_started) ReapplyAll();
        }

        public void Unregister(RuleEntity entity) => _entities.Remove(entity);

        // 공격으로 사라진 대상은 규칙 효과를 받지 않는다. 변신 원본 찾기(FindOriginalEntity)에서는 그대로 쓴다
        public IEnumerable<RuleEntity> GetEntities(WordId id)
        {
            foreach (var e in _entities)
                if (e != null && !e.IsRemoved && e.Id == id) yield return e;
        }
    }
}