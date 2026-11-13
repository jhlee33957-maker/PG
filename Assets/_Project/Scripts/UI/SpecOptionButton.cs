using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 기획서 화면에 자유롭게 놓이는 단어 칩 하나. SpecPanel이 단어 묶음 수만큼 프리팹으로 생성한다.
    /// 끌어서 빈칸에 놓으면 문장에 합쳐지고 칩은 화면에서 사라진다. 빈칸이 아닌 곳에 놓으면 그 자리에 머문다.
    /// (빈칸에 들어간 단어를 꺼내려면 빈칸에서 끌면 된다)
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SpecOptionButton : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("단어가 표시될 텍스트")]
        [SerializeField] private TMP_Text _label;

        private CanvasGroup _canvasGroup;
        private Color _defaultLabelColor = new Color(0.541f, 0.565f, 0.6f, 1f);
        private bool _defaultLabelColorCaptured;

        /// <summary>단어 묶음에서의 번호.</summary>
        public int Index { get; private set; }

        public bool IsUsed { get; private set; }

        public event Action<SpecOptionButton, PointerEventData> DragBegan;
        public event Action<SpecOptionButton, PointerEventData> Dragged;
        public event Action<SpecOptionButton, PointerEventData> DragEnded;

        private bool _isDragging;

        public void Setup(int index, string text)
        {
            Index = index;
            if (_label != null)
            {
                if (!_defaultLabelColorCaptured)
                {
                    _defaultLabelColor = _label.color;
                    _defaultLabelColorCaptured = true;
                }
                _label.text = text;
            }
            else
            {
                Debug.LogWarning($"[SpecOptionButton] '{name}'의 Label이 할당되지 않아 단어가 표시되지 않습니다.", this);
            }
        }

        /// <summary>빈칸에 쓰인 단어는 문장에 합쳐져 있으므로 칩을 숨긴다.</summary>
        /// <summary>품사 색 테두리를 칩 배경 위, 글자 아래에 덧씌운다.</summary>
        public void SetOutline(Sprite outlineSprite, Color color)
        {
            if (outlineSprite == null)
            {
                Debug.LogWarning($"[SpecOptionButton] 테두리 스프라이트가 없어 '{name}'에 품사 색 테두리를 그리지 않습니다. SpecPanel의 Box Outline Sprite를 확인하세요.", this);
                return;
            }

            var outlineObject = new GameObject("TypeOutline", typeof(RectTransform));
            var rect = (RectTransform)outlineObject.transform;
            rect.SetParent(transform, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // 1px 링 스프라이트는 작은 화면에서 위·아래가 끊기므로, 화면 픽셀 두께가 보장되는 선으로 그린다
            var border = outlineObject.AddComponent<UILineBorder>();
            border.color = color;
            border.raycastTarget = false;

            // 끌기 입력은 루트 Image가 받는다. 문장 위에 겹칠 때만 SetOverlapStyle이 반투명 바탕을 깐다.
            // 루트가 링 스프라이트면 작은 화면에서 끊긴 점이 비치므로 투명하게만 둔다.
            var background = GetComponent<Image>();
            if (background != null && background.sprite == outlineSprite)
            {
                Color clear = background.color;
                clear.a = 0f;
                background.color = clear;
            }
        }

        /// <summary>
        /// 문장 글자 위에 자유롭게 겹쳐 둘 때만 반투명 바탕과 얕은 그림자를 깐다.
        /// 빈칸 안에 들어간 단어나 하단 바의 칩에는 쓰지 않는다 (문장 흐름을 가리지 않기 위해).
        /// 강조 외곽선이나 빛 번짐은 쓰지 않는다.
        /// </summary>
        public void SetOverlapStyle(Sprite fillSprite, Color fillColor, Color textColor, bool overlapping)
        {
            Image shadow = EnsureChildImage("ChipShadow");
            Image fill = EnsureChildImage("ChipFill");

            if (!overlapping)
            {
                shadow.gameObject.SetActive(false);
                fill.gameObject.SetActive(false);
                if (_label != null)
                {
                    _label.color = _defaultLabelColor;
                }
                return;
            }

            var shadowRect = (RectTransform)shadow.transform;
            shadowRect.SetAsFirstSibling();
            shadowRect.anchorMin = Vector2.zero;
            shadowRect.anchorMax = Vector2.one;
            shadowRect.offsetMin = new Vector2(3f, -4f);
            shadowRect.offsetMax = new Vector2(3f, -4f);
            ApplySprite(shadow, fillSprite);
            shadow.color = new Color(0f, 0f, 0f, 0.28f);
            shadow.raycastTarget = false;
            shadow.gameObject.SetActive(true);

            var fillRect = (RectTransform)fill.transform;
            fillRect.SetSiblingIndex(1);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            ApplySprite(fill, fillSprite);
            fillColor.a = 0.72f;
            fill.color = fillColor;
            fill.raycastTarget = false;
            fill.gameObject.SetActive(true);

            if (_label != null)
            {
                _label.color = textColor;
            }
        }

        private Image EnsureChildImage(string name)
        {
            Transform existing = transform.Find(name);
            if (existing != null)
            {
                return existing.GetComponent<Image>() ?? existing.gameObject.AddComponent<Image>();
            }

            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(transform, false);
            return child.AddComponent<Image>();
        }

        private static void ApplySprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        }

        public void SetUsed(bool used)
        {
            IsUsed = used;
            CanvasGroupComponent.alpha = 1f;
            gameObject.SetActive(!used);
        }

        private CanvasGroup CanvasGroupComponent
        {
            get
            {
                if (_canvasGroup == null)
                {
                    _canvasGroup = GetComponent<CanvasGroup>();
                }
                return _canvasGroup;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // 이미 빈칸에 쓰인 단어는 단어 바에서 끌 수 없다
            if (IsUsed)
            {
                eventData.pointerDrag = null;
                return;
            }
            _isDragging = true;
            CanvasGroupComponent.alpha = 0f; // 집어 든 동안에는 마우스를 따라가는 칩만 보인다
            DragBegan?.Invoke(this, eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_isDragging)
            {
                Dragged?.Invoke(this, eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_isDragging)
            {
                return;
            }
            _isDragging = false;
            CanvasGroupComponent.alpha = 1f;
            DragEnded?.Invoke(this, eventData);
        }
    }
}
