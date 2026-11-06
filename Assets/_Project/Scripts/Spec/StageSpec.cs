using System.Collections.Generic;
using CoffeeSpec.Rules;
using UnityEngine;

namespace CoffeeSpec.Spec
{
    /// <summary>
    /// 스테이지 하나의 기획서 구성: 어떤 줄이 나오고, 어떤 단어 묶음을 주는지.
    /// 문장 틀과 단어는 규칙 시스템(강석)의 것을 그대로 쓴다. 이 에셋은 "고르는 것"만 한다.
    ///
    /// Lines (위에서부터 한 줄씩)
    ///   - 문장 틀 Id   : SentencesCSV.csv의 Id (예: NounIsNoun → "{0}은(는) {1}이다")
    ///   - 문장 틀 Id, 단어 단어 : 쉼표 뒤에 빈칸 순서대로 처음 채워 둘 단어를 띄어 쓴다. 비워 둘 빈칸은 "_"
    ///                       (예: "NounIsNoun, Person _" → 첫 빈칸에 Person, 둘째 빈칸은 빈칸). 쉼표가 없으면 모두 빈칸
    ///   - "//" 로 시작 : 주석 줄. 코드 에디터의 주석처럼 그대로 보여 준다 (예: "// Object & Role Definition")
    ///   - 빈 칸        : 빈 줄
    /// 단어 묶음: WordId 목록. 모든 빈칸이 같이 쓰며, 어떤 빈칸에든 어떤 단어든 넣을 수 있다.
    /// 빈칸 번호는 기획서 전체에서 위 문장부터 0, 1, 2 ... 순서다 (주석과 빈 줄은 세지 않는다).
    /// </summary>
    [CreateAssetMenu(fileName = "StageSpec", menuName = "CoffeeSpec/Stage Spec")]
    public class StageSpec : ScriptableObject
    {
        public const string CommentPrefix = "//";
        public const char PresetSeparator = ',';
        public const string EmptyPresetToken = "_";

        public enum LineKind
        {
            Sentence,
            Comment,
            Empty
        }

        /// <summary>기획서 한 줄.</summary>
        public sealed class Line
        {
            public LineKind Kind { get; }
            /// <summary>주석 줄의 글자. 문장 줄이면 null.</summary>
            public string Comment { get; }
            /// <summary>문장 줄의 문장 틀. 주석·빈 줄이면 null.</summary>
            public SentenceTemplate Template { get; }
            /// <summary>문장 줄에서 빈칸 순서대로 처음 채워 둘 단어. 비워 둘 빈칸은 빈 WordId. 적지 않았으면 빈 목록.</summary>
            public IReadOnlyList<WordId> Preset { get; }

            public Line(LineKind kind, string comment, SentenceTemplate template, IReadOnlyList<WordId> preset = null)
            {
                Kind = kind;
                Comment = comment;
                Template = template;
                Preset = preset ?? System.Array.Empty<WordId>();
            }
        }

        [Tooltip("문장 틀 CSV (Assets/_Project/Data/SentencesCSV.csv)")]
        [SerializeField] private TextAsset _sentenceCsv;

        [Tooltip("기획서 줄. 위에서부터 순서대로 표시된다.\n- 문장 틀 Id (예: NounIsNoun)\n- 문장 틀 Id, 처음 단어 (예: NounIsNoun, Person _ / _는 빈칸)\n- // 로 시작하면 주석 줄\n- 비워 두면 빈 줄")]
        [SerializeField] private List<string> _templateIds = new List<string>();

        [Tooltip("선택지 묶음. 같은 단어가 여러 번 필요하면 여러 개 넣는다")]
        [SerializeField] private List<WordId> _wordPool = new List<WordId>();

        public IReadOnlyList<WordId> WordPool => _wordPool;

