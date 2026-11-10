using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 화면 가운데에 네온 CLEAR 간판을 띄운다. 씬마다 UI를 미리 둘 필요 없게 처음 호출될 때 만든다.
    /// </summary>
    public static class ClearScreen
    {
        // 1920x1080 기준에서 1배 픽셀이 정수 배로 커지게. 99x43 간판 → 594x258.
        private const int PixelScale = 6;
        private const float DropHeight = 96f;
        private const float DropDuration = 0.35f;
        private const float SwingDegrees = 7f;
        // 꺼짐→켜짐→꺼짐→켜짐→꺼짐 뒤 점등. 합 0.60초. 값은 정해 두어 매 판 같다.
        private static readonly float[] FlickerHolds = { 0.08f, 0.12f, 0.18f, 0.10f, 0.12f };
        private const float GlowPulseMin = 0.35f;
        private const float GlowPulseMax = 0.85f;
        private const float GlowPulseSpeed = 2.2f;

        private static GameObject _current;
        private static MonoBehaviour _host;

        /// <summary>떠 있는 Clear 표시를 지운다. 리트할 때 쓴다.</summary>
        public static void Hide()
        {
            if (_host != null)
            {
                _host.StopAllCoroutines();
                _host = null;
            }

            if (_current != null)
            {
                Object.Destroy(_current);
                _current = null;
            }
        }

        public static void Show()
        {
            Hide();

            var art = ClearScreenSprites.Instance;
            if (art == null || art.Lit == null || art.Dark == null)
            {
                Debug.LogWarning("[ClearScreen] ClearScreenSprites가 없거나 간판 그림이 없어 표시하지 못했다");
                return;
            }

            var canvasGo = new GameObject("ClearCanvas");
            _current = canvasGo;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Vector2 size = art.Lit.rect.size * PixelScale;

            var rootGo = new GameObject("ClearSign");
            var root = rootGo.AddComponent<RectTransform>();
            root.SetParent(canvasGo.transform, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            // 체인 위 가로대를 축으로 살짝 흔들린다.
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = size;
            // 피벗이 위쪽이라, 간판 덩어리가 화면 한가운데 오도록 반 칸 올린다.
            root.anchoredPosition = new Vector2(0f, size.y * 0.5f);

            Image glow = null;
            if (art.Glow != null)
            {
                glow = AddImage(root, "Glow", art.Glow);
                glow.enabled = false;
                var glowColor = glow.color;
                glowColor.a = GlowPulseMin;
                glow.color = glowColor;
            }

            var sign = AddImage(root, "Sign", art.Dark);

            var host = canvasGo.AddComponent<ClearScreenHost>();
            _host = host;
            host.StartCoroutine(Play(root, sign, glow, art.Lit, art.Dark));
        }

        private static Image AddImage(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static IEnumerator Play(RectTransform root, Image sign, Image glow, Sprite lit, Sprite dark)
        {
            Vector2 rest = root.anchoredPosition;
            Vector2 start = rest + new Vector2(0f, DropHeight);
            root.anchoredPosition = start;
            root.localRotation = Quaternion.Euler(0f, 0f, -SwingDegrees);
            SetLit(sign, glow, lit, dark, false);

            float t = 0f;
            while (t < DropDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / DropDuration);
                const float overshoot = 1.35f;
                float u2 = u - 1f;
                float eased = 1f + u2 * u2 * ((overshoot + 1f) * u2 + overshoot);
                root.anchoredPosition = Vector2.LerpUnclamped(start, rest, eased);
                float swing = -SwingDegrees * (1f - u) * Mathf.Cos(u * Mathf.PI * 2.15f);
                root.localRotation = Quaternion.Euler(0f, 0f, swing);
                yield return null;
            }

            root.anchoredPosition = rest;
            root.localRotation = Quaternion.identity;

            bool on = false;
            for (int i = 0; i < FlickerHolds.Length; i++)
            {
                SetLit(sign, glow, lit, dark, on);
                yield return new WaitForSecondsRealtime(FlickerHolds[i]);
                on = !on;
            }

            SetLit(sign, glow, lit, dark, true);
            if (glow == null)
                yield break;

            glow.enabled = true;
            float pulse = 0f;
            while (true)
            {
                pulse += Time.unscaledDeltaTime * GlowPulseSpeed;
                float alpha = Mathf.Lerp(GlowPulseMin, GlowPulseMax, 0.5f + 0.5f * Mathf.Sin(pulse));
                var color = glow.color;
                color.a = alpha;
                glow.color = color;
                yield return null;
            }
        }

        private static void SetLit(Image sign, Image glow, Sprite lit, Sprite dark, bool on)
        {
            sign.sprite = on ? lit : dark;
            if (glow != null)
                glow.enabled = false;
        }

        private sealed class ClearScreenHost : MonoBehaviour { }
    }
}
