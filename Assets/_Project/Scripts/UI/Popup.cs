using UnityEngine;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 팝업 공용 컴포넌트. 팝업의 루트 오브젝트에 붙이고, 우측 상단 닫기 버튼(BTN_Close)을 할당한다.
    /// 열기/닫기는 루트 오브젝트를 켜고 끄는 방식이다.
    /// </summary>
    public class Popup : MonoBehaviour
    {
        [Tooltip("우측 상단 닫기 버튼 (BTN_Close)")]
        [SerializeField] private Button _btnClose;

        [Tooltip("팝업이 그려지는 순서. 같은 캔버스의 다른 UI(화면 연출, 오버레이 등)보다 커야 위에 보이고 클릭된다")]
        [SerializeField] private int _sortingOrder = 100;

        private bool _isBound;

        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            BindCloseButton();
        }

        public void Open()
        {
            // 처음부터 꺼져 있던 팝업은 켜질 때 Awake가 불리므로, 그 전에 미리 연결해 둔다
            BindCloseButton();
            gameObject.SetActive(true);

            // 같은 부모 아래의 다른 UI보다 위에 그려지도록
            transform.SetAsLastSibling();
            BringToFront();
        }

        /// <summary>
        /// 팝업의 부모가 계층 앞쪽에 있으면 SetAsLastSibling만으로는 뒤의 UI에 가려진다.
        /// (예: Title 씬의 Popups가 TitleCanvas의 첫 번째 자식이라 모니터 화면과 DarkOverlay 아래에 깔림)
        /// 팝업에 별도 정렬 캔버스를 붙여 계층 위치와 상관없이 맨 위에 그리고, 클릭도 받게 한다.
        /// </summary>
        private void BringToFront()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.overrideSorting = true;
            canvas.sortingOrder = _sortingOrder;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void BindCloseButton()
        {
            if (_isBound)
            {
                return;
            }
            _isBound = true;

            if (_btnClose == null)
            {
                Debug.LogWarning($"[Popup] '{name}'에 BTN_Close가 할당되지 않았습니다.", this);
                return;
            }
            _btnClose.onClick.AddListener(Close);
        }
    }
}
