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
    ///   - "//" 로 시작 : 주석 줄. 코드 에디터의 주석처럼 그대로 보여 준다 (예: "// Object & Role Definition")
    ///   - 빈 칸        : 빈 줄
    /// 단어 묶음: WordId 목록. 모든 빈칸이 같이 쓰며, 어떤 빈칸에든 어떤 단어든 넣을 수 있다.
    /// 빈칸 번호는 기획서 전체에서 위 문장부터 0, 1, 2 ... 순서다 (주석과 빈 줄은 세지 않는다).
    /// </summary>
    [CreateAssetMenu(fileName = "StageSpec", menuName = "CoffeeSpec/Stage Spec")]
    public class StageSpec : ScriptableObject
    {
        public const string CommentPrefix = "//";

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

            public Line(LineKind kind, string comment, SentenceTemplate template)
            {
                Kind = kind;
                Comment = comment;
                Template = template;
            }
        }

        [Tooltip("문장 틀 CSV (Assets/_Project/Data/SentencesCSV.csv)")]
        [SerializeField] private TextAsset _sentenceCsv;

        [Tooltip("기획서 줄. 위에서부터 순서대로 표시된다.\n- 문장 틀 Id (예: NounIsNoun)\n- // 로 시작하면 주석 줄\n- 비워 두면 빈 줄")]
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
                else if (templates.TryGetValue(entry, out SentenceTemplate template))
                {
                    result.Add(new Line(LineKind.Sentence, null, template));
                }
                else
                {
                    Debug.LogWarning($"[StageSpec] '{name}': CSV에 없는 문장 틀 Id '{entry}'는 건너뜁니다.", this);
                }
            }
            return result;
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
            foreach (string raw in _templateIds)
            {
                string entry = raw == null ? string.Empty : raw.Trim();
                if (entry.Length == 0 || entry.StartsWith(CommentPrefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (templates.TryGetValue(entry, out SentenceTemplate template))
                {
                    blankCount += template.Pattern.Count;
                }
                else
                {
                    Debug.LogWarning($"[StageSpec] '{name}': CSV에 없는 문장 틀 Id '{entry}'", this);
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
