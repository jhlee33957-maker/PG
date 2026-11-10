using UnityEngine;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 사각형 테두리를 텍스처 없이 네 변의 막대로 그린다.
    /// 1px 링 스프라이트(UI_RoundRect_Outline)는 CanvasScaler가 화면을 줄이면 선이 1화면픽셀보다 가늘어져 위·아래가 점선처럼 끊겼다.
    /// 여기서는 두께를 캔버스 배율로 나눠 화면에서 언제나 최소 _minScreenPixels 픽셀이 되게 해서, 어떤 해상도에서도 끊기지 않는다.
    /// 1920x1080(배율 1)에서는 기존 링과 같은 1단위 두께다.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UILineBorder : MaskableGraphic
    {
        [Tooltip("캔버스 단위 기본 두께 (1920x1080 기준 픽셀)")]
        [SerializeField, Min(0f)] private float _thickness = 1f;
        [Tooltip("화면이 작아져도 이 화면 픽셀 수보다 가늘어지지 않는다")]
        [SerializeField, Min(0f)] private float _minScreenPixels = 1f;

        private float _builtScale = -1f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float t = Mathf.Min(CurrentThickness(), r.width * 0.5f, r.height * 0.5f);
            if (t <= 0f)
            {
                return;
            }

            // 위·아래 변은 가로 전체, 왼쪽·오른쪽 변은 그 사이만 그려 모서리가 겹쳐 진해지지 않게 한다
            AddQuad(vh, r.xMin, r.yMin, r.xMax, r.yMin + t);
            AddQuad(vh, r.xMin, r.yMax - t, r.xMax, r.yMax);
            AddQuad(vh, r.xMin, r.yMin + t, r.xMin + t, r.yMax - t);
            AddQuad(vh, r.xMax - t, r.yMin + t, r.xMax, r.yMax - t);
        }

        // 해상도를 바꾸면 캔버스 배율이 바뀌므로, 바뀌었을 때만 다시 그린다
        private void LateUpdate()
        {
            if (!Mathf.Approximately(RootScale(), _builtScale))
            {
                SetVerticesDirty();
            }
        }

        private float CurrentThickness()
        {
            _builtScale = RootScale();
            return _builtScale > 0f ? Mathf.Max(_thickness, _minScreenPixels / _builtScale) : _thickness;
        }

        private float RootScale()
        {
            Canvas c = canvas;
            return c != null ? c.rootCanvas.scaleFactor : 1f;
        }

        private void AddQuad(VertexHelper vh, float xMin, float yMin, float xMax, float yMax)
        {
            if (xMax <= xMin || yMax <= yMin)
            {
                return;
            }
            int start = vh.currentVertCount;
            Color32 c = color;
            vh.AddVert(new Vector3(xMin, yMin), c, Vector4.zero);
            vh.AddVert(new Vector3(xMin, yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(xMax, yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(xMax, yMin), c, Vector4.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
