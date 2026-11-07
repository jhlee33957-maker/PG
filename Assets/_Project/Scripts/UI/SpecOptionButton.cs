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

            var image = outlineObject.AddComponent<Image>();
            image.sprite = outlineSprite;
            image.type = outlineSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
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
