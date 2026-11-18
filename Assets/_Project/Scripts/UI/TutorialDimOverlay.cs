using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 튜토리얼용 화면 가림막. 화면 전체를 반투명하게 덮고, 지정한 사각형(구멍)만 뚫어서 아래 UI가 그대로 보이게 한다.
    /// 구멍 안을 누르면 입력이 아래로 지나가고, 구멍 밖은 입력을 막는다 (BlocksInput이 false면 어디서도 막지 않는다).
    /// 구멍 좌표는 이 오브젝트 RectTransform의 로컬 좌표다. TutorialDirector가 실행 중에 만든다.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TutorialDimOverlay : MaskableGraphic, ICanvasRaycastFilter
    {
        private readonly List<Rect> _holes = new List<Rect>();

        /// <summary>구멍 밖의 입력을 막을지.</summary>
        public bool BlocksInput { get; set; } = true;

        /// <summary>구멍을 바꾼다. 이전과 같으면 다시 그리지 않는다.</summary>
        public void SetHoles(IReadOnlyList<Rect> holes)
        {
            if (SameHoles(holes))
            {
                return;
            }
            _holes.Clear();
            if (holes != null)
            {
                _holes.AddRange(holes);
            }
            SetVerticesDirty();
        }

        private bool SameHoles(IReadOnlyList<Rect> holes)
        {
            int count = holes != null ? holes.Count : 0;
            if (count != _holes.Count)
            {
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                if (!Approximately(holes[i], _holes[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool Approximately(Rect a, Rect b)
        {
            return Mathf.Abs(a.xMin - b.xMin) < 0.5f && Mathf.Abs(a.yMin - b.yMin) < 0.5f
                && Mathf.Abs(a.xMax - b.xMax) < 0.5f && Mathf.Abs(a.yMax - b.yMax) < 0.5f;
        }

        // 화면에서 구멍을 뺀 영역을 세로 띠로 나눠 사각형들로 채운다
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect area = rectTransform.rect;

            var clipped = new List<Rect>(_holes.Count);
            var xs = new List<float> { area.xMin, area.xMax };
            foreach (Rect hole in _holes)
            {
                Rect r = Rect.MinMaxRect(
                    Mathf.Max(hole.xMin, area.xMin), Mathf.Max(hole.yMin, area.yMin),
                    Mathf.Min(hole.xMax, area.xMax), Mathf.Min(hole.yMax, area.yMax));
                if (r.width <= 0f || r.height <= 0f)
                {
                    continue;
                }
                clipped.Add(r);
                xs.Add(r.xMin);
                xs.Add(r.xMax);
            }
            xs.Sort();

            var spans = new List<Vector2>();
            for (int i = 0; i < xs.Count - 1; i++)
            {
                float x0 = xs[i];
                float x1 = xs[i + 1];
                if (x1 - x0 <= 0.01f)
                {
                    continue;
                }

                // 이 띠를 가로로 완전히 덮는 구멍들의 세로 구간
                spans.Clear();
                foreach (Rect r in clipped)
                {
                    if (r.xMin <= x0 + 0.01f && r.xMax >= x1 - 0.01f)
                    {
                        spans.Add(new Vector2(r.yMin, r.yMax));
                    }
                }
                spans.Sort((a, b) => a.x.CompareTo(b.x));

                float y = area.yMin;
                foreach (Vector2 span in spans)
                {
                    if (span.x > y)
                    {
                        AddQuad(vh, x0, y, x1, span.x);
                    }
                    y = Mathf.Max(y, span.y);
                }
                if (y < area.yMax)
                {
                    AddQuad(vh, x0, y, x1, area.yMax);
                }
            }
        }

        private void AddQuad(VertexHelper vh, float xMin, float yMin, float xMax, float yMax)
        {
            int start = vh.currentVertCount;
            Color32 c = color;
            vh.AddVert(new Vector3(xMin, yMin), c, Vector4.zero);
            vh.AddVert(new Vector3(xMin, yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(xMax, yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(xMax, yMin), c, Vector4.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        // 구멍 안이면 false를 돌려 입력이 아래 UI로 지나가게 한다
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!BlocksInput)
            {
                return false;
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out Vector2 local))
            {
                return true;
            }
            foreach (Rect hole in _holes)
            {
                if (hole.Contains(local))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