        /// <summary>
        /// 기획서 줄을 순서대로 읽는다. CSV에 없는 문장 틀 Id는 건너뛰고 경고를 남긴다.
        /// </summary>
        public List<Line> LoadLines()
        {
            var result = new List<Line>();
            if (_sentenceCsv == null)
            {
                Debug.LogError($"[StageSpec] '{name}': 문장 틀 CSV가 할당되지 않았습니다.", this);
                return result;
            }

            Dictionary<string, SentenceTemplate> templates = SentenceTemplate.ParseCsv(_sentenceCsv.text);
            foreach (string raw in _templateIds)
            {
                string entry = raw == null ? string.Empty : raw.Trim();

                if (entry.Length == 0)
                {
                    result.Add(new Line(LineKind.Empty, null, null));
                }
                else if (entry.StartsWith(CommentPrefix, System.StringComparison.Ordinal))
                {
                    result.Add(new Line(LineKind.Comment, entry, null));
                }
                else
                {
                    SplitSentenceEntry(entry, out string templateId, out List<WordId> preset);
                    if (templates.TryGetValue(templateId, out SentenceTemplate template))
                    {
                        result.Add(new Line(LineKind.Sentence, null, template, preset));
                    }
                    else
                    {
                        Debug.LogWarning($"[StageSpec] '{name}': CSV에 없는 문장 틀 Id '{templateId}'는 건너뜁니다.", this);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// "NounIsNoun, Person _" 를 문장 틀 Id와 처음 단어 목록으로 나눈다.
        /// 쉼표가 없으면 단어 목록은 비어 있다 (모든 빈칸이 빈칸으로 시작). "_"는 빈 WordId.
        /// </summary>
        private static void SplitSentenceEntry(string entry, out string templateId, out List<WordId> preset)
        {
            preset = new List<WordId>();
            int comma = entry.IndexOf(PresetSeparator);
            if (comma < 0)
            {
                templateId = entry.Trim();
                return;
            }

            templateId = entry.Substring(0, comma).Trim();
            string[] tokens = entry.Substring(comma + 1).Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                preset.Add(token == EmptyPresetToken ? default : new WordId(token));
            }
        }

#if UNITY_EDITOR
        // 데이터를 입력하는 순간 실수를 알려 준다
        private void OnValidate()
        {
            if (_sentenceCsv == null)
            {
                return;
            }

            Dictionary<string, SentenceTemplate> templates = SentenceTemplate.ParseCsv(_sentenceCsv.text);
            int blankCount = 0;
            // 처음 단어가 단어 묶음에서 몇 개씩 필요한지
            var presetNeed = new Dictionary<WordId, int>();
            foreach (string raw in _templateIds)
            {
                string entry = raw == null ? string.Empty : raw.Trim();
                if (entry.Length == 0 || entry.StartsWith(CommentPrefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                SplitSentenceEntry(entry, out string templateId, out List<WordId> preset);
                if (!templates.TryGetValue(templateId, out SentenceTemplate template))
                {
                    Debug.LogWarning($"[StageSpec] '{name}': CSV에 없는 문장 틀 Id '{templateId}'", this);
                    continue;
                }
                blankCount += template.Pattern.Count;

                if (preset.Count > template.Pattern.Count)
                {
                    Debug.LogWarning($"[StageSpec] '{name}': '{entry}'의 처음 단어({preset.Count}개)가 빈칸({template.Pattern.Count}개)보다 많아서 남는 단어는 무시됩니다.", this);
                }
                for (int i = 0; i < preset.Count && i < template.Pattern.Count; i++)
                {
                    if (preset[i].IsEmpty) continue;
                    presetNeed.TryGetValue(preset[i], out int need);
                    presetNeed[preset[i]] = need + 1;
                }
            }

            foreach (KeyValuePair<WordId, int> pair in presetNeed)
            {
                int have = 0;
                foreach (WordId word in _wordPool)
                {
                    if (word == pair.Key) have++;
                }
                if (have < pair.Value)
                {
                    Debug.LogWarning($"[StageSpec] '{name}': 처음 단어 '{pair.Key}'가 {pair.Value}번 쓰였는데 단어 묶음에는 {have}개뿐이라 모자란 빈칸은 비어서 시작합니다.", this);
                }
            }

            if (_wordPool.Count < blankCount)
            {
                Debug.LogWarning($"[StageSpec] '{name}': 단어({_wordPool.Count}개)가 빈칸({blankCount}개)보다 적어서 모든 빈칸을 채울 수 없습니다.", this);
            }
        }
#endif
    }
}
