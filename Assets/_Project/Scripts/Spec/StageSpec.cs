using System.Collections.Generic;
using CoffeeSpec.Rules;
using UnityEngine;

namespace CoffeeSpec.Spec
{
    /// <summary>
    /// 스테이지 하나의 기획서 구성: 어떤 문장 틀이 나오고, 어떤 단어 묶음을 주는지.
    /// 문장 틀과 단어는 규칙 시스템(강석)의 것을 그대로 쓴다. 이 에셋은 "고르는 것"만 한다.
    ///   - 문장 틀: SentencesCSV.csv의 Id (예: NounIsNoun → "{0}은(는) {1}이다")
    ///   - 단어 묶음: WordId 목록. 모든 빈칸이 같이 쓰며, 어떤 빈칸에든 어떤 단어든 넣을 수 있다
    /// 빈칸 번호는 기획서 전체에서 위 문장부터 0, 1, 2 ... 순서다.
    /// </summary>
    [CreateAssetMenu(fileName = "StageSpec", menuName = "CoffeeSpec/Stage Spec")]
    public class StageSpec : ScriptableObject
    {
        [Tooltip("문장 틀 CSV (Assets/_Project/Data/SentencesCSV.csv)")]
        [SerializeField] private TextAsset _sentenceCsv;

        [Tooltip("이 스테이지 기획서에 나오는 문장 틀 Id. 위에서부터 순서대로 표시된다")]
        [SerializeField] private List<string> _templateIds = new List<string>();

        [Tooltip("선택지 묶음. 같은 단어가 여러 번 필요하면 여러 개 넣는다")]
        [SerializeField] private List<WordId> _wordPool = new List<WordId>();

        public IReadOnlyList<WordId> WordPool => _wordPool;

        /// <summary>
        /// 문장 틀을 Id 순서대로 읽는다. CSV에 없는 Id는 건너뛰고 경고를 남긴다.
        /// </summary>
        public List<SentenceTemplate> LoadTemplates()
        {
            var result = new List<SentenceTemplate>();
            if (_sentenceCsv == null)
            {
                Debug.LogError($"[StageSpec] '{name}': 문장 틀 CSV가 할당되지 않았습니다.", this);
                return result;
            }

            Dictionary<string, SentenceTemplate> templates = SentenceTemplate.ParseCsv(_sentenceCsv.text);
            foreach (string rawId in _templateIds)
            {
                string id = rawId == null ? string.Empty : rawId.Trim();
                if (templates.TryGetValue(id, out SentenceTemplate template))
                {
                    result.Add(template);
                }
                else
                {
                    Debug.LogWarning($"[StageSpec] '{name}': CSV에 없는 문장 틀 Id '{id}'는 건너뜁니다.", this);
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
            foreach (string rawId in _templateIds)
            {
                string id = rawId == null ? string.Empty : rawId.Trim();
                if (id.Length == 0)
                {
                    continue;
                }

                if (templates.TryGetValue(id, out SentenceTemplate template))
                {
                    blankCount += template.Pattern.Count;
                }
                else
                {
                    Debug.LogWarning($"[StageSpec] '{name}': CSV에 없는 문장 틀 Id '{id}'", this);
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
