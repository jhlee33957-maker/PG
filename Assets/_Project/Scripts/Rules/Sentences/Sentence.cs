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

        // 빈칸 개수가 같고, 단어마다 그 빈칸이 받는 품사 중 하나여야 성립한다
        public bool IsValid()
        {
            if (Template == null || Words.Count != Template.Pattern.Count) return false;
            for (int i = 0; i < Words.Count; i++)
                if (!Template.Accepts(i, Words[i])) return false;
            return true;
        }

        /// <summary>플레이어에게 보일 문장. 예: 사람은(는) 비둘기이다 / 사람은(는) 움직인다</summary>
        public string DisplayText =>
            SentenceTemplate.TokenPattern.Replace(Template.Text, m =>
            {
                int slot = int.Parse(m.Groups[1].Value);
                if (slot >= Words.Count) return m.Value;
                // {n}은 단어, {n@활용형}은 그 활용형(예: 날면), {n:품사=말}은 그 단어 품사에 맞는 끝말
                if (m.Groups[3].Success) return SentenceTemplate.ResolveEnding(m.Groups[3].Value, Words[slot]);
                return WordTags.Form(Words[slot], m.Groups[2].Success ? m.Groups[2].Value : null);
            });

        public override string ToString() => $"{Template?.Id}({string.Join(", ", Words)})";
    }
}