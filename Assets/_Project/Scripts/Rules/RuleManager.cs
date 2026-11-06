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
    /// - 접촉: 플레이어가 Exit 태그 객체에 닿으면 클리어
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
        private bool _started;
        private bool _rebuildPending;
        private bool _cleared;

        private static readonly WordId Exit = new("Exit");
        private static readonly WordId Fly = new("Fly");

        /// <summary>스테이지를 클리어했을 때 알린다. 저장, 다음 스테이지 이동 등은 여기에 구독한다.</summary>
        public event Action Cleared;

        public IReadOnlyList<Sentence> Sentences => _sentences;
        public IReadOnlyCollection<string> TemplateIds => _templates.Keys;

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
            foreach (var s in _sentences) if (IsTransform(s)) ApplyTransform(s);
            foreach (var s in _sentences) if (!IsTransform(s)) ApplyEffect(s);
        }

        // ---- 태그를 보고 효과를 정하는 부분. 새 효과는 여기에 추가한다 ----

        private static bool IsTransform(Sentence s) =>
            s.Words.Count == 2 &&
            WordTags.Is(s.Words[0], WordType.Noun) && WordTags.Is(s.Words[1], WordType.Noun);

        private void ApplyEffect(Sentence s)
        {
            if (s.Words.Count == 0 || !WordTags.Is(s.Words[0], WordType.Noun)) return;
            var subject = s.Words[0];

            for (int i = 1; i < s.Words.Count; i++)
            {
                if (s.Words[i] == WordId.Move) // 효과가 연결된 단어만 여기서 처리한다
                    foreach (var e in GetEntities(subject)) e.SetRandomMove(true);
                else if (s.Words[i] == Fly)
                    foreach (var e in GetEntities(subject)) e.SetFly(true);
            }
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
            if (IsExitContact(a, b) || IsExitContact(b, a)) Clear();
        }

        private static bool IsExitContact(RuleEntity player, RuleEntity other) =>
            player.IsPlayer && other.Id == Exit;

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