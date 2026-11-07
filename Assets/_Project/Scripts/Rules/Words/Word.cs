using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoffeeSpec.Rules
{
    /// <summary>품사. 문장이 이 단어를 어느 자리에 받을 수 있는지 판단할 때 쓴다.</summary>
    public enum WordType
    {
        Noun,       // 명사
        Verb,       // 동사
        Conjunction // 접속사 (If 등)
    }

    /// <summary>
    /// 단어의 의미 태그. 단어 목록을 코드가 아니라 WordsCSV에서 관리하려고 enum 대신 문자열을 담는다.
    /// 값 비교와 Dictionary 키로 쓸 수 있게 값 타입으로 둔다.
    /// </summary>
    [Serializable]
    public struct WordId : IEquatable<WordId>
    {
        [SerializeField] private string _value;

        public WordId(string value) => _value = value?.Trim() ?? string.Empty;

        public string Value => _value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(_value);

        // 효과가 연결된 단어만 코드에 이름을 남긴다. 새 명사는 CSV만 고치면 된다
        public static readonly WordId Move = new("Move");

        // 예전 enum(Person=0, Pigeon=1, Move=2)으로 저장된 프리팹/씬 값을 읽어 오기 위한 표
        private static readonly string[] LegacyEnumNames = { "Person", "Pigeon", "Move" };

        public static WordId FromLegacy(int index) =>
            index >= 0 && index < LegacyEnumNames.Length ? new WordId(LegacyEnumNames[index]) : default;

        public static implicit operator WordId(string value) => new(value);
        public bool Equals(WordId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is WordId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(WordId a, WordId b) => a.Equals(b);
        public static bool operator !=(WordId a, WordId b) => !a.Equals(b);
        public override string ToString() => Value;
    }

    /// <summary>
    /// Data/WordsCSV.csv (Id,DisplayName,Type)에서 읽은 단어 조회표.
    /// 실행 중에는 RuleManager가 Initialize로 CSV를 넘겨주고, 에디터에서는 경로로 직접 읽는다.
    /// </summary>
    public static class WordTags
    {
        public const string EditorAssetPath = "Assets/_Project/Data/WordsCSV.csv";

        private static TextAsset _source;

        /// <summary>실행 중에 쓸 CSV를 지정한다. RuleManager.Awake에서 호출한다.</summary>
        public static void Initialize(TextAsset csv)
        {
            if (csv == null || csv == _source) return;
            _source = csv;
            _entries = null;
        }

        public sealed class Entry
        {
            public WordId Id;
            public string DisplayName;
            public WordType Type;
            // Type 뒤에 추가한 열(예: If). 열 이름 → 그 단어의 활용형
            public Dictionary<string, string> Forms = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>조건(~면) 활용형 열 이름. 이 열이 비어 있는 명사는 받침을 보고 자동으로 만든다.</summary>
        public const string IfForm = "If";

        private static readonly HashSet<string> WarnedForms = new();

        private static Dictionary<WordId, Entry> _entries;
        private static List<Entry> _ordered;

        public static IReadOnlyList<Entry> All { get { EnsureLoaded(); return _ordered; } }

        public static bool Exists(WordId id) { EnsureLoaded(); return _entries.ContainsKey(id); }

        public static WordType TypeOf(WordId id)
        {
            EnsureLoaded();
            return _entries.TryGetValue(id, out var e) ? e.Type : WordType.Noun;
        }

        // CSV에 없는 단어는 어떤 품사 자리에도 맞지 않게 해서 오타를 문장 검증에서 잡는다
        public static bool Is(WordId id, WordType type)
        {
            EnsureLoaded();
            return _entries.TryGetValue(id, out var e) && e.Type == type;
        }

        // 플레이어에게 보일 단어. 문장 틀의 빈칸에 들어간다
        public static string DisplayName(WordId id)
        {
            EnsureLoaded();
            return _entries.TryGetValue(id, out var e) ? e.DisplayName : id.Value;
        }

        /// <summary>
        /// 활용형 이름(WordsCSV의 열 이름)에 맞는 말. 칸에 적힌 말이 있으면 그대로 쓴다.
        /// 비어 있으면 명사의 If형만 받침을 보고 만들고(사람이, 비둘기), 나머지는 경고 후 표시 이름을 쓴다.
        /// '면'은 문장 틀에 직접 적으므로({1@If}면) 여기서는 붙이지 않는다. 동사 칸에는 '날'처럼 '면' 앞까지만 적는다.
        /// form이 비어 있으면 표시 이름을 돌려준다.
        /// </summary>
        public static string Form(WordId id, string form)
        {
            if (string.IsNullOrEmpty(form)) return DisplayName(id);
            EnsureLoaded();
            if (!_entries.TryGetValue(id, out var e)) return id.Value;
            if (e.Forms.TryGetValue(form, out var text) && text.Length > 0) return text;

            if (e.Type == WordType.Noun && string.Equals(form, IfForm, StringComparison.OrdinalIgnoreCase))
                return e.DisplayName + (EndsWithFinalConsonant(e.DisplayName) ? "이" : "");

            // 같은 경고가 매 프레임 쌓이지 않게 단어·활용형마다 한 번만 남긴다
            if (WarnedForms.Add($"{e.Id}@{form}"))
                Debug.LogWarning($"[WordTags] {e.Id}의 {form} 활용형이 WordsCSV에 비어 있어 표시 이름을 씀");
            return e.DisplayName;
        }

        // 마지막 글자에 받침이 있는지. 한글이 아니면 받침이 있는 것으로 보고 '이'를 붙인다
        private static bool EndsWithFinalConsonant(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            char last = word[word.Length - 1];
            if (last < '\uAC00' || last > '\uD7A3') return true;
            return (last - '\uAC00') % 28 != 0;
        }

        /// <summary>CSV가 바뀌었을 때 다시 읽는다. 에디터 임포트 훅에서 호출한다.</summary>
        public static void Reload() { _entries = null; EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (_entries != null) return;
            _entries = new Dictionary<WordId, Entry>();
            _ordered = new List<Entry>();
            WarnedForms.Clear();

            var csv = _source;
#if UNITY_EDITOR
            // 에디터에서는 Inspector 드롭다운이 RuleManager 없이도 목록을 보여줘야 해서 경로로 읽는다
            if (csv == null) csv = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(EditorAssetPath);
#endif
            if (csv == null)
            {
                // 빌드에서 RuleManager에 CSV를 연결하지 않으면 여기로 온다. 빈 목록으로 두고 경고만 남긴다
                Debug.LogWarning("[WordTags] WordsCSV가 없음. RuleManager의 Word Csv에 연결했는지 확인");
                return;
            }

            var lines = csv.text.Replace("\r", "").TrimStart('\uFEFF').Split('\n');
            // 헤더의 4번째 열부터는 활용형 이름이다 (예: Id,DisplayName,Type,If)
            var header = lines.Length > 0 ? lines[0].Split(',') : Array.Empty<string>();
            for (int i = 1; i < lines.Length; i++) // 첫 줄은 헤더
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                var cols = line.Split(',');
                if (cols.Length < 3)
                {
                    Debug.LogWarning($"[WordTags] {i + 1}번째 줄 형식 오류: {line}");
                    continue;
                }
                if (!Enum.TryParse(cols[2].Trim(), true, out WordType type))
                {
                    Debug.LogWarning($"[WordTags] {i + 1}번째 줄 품사를 알 수 없음: {cols[2]}");
                    continue;
                }
                var entry = new Entry { Id = new WordId(cols[0]), DisplayName = cols[1].Trim(), Type = type };
                for (int c = 3; c < cols.Length && c < header.Length; c++)
                {
                    // 열 이름은 'If'나 'If DisplayName' 둘 다 받는다
                    var formName = header[c].Trim();
                    if (formName.EndsWith(" DisplayName", StringComparison.OrdinalIgnoreCase))
                        formName = formName.Substring(0, formName.Length - " DisplayName".Length).Trim();
                    if (formName.Length > 0) entry.Forms[formName] = cols[c].Trim();
                }
                if (entry.Id.IsEmpty || _entries.ContainsKey(entry.Id))
                {
                    Debug.LogWarning($"[WordTags] 비었거나 중복된 Id: {cols[0]}");
                    continue;
                }
                _entries.Add(entry.Id, entry);
                _ordered.Add(entry);
            }
        }
    }
}