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
