using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 기획서 화면 하단의 단어 하나. SpecPanel이 단어 묶음 수만큼 프리팹으로 생성한다.
    /// 끌어서 빈칸에 놓는 방식이며, 이미 빈칸에 쓰인 단어는 반투명하게 보이고 끌 수 없다.
    /// (빈칸에 들어간 단어를 옮기려면 빈칸에서 끌면 된다)
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

        public void SetUsed(bool used, float usedAlpha)
        {
            IsUsed = used;
            CanvasGroupComponent.alpha = used ? usedAlpha : 1f;
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
            DragEnded?.Invoke(this, eventData);
        }
    }
}
