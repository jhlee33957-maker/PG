using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 기획서 텍스트(TMP)에서 시작한 끌기 입력을 SpecPanel로 전달한다 (빈칸에 들어간 단어를 끌어서 옮기거나 빼기).
    /// SpecPanel이 실행 중에 자동으로 붙이므로 직접 추가할 필요 없다.
    /// </summary>
    public class SpecTextClickRelay : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public event Action<PointerEventData> DragBegan;
        public event Action<PointerEventData> Dragged;
        public event Action<PointerEventData> DragEnded;

        public void OnBeginDrag(PointerEventData eventData)
        {
            DragBegan?.Invoke(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            Dragged?.Invoke(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            DragEnded?.Invoke(eventData);
        }
    }
}
