using System.Collections.Generic;
using UnityEngine;

namespace CoffeeSpec.Rules
{
    /// <summary>
    /// CSV 한 줄. 플레이어에게 보일 문장 틀과 빈칸에 들어갈 품사 순서를 가진다.
    /// </summary>
    public class SentenceTemplate
    {
        public string Id { get; }
        public string Text { get; }
        public IReadOnlyList<WordType> Pattern { get; }

        public SentenceTemplate(string id, string text, List<WordType> pattern)
        {
            Id = id;
            Text = text;
            Pattern = pattern;
        }

        /// <summary>CSV(Id,Text,Pattern)를 읽는다. Pattern은 Noun|Verb처럼 |로 구분한다.</summary>
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

                var pattern = new List<WordType>();
                bool ok = true;
                foreach (var p in line.Substring(b + 1).Split('|'))
                {
                    if (System.Enum.TryParse(p.Trim(), out WordType t)) pattern.Add(t);
                    else { Debug.LogWarning($"[SentenceTemplate] 알 수 없는 품사 '{p}' ({i + 1}번째 줄)"); ok = false; }
                }
                if (!ok) continue;

                var id = line.Substring(0, a).Trim();
                result[id] = new SentenceTemplate(id, line.Substring(a + 1, b - a - 1).Trim(), pattern);
            }
            return result;
        }
    }
}