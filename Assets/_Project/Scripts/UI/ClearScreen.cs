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

        public static void Show()
        {
            var canvasGo = new GameObject("ClearCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            var textGo = new GameObject("ClearText");
            textGo.transform.SetParent(canvasGo.transform, false);
            var rect = textGo.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = Message;
            text.fontSize = FontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.outlineWidth = 0.2f;
            text.outlineColor = Color.black;
        }
    }
}