using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 기획서 클로즈업의 큰 문장 1~2줄.
    /// 문장 안의 {단어} 는 커피 방울이 떨어지면 빈칸 [ ____ ] 으로 바뀐다.
    /// 단어 순서: 첫 줄 왼쪽부터 → 둘째 줄 왼쪽부터.
    /// </summary>
    public class TitleDocSentence : MonoBehaviour
    {
        [Tooltip("문장을 표시할 TMP (줄마다 하나)")]
        [SerializeField] private TMP_Text[] _lines = new TMP_Text[0];

        [Tooltip("{ } 로 감싼 부분이 지워질 단어. 리치 텍스트 태그는 쓰지 않는다")]
        [SerializeField] private string[] _templates =
        {
            "{플레이어}은(는) {시안 네모}이다",
            "{빨간 네모}에 닿으면 {실패}한다",
        };

        [SerializeField] private string _blankText = "[ ____ ]";
        [SerializeField] private Color _blankColor = new Color32(0x5C, 0x63, 0x70, 0xFF);

        private class Word
        {
            public int Line;
            public string Text;
            public bool Erased;
            public int PlainStart;   // 현재 표시 문자열(태그 제외)에서의 시작 위치
            public int PlainLength;
        }

        private readonly List<Word> _words = new List<Word>();
        private readonly List<List<object>> _segments = new List<List<object>>(); // string 또는 Word

        public int WordCount
        {
            get { EnsureParsed(); return _words.Count; }
        }

        private void Awake()
        {
            EnsureParsed();
            RebuildAll();
        }

        private void OnValidate()
        {
            _words.Clear();
            _segments.Clear();
            if (_lines != null)
            {
                EnsureParsed();
                RebuildAll();
            }
        }

        public void ResetAll()
        {
            EnsureParsed();
            foreach (Word w in _words)
            {
                w.Erased = false;
            }
            RebuildAll();
        }

        public void EraseAll()
        {
            EnsureParsed();
            foreach (Word w in _words)
            {
                w.Erased = true;
            }
            RebuildAll();
        }

        public void EraseWord(int index)
        {
            EnsureParsed();
            if (index < 0 || index >= _words.Count)
            {
                return;
            }
            _words[index].Erased = true;
            RebuildLine(_words[index].Line);
        }

        /// <summary>단어(또는 빈칸)가 그려진 영역의 가운데 (월드 좌표).</summary>
        public Vector3 GetWordWorldCenter(int index)
        {
            EnsureParsed();
            if (index < 0 || index >= _words.Count)
            {
                return transform.position;
            }

            Word w = _words[index];
            if (_lines == null || w.Line >= _lines.Length || _lines[w.Line] == null)
            {
                return transform.position;
            }

            TMP_Text text = _lines[w.Line];
            text.ForceMeshUpdate();
            TMP_TextInfo info = text.textInfo;

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            int end = Mathf.Min(w.PlainStart + w.PlainLength, info.characterCount);
            for (int i = w.PlainStart; i < end; i++)
            {
                TMP_CharacterInfo ch = info.characterInfo[i];
                if (!ch.isVisible)
                {
                    continue;
                }
                minX = Mathf.Min(minX, ch.bottomLeft.x);
                maxX = Mathf.Max(maxX, ch.topRight.x);
                minY = Mathf.Min(minY, ch.bottomLeft.y);
                maxY = Mathf.Max(maxY, ch.topRight.y);
            }

            if (minX > maxX)
            {
                return text.transform.position;
            }
            return text.transform.TransformPoint(new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f));
        }

        private void EnsureParsed()
        {
            if (_segments.Count > 0 || _templates == null)
            {
                return;
            }

            for (int line = 0; line < _templates.Length; line++)
            {
                var segs = new List<object>();
                string tpl = _templates[line] ?? string.Empty;
                int i = 0;
                while (i < tpl.Length)
                {
                    int open = tpl.IndexOf('{', i);
                    int close = open >= 0 ? tpl.IndexOf('}', open + 1) : -1;
                    if (open < 0 || close < 0)
                    {
                        segs.Add(tpl.Substring(i));
                        break;
                    }
                    if (open > i)
                    {
                        segs.Add(tpl.Substring(i, open - i));
                    }
                    var word = new Word { Line = line, Text = tpl.Substring(open + 1, close - open - 1) };
                    _words.Add(word);
                    segs.Add(word);
                    i = close + 1;
                }
                _segments.Add(segs);
            }
        }

        private void RebuildAll()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                RebuildLine(i);
            }
        }

        private void RebuildLine(int line)
        {
            if (_lines == null || line >= _lines.Length || _lines[line] == null || line >= _segments.Count)
            {
                return;
            }

            string hex = ColorUtility.ToHtmlStringRGBA(_blankColor);
            var rich = new StringBuilder();
            int plain = 0;
            foreach (object seg in _segments[line])
            {
                if (seg is Word w)
                {
                    string shown = w.Erased ? _blankText : w.Text;
                    w.PlainStart = plain;
                    w.PlainLength = shown.Length;
                    plain += shown.Length;
                    if (w.Erased)
                    {
                        rich.Append("<color=#").Append(hex).Append('>').Append(shown).Append("</color>");
                    }
                    else
                    {
                        rich.Append(shown);
                    }
                }
                else
                {
                    string s = (string)seg;
                    rich.Append(s);
                    plain += s.Length;
                }
            }

            TMP_Text text = _lines[line];
            text.richText = true;
            text.text = rich.ToString();
        }
    }
}
