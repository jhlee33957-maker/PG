using UnityEngine;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 타이틀 메뉴의 OpenSettings(); 줄에 붙는 버튼. 누르면 인게임 ESC 메뉴와 같은 설정 창(PauseMenu)을 설정 부분만 띄운다.
    /// TitleMenuLine이 실행 연출 뒤 이 버튼의 onClick을 대신 눌러 준다. TitleUI를 고치지 않으려고 버튼 쪽에서 직접 연결한다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class TitleSettingsButton : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(PauseMenu.OpenSettings);
        }
    }
}
