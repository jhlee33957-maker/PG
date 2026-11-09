using TMPro;
using UnityEngine;
namespace CoffeeSpec.UI
{
    public sealed class IntroPixelBlink : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _target;
        [SerializeField, Min(0.05f)] private float _interval = 0.5f;
        [Tooltip("마지막으로 내용이 있는 코드 줄 끝에 커서를 둔다")]
        [SerializeField] private TMP_Text[] _lines = new TMP_Text[0];
        [SerializeField] private float _cursorGap = 4f;
        private float _time;
        private void OnEnable() { _time = 0; if (_target != null) _target.alpha = 1; }
        private void LateUpdate()
        {
            _time += Time.unscaledDeltaTime;
            if (_target != null) _target.alpha = ((int)(_time / Mathf.Max(0.05f, _interval)) % 2 == 0) ? 1 : 0;
            TMP_Text line = null;
            foreach (TMP_Text candidate in _lines)
            {
                if (candidate == null) continue;
                if (line == null || !string.IsNullOrEmpty(candidate.text)) line = candidate;
            }
            if (line == null) return;
            RectTransform cursor = (RectTransform)transform;
            Vector3 end = line.rectTransform.TransformPoint(new Vector3(line.rectTransform.rect.xMin + line.preferredWidth + _cursorGap, 0, 0));
            Vector3 local = cursor.parent.InverseTransformPoint(end);
            cursor.localPosition = new Vector3(local.x, local.y, cursor.localPosition.z);
        }
    }
}
