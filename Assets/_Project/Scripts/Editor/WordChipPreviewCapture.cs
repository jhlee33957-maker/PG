using TMPro;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeSpec.EditorTools
{
    /// <summary>
    /// 기획서 빈칸에 자연스럽게 들어간 단어와, 문장 위에 겹친 반투명 칩을 한 장에 담아 비교한다.
    /// 플레이어 빌드는 하지 않는다.
    /// </summary>
    [InitializeOnLoad]
    public static class WordChipPreviewCapture
    {
        private const string SentinelPath = @"C:\Users\KangSeok\agent-tools\word-chip-capture.sentinel";
        // 열린 에디터의 스테이지 카메라에 섞이지 않게, 미리보기만 이 레이어에 두고 찍는다
        private const int PreviewLayer = 31;

        static WordChipPreviewCapture()
        {
            if (!File.Exists(SentinelPath))
            {
                return;
            }
            File.Delete(SentinelPath);
            EditorApplication.delayCall += Capture;
        }

        public const string OutputPath = @"C:\Users\KangSeok\agent-tools\word-chip-readability-v2.png";

        private static readonly Color DocumentBg = new Color(0.12f, 0.14f, 0.18f, 1f);
        private static readonly Color SentenceColor = new Color(0.82f, 0.86f, 0.9f, 1f);
        private static readonly Color SlotFill = new Color(0.149f, 0.204f, 0.302f, 1f);      // #26344D
        private static readonly Color SlotText = new Color(0.561f, 0.722f, 0.941f, 1f);      // #8FB8F0
        private static readonly Color SlotOutline = new Color(0.31f, 0.545f, 0.839f, 1f);    // #4F8BD6
        private static readonly Color ChipFill = new Color(0.227f, 0.173f, 0.118f, 0.72f);
        private static readonly Color ChipText = new Color(0.961f, 0.757f, 0.537f, 1f);
        private static readonly Color CaptionColor = new Color(0.62f, 0.67f, 0.72f, 1f);
        private static readonly Color LabelColor = new Color(0.55f, 0.72f, 0.64f, 1f);

        [MenuItem("CoffeeSpec/단어 칩 가독성 미리보기 캡처")]
        public static void Capture()
        {
            const int width = 1600;
            const int height = 900;

            var root = new GameObject("WordChipPreviewRoot");
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                var camGo = new GameObject("PreviewCamera");
                camGo.transform.SetParent(root.transform, false);
                camGo.transform.position = new Vector3(0f, 0f, -10f);
                var camera = camGo.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 4.5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 20f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 1f);
                camera.cullingMask = 1 << PreviewLayer;
                camera.enabled = false;

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = rt;

                var canvasGo = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas));
                canvasGo.transform.SetParent(root.transform, false);
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                var canvasRect = (RectTransform)canvasGo.transform;
                canvasRect.sizeDelta = new Vector2(width, height);
                canvasRect.localScale = new Vector3(9f / height, 9f / height, 1f);
                canvasRect.position = Vector3.zero;

                TMP_FontAsset font = LoadFont();

                var panel = CreateUiImage(canvasRect, "Document", DocumentBg);
                Stretch(panel, new Vector2(100f, 80f), new Vector2(-100f, -80f));
                var panelRect = panel.rectTransform;

                var title = CreateTmp(panelRect, "Title", font, 22f, LabelColor);
                title.alignment = TextAlignmentOptions.Left;
                title.text = "빈칸에 넣은 단어 — 문장 안에 인라인, 칸이 단어 크기에 맞춰짐";
                SetAnchors(title.rectTransform, new Vector2(0f, 0.78f), new Vector2(1f, 0.92f), new Vector2(70f, 0f), new Vector2(-70f, 0f));

                // 인라인 문장: "고양이는 [움직인다]." 슬롯만 은은한 틴트, 옆 글자는 가리지 않음
                var inlineRow = CreateRow(panelRect, "InlineRow", new Vector2(0f, 0.52f), new Vector2(1f, 0.76f), new Vector2(70f, 0f), new Vector2(-70f, 0f));
                CreatePlainWord(inlineRow, font, "고양이는  ");
                CreateInlineSlot(inlineRow, font, "움직인다");
                CreatePlainWord(inlineRow, font, ".");

                var mid = CreateTmp(panelRect, "MidLabel", font, 22f, LabelColor);
                mid.alignment = TextAlignmentOptions.Left;
                mid.text = "문장 위에 겹친 칩 — 반투명 바탕, 드래그 중 문장은 살짝 흐리게";
                SetAnchors(mid.rectTransform, new Vector2(0f, 0.36f), new Vector2(1f, 0.5f), new Vector2(70f, 0f), new Vector2(-70f, 0f));

                var overlapHost = new GameObject("OverlapHost", typeof(RectTransform));
                var overlapRect = (RectTransform)overlapHost.transform;
                overlapRect.SetParent(panelRect, false);
                SetAnchors(overlapRect, new Vector2(0f, 0.14f), new Vector2(1f, 0.36f), new Vector2(70f, 0f), new Vector2(-70f, 0f));

                // 드래그 중처럼 문장을 살짝 흐리게 해서, 위에 올린 칩이 더 잘 보이게 한다
                var dimmedSentence = new Color(SentenceColor.r, SentenceColor.g, SentenceColor.b, 0.45f);
                var overlapSentence = CreateTmp(overlapRect, "OverlapSentence", font, 44f, dimmedSentence);
                overlapSentence.alignment = TextAlignmentOptions.MidlineLeft;
                overlapSentence.text = "가시에 닿으면 죽는다.";
                SetAnchors(overlapSentence.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

                var caption = CreateTmp(panelRect, "Caption", font, 22f, CaptionColor);
                caption.alignment = TextAlignmentOptions.Center;
                caption.text = "슬롯 안 단어는 본문처럼 읽고, 겹친 칩만 반투명 처리 (강조 외곽선·빛 번짐 없음)";
                SetAnchors(caption.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.12f), new Vector2(40f, 10f), new Vector2(-40f, 0f));

                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(inlineRow);
                Canvas.ForceUpdateCanvases();
                overlapSentence.ForceMeshUpdate();

                // "닿으면" 글자 한가운데에 칩을 올려, 문장과 실제로 겹치게 한다
                Vector2 chipPos = LocatePhraseCenter(overlapSentence, overlapRect, "닿으면", new Vector2(180f, 0f));
                CreateOverlapChip(overlapRect, font, chipPos, "비둘기");

                SetLayerRecursively(root, PreviewLayer);
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;

                string dir = System.IO.Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(OutputPath, tex.EncodeToPNG());
                Debug.Log("[WordChipPreview] saved " + OutputPath);
            }
            finally
            {
                if (tex != null) Object.DestroyImmediate(tex);
                if (rt != null)
                {
                    rt.Release();
                    Object.DestroyImmediate(rt);
                }
                Object.DestroyImmediate(root);
            }
        }

        private static Vector2 LocatePhraseCenter(TextMeshProUGUI tmp, RectTransform space, string phrase, Vector2 fallback)
        {
            if (tmp == null || space == null || string.IsNullOrEmpty(phrase)) return fallback;
            tmp.ForceMeshUpdate();
            int start = tmp.text.IndexOf(phrase, System.StringComparison.Ordinal);
            if (start < 0) return fallback;
            var info = tmp.textInfo;
            if (info == null || start >= info.characterCount) return fallback;
            var first = info.characterInfo[start];
            if (!first.isVisible) return fallback;
            int end = Mathf.Min(start + phrase.Length - 1, info.characterCount - 1);
            var last = info.characterInfo[end];
            Vector3 midWorld = tmp.transform.TransformPoint((first.bottomLeft + last.topRight) * 0.5f);
            return space.InverseTransformPoint(midWorld);
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i).gameObject, layer);
        }

        private static RectTransform CreateRow(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            SetAnchors(rect, anchorMin, anchorMax, offsetMin, offsetMax);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            // 높이를 행 전체로 늘리면 슬롯이 큰 사각형이 되어 문장이 어색해 보인다
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 0f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            return rect;
        }

        private static void CreatePlainWord(RectTransform row, TMP_FontAsset font, string text)
        {
            var tmp = CreateTmp(row, "Plain", font, 44f, SentenceColor);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.text = text;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            var le = tmp.gameObject.AddComponent<LayoutElement>();
            tmp.ForceMeshUpdate();
            Vector2 size = tmp.GetPreferredValues(text);
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            tmp.rectTransform.sizeDelta = new Vector2(size.x, size.y);
        }

        private static void CreateInlineSlot(RectTransform row, TMP_FontAsset font, string word)
        {
            var slot = new GameObject("Slot", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var slotRect = (RectTransform)slot.transform;
            slotRect.SetParent(row, false);
            var fill = slot.GetComponent<Image>();
            fill.color = SlotFill;
            fill.raycastTarget = false;

            var outlineGo = new GameObject("Outline", typeof(RectTransform));
            var outlineRect = (RectTransform)outlineGo.transform;
            outlineRect.SetParent(slotRect, false);
            outlineRect.anchorMin = Vector2.zero;
            outlineRect.anchorMax = Vector2.one;
            outlineRect.offsetMin = Vector2.zero;
            outlineRect.offsetMax = Vector2.zero;
            var border = outlineGo.AddComponent<CoffeeSpec.UI.UILineBorder>();
            border.color = SlotOutline;
            border.raycastTarget = false;

            var tmp = CreateTmp(slotRect, "Word", font, 44f, SlotText);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = word;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.ForceMeshUpdate();
            Vector2 size = tmp.GetPreferredValues(word);
            float padX = 16f;
            float padY = 8f;
            var le = slot.GetComponent<LayoutElement>();
            le.preferredWidth = size.x + padX * 2f;
            le.preferredHeight = size.y + padY * 2f;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            slotRect.sizeDelta = new Vector2(size.x + padX * 2f, size.y + padY * 2f);
            SetAnchors(tmp.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private static void CreateOverlapChip(RectTransform parent, TMP_FontAsset font, Vector2 anchored, string word)
        {
            var chipGo = new GameObject("OverlapChip", typeof(RectTransform), typeof(Canvas));
            var chipRect = (RectTransform)chipGo.transform;
            chipRect.SetParent(parent, false);
            chipRect.anchorMin = chipRect.anchorMax = chipRect.pivot = new Vector2(0.5f, 0.5f);
            chipRect.anchoredPosition = anchored;

            var chipCanvas = chipGo.GetComponent<Canvas>();
            chipCanvas.overrideSorting = true;
            chipCanvas.sortingOrder = 120;

            var tmp = CreateTmp(chipRect, "Label", font, 36f, ChipText);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = word;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.ForceMeshUpdate();
            Vector2 size = tmp.GetPreferredValues(word);
            chipRect.sizeDelta = new Vector2(size.x + 40f, size.y + 20f);
            SetAnchors(tmp.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var shadow = CreateUiImage(chipRect, "Shadow", new Color(0f, 0f, 0f, 0.28f));
            SetAnchors(shadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(3f, -4f), new Vector2(3f, -4f));
            shadow.rectTransform.SetAsFirstSibling();

            var fill = CreateUiImage(chipRect, "Fill", ChipFill);
            SetAnchors(fill.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fill.rectTransform.SetSiblingIndex(1);
            tmp.rectTransform.SetAsLastSibling();
        }

        private static TMP_FontAsset LoadFont()
        {
            string path = AssetDatabase.GUIDToAssetPath("34d90ea1837521248bfaa130815f5588");
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        private static Image CreateUiImage(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(Image image, Vector2 offsetMin, Vector2 offsetMax)
        {
            SetAnchors(image.rectTransform, Vector2.zero, Vector2.one, offsetMin, offsetMax);
        }

        private static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static TextMeshProUGUI CreateTmp(RectTransform parent, string name, TMP_FontAsset font, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            return tmp;
        }
    }
}
