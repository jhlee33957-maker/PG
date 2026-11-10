using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 버튼에 마우스를 올리거나 누를 때 글자색과 테두리색을 바꾼다.
    /// Button의 Color Tint는 버튼 자신의 Image 하나만 바꿀 수 있어서, ESC 설정창처럼 글자·테두리가 주황으로 바뀌는 부분만 이 컴포넌트가 맡는다.
    /// 버튼 바탕색은 Button의 Color Tint로 정한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonHoverColors : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("색을 바꿀 글자. 비워 두면 자식에서 첫 TMP 글자를 찾아 쓴다")]
        [SerializeField] private TMP_Text _label;
        [Tooltip("색을 바꿀 테두리 (자식 Edge Image 등). 없으면 비워 둔다")]
        [SerializeField] private Graphic _border;

        [SerializeField] private Color _normalText = Hex(0xDCDFE4);
        [SerializeField] private Color _hoverText = Hex(0xE8913B);
        [SerializeField] private Color _pressedText = Hex(0xF5C189);
        [SerializeField] private Color _normalBorder = Hex(0x7FA3C8);
        [SerializeField] private Color _hoverBorder = Hex(0xE8913B);

        private Selectable _selectable;
        private bool _hover;
        private bool _pressed;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            if (_label == null)
            {
                _label = GetComponentInChildren<TMP_Text>(true);
            }
        }

        private void OnEnable()
        {
            Refresh();
        }

        // 누른 직후 버튼이 숨겨지거나 다른 자리로 옮겨지면 나가기 이벤트가 안 올 수 있어, 숨겨질 때 평소 색으로 되돌린다
        private void OnDisable()
        {
            _hover = false;
            _pressed = false;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
            _pressed = false;
            Refresh();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            _pressed = true;
            Refresh();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            Refresh();
        }

        private void Refresh()
        {
            // 버튼이 눌리지 않는 상태(Interactable 꺼짐)면 마우스를 올려도 평소 색 그대로 둔다
            bool active = _selectable == null || _selectable.IsInteractable();
            bool hover = active && _hover;
            bool pressed = active && _pressed;

            if (_label != null)
            {
                _label.color = pressed ? _pressedText : hover ? _hoverText : _normalText;
            }
            if (_border != null)
            {
                _border.color = hover || pressed ? _hoverBorder : _normalBorder;
            }
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
