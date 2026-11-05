using System.Collections.Generic;

namespace CoffeeSpec.Rules
{
    /// <summary>
    /// 문장 틀(CSV)에 태그를 채운 완성 문장. 효과는 갖지 않고 RuleManager가 태그를 보고 정한다.
    /// </summary>
    public class Sentence
    {
        public SentenceTemplate Template { get; }
        public IReadOnlyList<WordId> Words { get; }

        public Sentence(SentenceTemplate template, List<WordId> words)
        {
            Template = template;
            Words = words;
        }

        // 빈칸 개수와 품사 순서가 CSV에 적힌 패턴과 같아야 성립한다
        public bool IsValid()
        {
            if (Template == null || Words.Count != Template.Pattern.Count) return false;
            for (int i = 0; i < Words.Count; i++)
                if (!WordTags.Is(Words[i], Template.Pattern[i])) return false;
            return true;
        }

        /// <summary>플레이어에게 보일 문장. 예: 사람은(는) 비둘기이다</summary>
        public string DisplayText
        {
            get
            {
                var args = new object[Words.Count];
                for (int i = 0; i < Words.Count; i++) args[i] = WordTags.DisplayName(Words[i]);
                return string.Format(Template.Text, args);
            }
        }

        public override string ToString() => $"{Template?.Id}({string.Join(", ", Words)})";
    }
}