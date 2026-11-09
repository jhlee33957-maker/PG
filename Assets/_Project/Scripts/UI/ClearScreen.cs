using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 화면 가운데에 Clear!를 띄운다. 씬마다 UI를 미리 둘 필요 없게 처음 호출될 때 만든다.
    /// </summary>
    public static class ClearScreen
    {
        private const string Message = "Clear!";
        private const float FontSize = 120f;
        private const float SubFontSize = 48f;

        private static GameObject _current;

        /// <summary>떠 있는 Clear!를 지운다. 리트할 때 쓴다.</summary>
        public static void Hide()
        {
            if (_current != null) UnityEngine.Object.Destroy(_current);
            _current = null;
        }

        public static void Show() => ShowMessage(Message, null);


        private static void ShowMessage(string message, string subMessage)
        {
            Hide();
            var canvasGo = new GameObject("ClearCanvas");
            _current = canvasGo;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            // 안내 글씨는 한 텍스트 안에서 작은 크기로 줄을 바꿔 붙여, 두 줄을 합친 덩어리가 화면 정가운데에 오게 한다
            string content = string.IsNullOrEmpty(subMessage)
                ? message
                : $"{message}\n<size={SubFontSize}>{subMessage}</size>";
            AddText(canvasGo.transform, "ClearText", content, FontSize, new Vector2(0f, 0f), new Vector2(1f, 1f));
        }

        private static void AddText(Transform parent, string name, string message, float fontSize, Vector2 anchorMin, Vector2 anchorMax)
        {
            var textGo = new GameObject(name);
            textGo.transform.SetParent(parent, false);
            var rect = textGo.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = message;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.outlineWidth = 0.2f;
            text.outlineColor = Color.black;
        }
    }
}