using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CoffeeSpec.Rules
{
    /// <summary>
    /// CSV 한 줄. 플레이어에게 보일 문장 틀과 빈칸마다 받을 수 있는 품사 목록을 가진다.
    /// </summary>
    public class SentenceTemplate
    {
        /// <summary>
        /// 문구 안의 빈칸 {0}과 끝말 {1:Noun=이다}를 함께 찾는다.
        /// 끝말은 그 번호 빈칸에 들어간 단어의 품사에 맞는 말만 붙고, 맞는 품사가 없으면 아무것도 붙지 않는다.
        /// 여러 품사는 {1:Noun=이다,Verb=...}처럼 쉼표로 적는다.
        /// </summary>
        public static readonly Regex TokenPattern = new(@"\{(\d+)(?::([^}]*))?\}", RegexOptions.Compiled);

        public string Id { get; }
        public string Text { get; }
        // 빈칸마다 받을 수 있는 품사들. CSV에서 Noun/Verb처럼 /로 여러 개를 적는다
        public IReadOnlyList<IReadOnlyList<WordType>> Pattern { get; }

        public SentenceTemplate(string id, string text, List<IReadOnlyList<WordType>> pattern)
        {
            Id = id;
            Text = text;
            Pattern = pattern;
        }

        /// <summary>이 빈칸에 이 단어를 넣을 수 있는지. 단어의 품사가 빈칸의 품사 목록에 있으면 된다.</summary>
        public bool Accepts(int slot, WordId word)
        {
            if (slot < 0 || slot >= Pattern.Count) return false;
            foreach (var type in Pattern[slot])
                if (WordTags.Is(word, type)) return true;
            return false;
        }

        /// <summary>인스펙터 등에 보일 빈칸 품사 이름. 예: Noun/Verb</summary>
        public string SlotLabel(int slot) => string.Join("/", Pattern[slot]);

        /// <summary>끝말 규칙("Noun=이다,Verb=...")에서 이 단어의 품사에 맞는 말을 고른다. 없으면 빈 문자열.</summary>
        public static string ResolveEnding(string rule, WordId word)
        {
            if (string.IsNullOrEmpty(rule) || word.IsEmpty || !WordTags.Exists(word)) return string.Empty;
            var type = WordTags.TypeOf(word);
            foreach (var part in rule.Split(','))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                if (System.Enum.TryParse(part.Substring(0, eq).Trim(), true, out WordType t) && t == type)
                    return part.Substring(eq + 1);
            }
            return string.Empty;
        }

        /// <summary>CSV(Id,Text,Pattern)를 읽는다. Pattern은 빈칸을 |로, 한 빈칸의 여러 품사를 /로 구분한다 (예: Noun|Noun/Verb).</summary>
        public static Dictionary<string, SentenceTemplate> ParseCsv(string csv)
        {
            var result = new Dictionary<string, SentenceTemplate>();
            var lines = csv.Replace("\r", "").Split('\n');
            for (int i = 1; i < lines.Length; i++) // 첫 줄은 헤더
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                // 문장에 쉼표가 들어갈 수 있어서 첫 쉼표와 마지막 쉼표로만 나눈다
                int a = line.IndexOf(','), b = line.LastIndexOf(',');
                if (a < 0 || a == b) { Debug.LogWarning($"[SentenceTemplate] {i + 1}번째 줄 형식 오류: {line}"); continue; }

                var pattern = new List<IReadOnlyList<WordType>>();
                bool ok = true;
                foreach (var slot in line.Substring(b + 1).Split('|'))
                {
                    var types = new List<WordType>();
                    foreach (var p in slot.Split('/'))
                    {
                        if (System.Enum.TryParse(p.Trim(), out WordType t)) types.Add(t);
                        else { Debug.LogWarning($"[SentenceTemplate] 알 수 없는 품사 '{p}' ({i + 1}번째 줄)"); ok = false; }
                    }
                    pattern.Add(types);
                }
                if (!ok) continue;

                var id = line.Substring(0, a).Trim();
                result[id] = new SentenceTemplate(id, line.Substring(a + 1, b - a - 1).Trim(), pattern);
            }
            return result;
        }
    }
}