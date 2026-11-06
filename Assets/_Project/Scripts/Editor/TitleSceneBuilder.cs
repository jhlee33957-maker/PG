using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 메뉴: CoffeeSpec/Title/타이틀 씬 자동 생성
    /// Title 씬에 Canvas, 연출 레이어, 기획서 클로즈업, 타이틀 화면, 메뉴 줄, 팝업 3개, EventSystem 을 만들고
    /// TitleUI / TitleIntroDirector / TitleScreenView / TitleMenuLine 의 필드를 전부 연결한다.
    /// 다시 실행하면 전에 만든 [TitleV2] 를 지우고 새로 만든다. 기존(옛) 타이틀 오브젝트는 지우지 않고 꺼 둔다.
    /// </summary>
    public static class TitleSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Title.unity";
        private const string SpriteDir = "Assets/_Project/Sprite/Title/";
        private const string RootName = "[TitleV2]";
        private const string CodeFontPath = "Assets/_Project/Fonts/D2Coding-Ver1.4.0-20261003 SDF.asset";
        private const string DocFontPath = "Assets/_Project/Fonts/NotoSansKR-Regular SDF.asset";
        private const string FillSpritePath = "Assets/_Project/Sprite/UI/UI_RoundRect_Fill.png";
        private const string OutlineSpritePath = "Assets/_Project/Sprite/UI/UI_RoundRect_Outline.png";

        private static readonly Vector2 Stage = new Vector2(1920f, 1080f);

        // 책상 이미지(title_desk.png) 안의 모니터 화면(마젠타) 영역. 0~1, 왼쪽 아래 기준.
        // 이미지를 바꾸면 이 값도 바꾸거나, 만든 뒤 MonitorScreenRect 의 Anchor 를 직접 맞춘다.
        private static readonly Vector2 MonitorMin = new Vector2(268f / 480f, 1f - 139f / 270f);
        private static readonly Vector2 MonitorMax = new Vector2(391f / 480f, 1f - 55f / 270f);

        // 색
        private static readonly Color EditorBg = Hex("1E1F22");
        private static readonly Color TabBarBg = Hex("18191C");
        private static readonly Color Accent = Hex("E8913B");
        private static readonly Color Mint = Hex("4EC9B0");
        private static readonly Color LineNo = Hex("5C6370");
        private static readonly Color CodeText = Hex("D4D4D4");
        private static readonly Color WindowBg = Hex("252526");
        private static readonly Color WindowBar = Hex("2D2F34");
        private static readonly Color WindowLine = Hex("3C3F45");
        private static readonly Color ButtonGrey = Hex("3A3D44");
        private static readonly Color Ink = Hex("2B3140");

        private const float TabHeight = 56f;
        private const float GutterWidth = 120f;
        private const float CodeLeft = 160f;
        private const float RowHeight = 60f;
        private const float TitleRowHeight = 180f;
        private const float CodeSize = 34f;

        private static TMP_FontAsset s_codeFont;
        private static TMP_FontAsset s_docFont;
        private static Sprite s_fill;
        private static Sprite s_outline;

        [MenuItem("CoffeeSpec/Title/타이틀 씬 자동 생성", false, 20)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (!PrepareSprites())
            {
                return;
            }

            LoadSharedAssets();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // 기존 TitleUI 설정(스테이지 씬 이름 등)을 가져오고 옛 오브젝트는 꺼 둔다
            OldTitleSettings old = ReadAndDisableOldTitle(scene);

            EnsureCamera(scene);

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            // ---------- Canvas ----------
            var canvasGo = new GameObject("TitleCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(AudioSource));
            canvasGo.layer = LayerMask.NameToLayer("UI");
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Stage;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var audio = canvasGo.GetComponent<AudioSource>();
            audio.playOnAwake = false;
            RectTransform canvasRect = (RectTransform)canvasGo.transform;

            // ---------- 연출 레이어 (방 / 책상 / 모니터 빛) ----------
            RectTransform world = Stretch(NewUI("IntroWorld", canvasRect));
            var worldGroup = world.gameObject.AddComponent<CanvasGroup>();
            worldGroup.blocksRaycasts = false;
            Image room = NewImage("L0_Room", world, LoadSprite("title_room.png"), Color.white);
            Centered(room.rectTransform, Stage);
            Image desk = NewImage("L1_Desk", world, LoadSprite("title_desk.png"), Color.white);
            Centered(desk.rectTransform, Stage);
            RectTransform monitorRect = NewUI("MonitorScreenRect", desk.rectTransform);
            monitorRect.anchorMin = MonitorMin;
            monitorRect.anchorMax = MonitorMax;
            monitorRect.offsetMin = monitorRect.offsetMax = Vector2.zero;
            Sprite glowSprite = LoadSprite("title_monitor_glow.png");
            Image glow = NewImage("L2_MonitorGlow", world, glowSprite, new Color(1f, 1f, 1f, 0f));
            Centered(glow.rectTransform, Stage);
            // 모니터 주변만 뚫린 검은 막: 깜빡일 때 방만 살짝 어두워진다
            Image roomDim = NewImage("L3_RoomDim", world, LoadSprite("title_room_dim.png"), new Color(1f, 1f, 1f, 0f));
            Centered(roomDim.rectTransform, Stage);
            roomDim.enabled = false;

            // ---------- 타이틀 화면 (모니터 안) ----------
            RectTransform viewport = Stretch(NewUI("ScreenViewport", canvasRect));
            var viewportBg = viewport.gameObject.AddComponent<Image>();
            viewportBg.color = EditorBg;
            viewportBg.raycastTarget = false;
            viewport.gameObject.AddComponent<RectMask2D>();
            var screenGroup = viewport.gameObject.AddComponent<CanvasGroup>();

            RectTransform screen = NewUI("TitleScreen", viewport);
            Centered(screen, Stage);
            var view = screen.gameObject.AddComponent<TitleScreenView>();

            Image screenBg = NewImage("Background", screen, null, EditorBg);
            Stretch(screenBg.rectTransform);

            // 탭
            Image tabBar = NewImage("TabBar", screen, null, TabBarBg);
            TopStrip(tabBar.rectTransform, 0f, TabHeight);
            Image tab = NewImage("Tab_title.cs", tabBar.rectTransform, null, EditorBg);
            tab.rectTransform.anchorMin = new Vector2(0f, 0f);
            tab.rectTransform.anchorMax = new Vector2(0f, 1f);
            tab.rectTransform.pivot = new Vector2(0f, 0.5f);
            tab.rectTransform.anchoredPosition = Vector2.zero;
            tab.rectTransform.sizeDelta = new Vector2(240f, 0f);
            Image tabAccent = NewImage("Accent", tab.rectTransform, null, Accent);
            TopStrip(tabAccent.rectTransform, 0f, 3f);
            TMP_Text tabText = NewText("Label", tab.rectTransform, "title.cs", 26f, CodeText, TextAlignmentOptions.Left, s_codeFont);
            Stretch(tabText.rectTransform, new Vector2(28f, 0f), new Vector2(-40f, 0f));
            TMP_Text tabClose = NewText("Close", tab.rectTransform, "×", 26f, LineNo, TextAlignmentOptions.Right, s_codeFont);
            Stretch(tabClose.rectTransform, new Vector2(0f, 0f), new Vector2(-16f, 0f));

            // 코드 줄
            RectTransform code = NewUI("Code", screen);
            Stretch(code, Vector2.zero, new Vector2(0f, -TabHeight - 30f));

            var comments = new List<TMP_Text>();
            var commentTexts = new List<string>();
            float y = 0f;
            int lineNo = 1;
            TMP_Text titleText = null;

            void Row(string text, Color color, bool isComment)
            {
                RectTransform row = NewRow($"Line{lineNo:00}", code, ref y, RowHeight);
                AddLineNumber(row, lineNo);
                TMP_Text t = NewText("Code", row, text, CodeSize, color, TextAlignmentOptions.Left, s_codeFont);
                Stretch(t.rectTransform, new Vector2(CodeLeft, 0f), Vector2.zero);
                if (isComment)
                {
                    comments.Add(t);
                    commentTexts.Add(text);
                }
                lineNo++;
            }

            Row("// ==========================================", Mint, true);
            {
                RectTransform row = NewRow($"Line{lineNo:00}_Title", code, ref y, TitleRowHeight);
                AddLineNumber(row, lineNo);
                titleText = NewText("Title", row, "// Coffee Spec", 120f, Hex("F3EBDD"), TextAlignmentOptions.Left, s_codeFont);
                Stretch(titleText.rectTransform, new Vector2(CodeLeft, 0f), Vector2.zero);
                lineNo++;
            }
            Row("// ==========================================", Mint, true);
            Row("// 마지막 수정: 새벽 2:07", Mint, true);
            Row("// TODO: 기획서에 쏟은 커피 수습하기", Mint, true);
            Row("", CodeText, false);
            Row("<color=#5A8BD6>void</color> Title()", CodeText, false);
            Row("{", CodeText, false);

            // 메뉴 줄 (CanvasGroup 으로 한꺼번에 잠금)
            RectTransform menu = NewUI("Menu", code);
            Stretch(menu);
            var menuGroup = menu.gameObject.AddComponent<CanvasGroup>();
            Button btnStart = MenuLine(menu, ref y, ref lineNo, view, "BTN_GameStart", "▶ StartGame();", "▶ StartGame();");
            Button btnStage = MenuLine(menu, ref y, ref lineNo, view, "BTN_StageSelect", "SelectStage();", "// SelectStage();  // 잠김");
            Button btnGuide = MenuLine(menu, ref y, ref lineNo, view, "BTN_Guide", "OpenGuide();", "OpenGuide();");
            Button btnExit = MenuLine(menu, ref y, ref lineNo, view, "BTN_Exit", "Exit();", "Exit();");

            Row("}", CodeText, false);

            // 화면 효과
            RawImage glowFx = NewRaw("FX_Vignette", screen, LoadOrCreateFxTexture("title_fx_vignette.png", false));
            Stretch(glowFx.rectTransform);
            RawImage scan = NewRaw("FX_Scanlines", screen, LoadOrCreateFxTexture("title_fx_scanline.png", true));
            Stretch(scan.rectTransform);

            // ---------- 모니터 화면 빛 (화면 창 위, 마스크 밖이라 베젤 너머까지 번짐) ----------
            Image bloom = NewImage("ScreenBloom", canvasRect, null, new Color(0.66f, 0.78f, 0.96f, 0f));
            bloom.raycastTarget = false;
            bloom.enabled = false;

            // ---------- 기획서 클로즈업 ----------
            RectTransform closeup = Stretch(NewUI("DocCloseup", canvasRect));
            var closeupGroup = closeup.gameObject.AddComponent<CanvasGroup>();
            closeupGroup.blocksRaycasts = false;
            Image closeupImage = NewImage("Paper", closeup, LoadSprite("title_doc_closeup.png"), Color.white);
            Centered(closeupImage.rectTransform, Stage);

            // 문장·커피잔·방울·자국은 기획서 이미지(title_doc_closeup.png)에 이미 그려져 있어 만들지 않는다

            // ---------- 어둠 ----------
            Image dark = NewImage("DarkOverlay", canvasRect, null, Color.black);
            Stretch(dark.rectTransform);

            // ---------- 팝업 ----------
            RectTransform popups = Stretch(NewUI("Popups", canvasRect));

            Popup restart = NewPopup(popups, "POPUP_Restart", "처음부터 다시?", new Vector2(900f, 340f), out RectTransform restartBody);
            TMP_Text warn = NewText("Message", restartBody, $"<color=#{ColorUtility.ToHtmlStringRGB(Accent)}>⚠</color> 저장된 진행을 덮어쓰고 처음부터 시작합니다.", 32f, CodeText, TextAlignmentOptions.TopLeft, s_codeFont);
            Stretch(warn.rectTransform, new Vector2(40f, 100f), new Vector2(-40f, -36f));
            Button yes = NewTextButton("BTN_Yes", restartBody, "예", Accent, EditorBg);
            Place(yes.GetComponent<RectTransform>(), new Vector2(170f, -100f), new Vector2(180f, 64f), new Vector2(0.5f, 0.5f));
            Button no = NewTextButton("BTN_No", restartBody, "아니오", ButtonGrey, CodeText);
            Place(no.GetComponent<RectTransform>(), new Vector2(360f, -100f), new Vector2(180f, 64f), new Vector2(0.5f, 0.5f));

            int stageCount = Mathf.Max(1, old.StageSceneNames.Count);
            int cols = Mathf.Min(stageCount, 5);
            int rows = Mathf.CeilToInt(stageCount / (float)cols);
            Popup stagePopup = NewPopup(popups, "POPUP_StageSelect", "SelectStage.cs", new Vector2(cols * 250f + 80f, rows * 210f + 140f), out RectTransform stageBody);
            var stageButtons = new Button[stageCount];
            var stageImages = new Image[stageCount];
            for (int i = 0; i < stageCount; i++)
            {
                int c = i % cols;
                int r = i / cols;
                Button b = NewTextButton($"BTN_Stage{i + 1}", stageBody, $"LoadStage({i + 1});", ButtonGrey, CodeText, 26f);
                RectTransform br = b.GetComponent<RectTransform>();
                br.anchorMin = br.anchorMax = new Vector2(0f, 1f);
                br.pivot = new Vector2(0f, 1f);
                br.sizeDelta = new Vector2(220f, 180f);
                br.anchoredPosition = new Vector2(40f + c * 250f, -24f - r * 210f);
                TMP_Text label = b.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.Bottom;
                label.rectTransform.offsetMin = new Vector2(0f, 14f);
                stageButtons[i] = b;
                stageImages[i] = b.GetComponent<Image>();
            }

            Popup guidePopup = NewPopup(popups, "POPUP_Guide", "Guide.md", new Vector2(980f, 560f), out RectTransform guideBody);
            string guide =
                "<color=#4EC9B0>// 기획서의 빈칸 [ ____ ] 을 채우면 그대로 게임이 된다</color>\n\n" +
                "이동      A / D, ← / →\n" +
                "점프      Z\n" +
                "공격      X\n" +
                "대시      C  (누르고 있으면 달리기)\n" +
                "기획서    R";
            TMP_Text guideText = NewText("Body", guideBody, guide, 32f, CodeText, TextAlignmentOptions.TopLeft, s_codeFont);
            guideText.lineSpacing = 20f;
            Stretch(guideText.rectTransform, new Vector2(48f, 32f), new Vector2(-48f, -36f));

            restart.gameObject.SetActive(false);
            stagePopup.gameObject.SetActive(false);
            guidePopup.gameObject.SetActive(false);

            // ---------- EventSystem ----------
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(root.transform, false);

            // ---------- 컴포넌트 연결 ----------
            // TitleScreenView
            var so = new SerializedObject(view);
            so.FindProperty("_gameTitle").stringValue = "Coffee Spec";
            so.FindProperty("_titleText").objectReferenceValue = titleText;
            SetArray(so.FindProperty("_commentTargets"), comments.ToArray());
            SerializedProperty ct = so.FindProperty("_commentTexts");
            ct.arraySize = commentTexts.Count;
            for (int i = 0; i < commentTexts.Count; i++) ct.GetArrayElementAtIndex(i).stringValue = commentTexts[i];
            so.FindProperty("_menuGroup").objectReferenceValue = menuGroup;
            so.FindProperty("_scanlines").objectReferenceValue = scan;
            so.FindProperty("_glow").objectReferenceValue = glowFx;
            so.FindProperty("_audioSource").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();

            // TitleIntroDirector
            var director = canvasGo.AddComponent<TitleIntroDirector>();
            so = new SerializedObject(director);
            so.FindProperty("_screen").objectReferenceValue = view;
            so.FindProperty("_screenViewport").objectReferenceValue = viewport;
            so.FindProperty("_screenContent").objectReferenceValue = screen;
            so.FindProperty("_screenGroup").objectReferenceValue = screenGroup;
            so.FindProperty("_monitorScreenRect").objectReferenceValue = monitorRect;
            so.FindProperty("_worldGroup").objectReferenceValue = worldGroup;
            SerializedProperty layers = so.FindProperty("_layers");
            layers.arraySize = 3;
            SetLayer(layers.GetArrayElementAtIndex(0), room.rectTransform, 0.55f, 0.8f, 1.15f);
            SetLayer(layers.GetArrayElementAtIndex(1), desk.rectTransform, 1f, 1f, 1f);
            SetLayer(layers.GetArrayElementAtIndex(2), glow.rectTransform, 1f, 1f, 1f);
            so.FindProperty("_monitorGlow").objectReferenceValue = glow;
            so.FindProperty("_roomDim").objectReferenceValue = roomDim;
            so.FindProperty("_screenBloom").objectReferenceValue = bloom;
            so.FindProperty("_darkOverlay").objectReferenceValue = dark;
            so.FindProperty("_closeupGroup").objectReferenceValue = closeupGroup;
            so.FindProperty("_roomImage").objectReferenceValue = room;
            so.FindProperty("_deskImage").objectReferenceValue = desk;
            so.FindProperty("_closeupImage").objectReferenceValue = closeupImage;
            so.FindProperty("_roomSprite").objectReferenceValue = room.sprite;
            so.FindProperty("_deskSprite").objectReferenceValue = desk.sprite;
            so.FindProperty("_glowSprite").objectReferenceValue = glow.sprite;
            so.FindProperty("_closeupSprite").objectReferenceValue = closeupImage.sprite;
            so.FindProperty("_closeupHold").floatValue = 2f;
            SerializedProperty panStart = so.FindProperty("_panStart");
            panStart.FindPropertyRelative("Focus").vector2Value = new Vector2(0.215f, 0.30f);
            panStart.FindPropertyRelative("Zoom").floatValue = 2.2f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // TitleUI (기존 버튼 로직 그대로)
            var titleUi = canvasGo.AddComponent<TitleUI>();
            so = new SerializedObject(titleUi);
            so.FindProperty("_btnGameStart").objectReferenceValue = btnStart;
            so.FindProperty("_btnStageSelect").objectReferenceValue = btnStage;
            so.FindProperty("_btnGuide").objectReferenceValue = btnGuide;
            so.FindProperty("_btnExit").objectReferenceValue = btnExit;
            so.FindProperty("_clearedPopup").objectReferenceValue = restart;
            so.FindProperty("_btnClearedYes").objectReferenceValue = yes;
            so.FindProperty("_btnClearedNo").objectReferenceValue = no;
            so.FindProperty("_stageSelectPopup").objectReferenceValue = stagePopup;
            so.FindProperty("_guidePopup").objectReferenceValue = guidePopup;
            SerializedProperty stages = so.FindProperty("_stages");
            stages.arraySize = stageCount;
            for (int i = 0; i < stageCount; i++)
            {
                SerializedProperty e = stages.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("_button").objectReferenceValue = stageButtons[i];
                e.FindPropertyRelative("_image").objectReferenceValue = stageImages[i];
                e.FindPropertyRelative("_sceneName").stringValue = i < old.StageSceneNames.Count ? old.StageSceneNames[i] : "";
            }
            so.FindProperty("_introSceneName").stringValue = string.IsNullOrEmpty(old.IntroSceneName) ? "Intro" : old.IntroSceneName;
            if (old.Found)
            {
                so.FindProperty("_useDebugSave").boolValue = old.UseDebugSave;
                so.FindProperty("_debugHasRecord").boolValue = old.DebugHasRecord;
                so.FindProperty("_debugLastPlayedStage").intValue = old.DebugLastPlayed;
                so.FindProperty("_debugHighestClearedStage").intValue = old.DebugHighestCleared;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = canvasGo;

            bool emptyScene = false;
            foreach (string n in old.StageSceneNames) if (string.IsNullOrEmpty(n)) emptyScene = true;
            Debug.Log($"[TitleSceneBuilder] Title 씬 생성 완료. 스테이지 {stageCount}개" + (old.Found ? " (기존 TitleUI 설정을 가져옴)" : " (기존 TitleUI 없음)") +
                      (emptyScene ? ". 스테이지 씬 이름이 비어 있는 항목이 있습니다. TitleUI > 스테이지 목록에서 입력하세요." : ""));
        }

        // ---------------- 준비 ----------------

        private static bool PrepareSprites()
        {
            string[] names =
            {
                "title_room.png", "title_desk.png", "title_monitor_glow.png", "title_monitor_glow_soft.png", "title_doc_closeup.png", "title_room_dim.png",
            };

            var magenta = new List<string>();
            foreach (string n in names)
            {
                string path = SpriteDir + n;
                if (!File.Exists(TitleMagentaConverter.ToFullPath(path)))
                {
                    Debug.LogWarning($"[TitleSceneBuilder] 이미지가 없습니다: {path}");
                    continue;
                }
                if (TitleMagentaConverter.HasMagenta(path))
                {
                    magenta.Add(path);
                }
            }

            if (magenta.Count > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "마젠타 배경이 남아 있음",
                    $"마젠타(#FF00FF) 배경이 남은 이미지가 {magenta.Count}개 있습니다.\n지금 투명으로 바꿀까요? (원본 파일을 덮어씀)",
                    "바꾸고 계속", "취소", "그대로 계속");
                if (choice == 1)
                {
                    return false;
                }
                if (choice == 0)
                {
                    foreach (string p in magenta) TitleMagentaConverter.ConvertFile(p);
                }
            }

            foreach (string n in names)
            {
                string path = SpriteDir + n;
                if (File.Exists(TitleMagentaConverter.ToFullPath(path)))
                {
                    TitleMagentaConverter.ApplySpriteImportSettings(path);
                }
            }
            AssetDatabase.Refresh();
            return true;
        }

        private static void LoadSharedAssets()
        {
            s_codeFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CodeFontPath);
            s_docFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DocFontPath);
            if (s_codeFont == null) Debug.LogWarning($"[TitleSceneBuilder] 코드 폰트가 없어 TMP 기본 폰트를 씁니다: {CodeFontPath}");
            if (s_docFont == null) s_docFont = s_codeFont;

            s_fill = AssetDatabase.LoadAssetAtPath<Sprite>(FillSpritePath);
            s_outline = AssetDatabase.LoadAssetAtPath<Sprite>(OutlineSpritePath);
            if (s_fill == null) Debug.LogWarning($"[TitleSceneBuilder] 9-slice 스프라이트가 없습니다: {FillSpritePath}");
            if (s_outline == null) Debug.LogWarning($"[TitleSceneBuilder] 9-slice 스프라이트가 없습니다: {OutlineSpritePath}");
            if (s_fill != null && s_fill.border == Vector4.zero) Debug.LogWarning("[TitleSceneBuilder] UI_RoundRect_Fill 에 9-slice Border 가 없습니다. Sprite Editor 에서 Border 를 지정하세요.");
        }

        private static Sprite LoadSprite(string fileName)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + fileName);
            if (sprite == null)
            {
                Debug.LogWarning($"[TitleSceneBuilder] 스프라이트를 찾지 못했습니다: {SpriteDir + fileName}");
            }
            return sprite;
        }

        /// <summary>스캔라인/빛 번짐 텍스처를 PNG 에셋으로 저장해 에디터에서도 보이게 한다.</summary>
        private static Texture2D LoadOrCreateFxTexture(string fileName, bool scanline)
        {
            string path = SpriteDir + fileName;
            if (!File.Exists(TitleMagentaConverter.ToFullPath(path)))
            {
                Texture2D tex = scanline ? TitleScreenView.CreateScanlineTexture() : TitleScreenView.CreateGlowTexture(256);
                File.WriteAllBytes(TitleMagentaConverter.ToFullPath(path), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.filterMode = scanline ? FilterMode.Point : FilterMode.Bilinear;
                importer.wrapMode = scanline ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private class OldTitleSettings
        {
            public bool Found;
            public readonly List<string> StageSceneNames = new List<string>();
            public string IntroSceneName = "Intro";
            public bool UseDebugSave;
            public bool DebugHasRecord = true;
            public int DebugLastPlayed = 1;
            public int DebugHighestCleared;
        }

        private static OldTitleSettings ReadAndDisableOldTitle(Scene scene)
        {
            var result = new OldTitleSettings();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == RootName || go.name == "[TitleV3]")
                {
                    // 전에 이 메뉴로 만든 것: 설정만 읽고 지운다
                    ReadTitleUi(go, result);
                    Object.DestroyImmediate(go);
                }
            }
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                bool hasTitleUi = go.GetComponentsInChildren<TitleUI>(true).Length > 0;
                bool hasEventSystem = go.GetComponentsInChildren<EventSystem>(true).Length > 0;
                if (hasTitleUi)
                {
                    ReadTitleUi(go, result);
                }
                if (hasTitleUi || hasEventSystem)
                {
                    go.SetActive(false);
                    if (!go.name.StartsWith("(old) "))
                    {
                        go.name = "(old) " + go.name;
                    }
                    Debug.Log($"[TitleSceneBuilder] 기존 오브젝트 '{go.name}' 를 껐습니다 (지우지 않음). 확인 후 직접 지워도 됩니다.");
                }
            }
            if (result.StageSceneNames.Count == 0)
            {
                for (int i = 0; i < 5; i++) result.StageSceneNames.Add("");
            }
            return result;
        }

        private static void ReadTitleUi(GameObject go, OldTitleSettings result)
        {
            if (result.Found)
            {
                return;
            }
            TitleUI ui = go.GetComponentInChildren<TitleUI>(true);
            if (ui == null)
            {
                return;
            }
            result.Found = true;
            var so = new SerializedObject(ui);
            SerializedProperty stages = so.FindProperty("_stages");
            result.StageSceneNames.Clear();
            for (int i = 0; i < stages.arraySize; i++)
            {
                result.StageSceneNames.Add(stages.GetArrayElementAtIndex(i).FindPropertyRelative("_sceneName").stringValue);
            }
            result.IntroSceneName = so.FindProperty("_introSceneName").stringValue;
            result.UseDebugSave = so.FindProperty("_useDebugSave").boolValue;
            result.DebugHasRecord = so.FindProperty("_debugHasRecord").boolValue;
            result.DebugLastPlayed = so.FindProperty("_debugLastPlayedStage").intValue;
            result.DebugHighestCleared = so.FindProperty("_debugHighestClearedStage").intValue;
        }

        private static void EnsureCamera(Scene scene)
        {
            Camera cam = null;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                cam = go.GetComponentInChildren<Camera>(false);
                if (cam != null && go.activeInHierarchy) break;
                cam = null;
            }
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera));
                camGo.tag = "MainCamera";
                SceneManager.MoveGameObjectToScene(camGo, scene);
                cam = camGo.GetComponent<Camera>();
                cam.orthographic = true;
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("0B0E14");
        }

        // ---------------- 만들기 도우미 ----------------

        private static RectTransform NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            RectTransform rt = NewUI(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static RawImage NewRaw(string name, Transform parent, Texture tex)
        {
            RectTransform rt = NewUI(name, parent);
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            return raw;
        }

        private static TMP_Text NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align, TMP_FontAsset font)
        {
            RectTransform rt = NewUI(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = true;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        private static RectTransform Stretch(RectTransform rt)
        {
            return Stretch(rt, Vector2.zero, Vector2.zero);
        }

        private static RectTransform Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private static void Centered(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        private static void TopStrip(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -top);
            rt.sizeDelta = new Vector2(0f, height);
        }

        private static RectTransform NewRow(string name, RectTransform parent, ref float y, float height)
        {
            RectTransform row = NewUI(name, parent);
            TopStrip(row, y, height);
            y += height;
            return row;
        }

        private static TMP_Text AddLineNumber(RectTransform row, int number)
        {
            TMP_Text n = NewText("LineNumber", row, number.ToString(), CodeSize, LineNo, TextAlignmentOptions.Right, s_codeFont);
            n.rectTransform.anchorMin = new Vector2(0f, 0f);
            n.rectTransform.anchorMax = new Vector2(0f, 1f);
            n.rectTransform.pivot = new Vector2(0f, 0.5f);
            n.rectTransform.anchoredPosition = Vector2.zero;
            n.rectTransform.sizeDelta = new Vector2(GutterWidth - 20f, 0f);
            return n;
        }

        /// <summary>
        /// 메뉴 줄 하나. 줄(투명 Image + TitleMenuLine)이 마우스를 받고,
        /// 자식의 BTN_xxx(Button, 그래픽 없음)는 TitleUI 가 onClick 을 연결하는 용도.
        /// </summary>
        private static Button MenuLine(RectTransform parent, ref float y, ref int lineNo, TitleScreenView view, string buttonName, string code, string lockedCode)
        {
            RectTransform row = NewRow($"Line{lineNo:00}_{buttonName}", parent, ref y, RowHeight);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            Image highlight = NewImage("Highlight", row, null, new Color(1f, 1f, 1f, 0.06f));
            Stretch(highlight.rectTransform);
            highlight.enabled = false;

            TMP_Text number = AddLineNumber(row, lineNo);
            TMP_Text text = NewText("Code", row, "    " + code, CodeSize, CodeText, TextAlignmentOptions.Left, s_codeFont);
            Stretch(text.rectTransform, new Vector2(CodeLeft, 0f), Vector2.zero);

            Image caret = NewImage("Caret", row, null, Accent);
            caret.rectTransform.anchorMin = caret.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            caret.rectTransform.pivot = new Vector2(0f, 0.5f);
            caret.rectTransform.sizeDelta = new Vector2(4f, 42f);
            caret.rectTransform.anchoredPosition = new Vector2(CodeLeft, 0f);
            caret.gameObject.SetActive(false);

            RectTransform btnRt = NewUI(buttonName, row);
            Stretch(btnRt);
            var button = btnRt.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            var line = row.gameObject.AddComponent<TitleMenuLine>();
            var so = new SerializedObject(line);
            so.FindProperty("_view").objectReferenceValue = view;
            so.FindProperty("_targetButton").objectReferenceValue = button;
            so.FindProperty("_codeText").objectReferenceValue = text;
            so.FindProperty("_lineNumberText").objectReferenceValue = number;
            so.FindProperty("_highlight").objectReferenceValue = highlight;
            so.FindProperty("_caret").objectReferenceValue = caret.rectTransform;
            so.FindProperty("_code").stringValue = code;
            so.FindProperty("_lockedCode").stringValue = lockedCode;
            so.ApplyModifiedPropertiesWithoutUndo();

            lineNo++;
            return button;
        }

        private static Popup NewPopup(RectTransform parent, string name, string title, Vector2 size, out RectTransform body)
        {
            // 루트 = 화면 전체를 덮는 어두운 막 (뒤의 메뉴 클릭을 막음)
            Image dim = NewImage(name, parent, null, new Color(0f, 0f, 0f, 0.55f));
            dim.raycastTarget = true;
            Stretch(dim.rectTransform);

            Image window = NewImage("Window", dim.rectTransform, s_fill, WindowBg);
            window.type = s_fill != null && s_fill.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            window.raycastTarget = true;
            Centered(window.rectTransform, size);

            Image bar = NewImage("TitleBar", window.rectTransform, s_fill, WindowBar);
            bar.type = window.type;
            TopStrip(bar.rectTransform, 0f, 56f);
            TMP_Text titleText = NewText("Title", bar.rectTransform, title, 28f, CodeText, TextAlignmentOptions.Left, s_codeFont);
            Stretch(titleText.rectTransform, new Vector2(24f, 0f), new Vector2(-70f, 0f));

            RectTransform closeRt = NewUI("BTN_Close", bar.rectTransform);
            closeRt.anchorMin = closeRt.anchorMax = new Vector2(1f, 0.5f);
            closeRt.pivot = new Vector2(1f, 0.5f);
            closeRt.sizeDelta = new Vector2(56f, 56f);
            closeRt.anchoredPosition = new Vector2(-4f, 0f);
            var closeHit = closeRt.gameObject.AddComponent<Image>();
            closeHit.color = new Color(0f, 0f, 0f, 0f);
            TMP_Text x = NewText("X", closeRt, "×", 34f, Hex("A0A4AC"), TextAlignmentOptions.Center, s_codeFont);
            Stretch(x.rectTransform);
            var close = closeRt.gameObject.AddComponent<Button>();
            close.targetGraphic = x;
            ColorBlock cb = close.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = Accent;
            cb.pressedColor = Accent;
            cb.selectedColor = Color.white;
            close.colors = cb;

            Image outline = NewImage("Outline", window.rectTransform, s_outline, WindowLine);
            outline.type = s_outline != null && s_outline.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            Stretch(outline.rectTransform);

            body = NewUI("Body", window.rectTransform);
            Stretch(body, Vector2.zero, new Vector2(0f, -56f));

            var popup = dim.gameObject.AddComponent<Popup>();
            var so = new SerializedObject(popup);
            so.FindProperty("_btnClose").objectReferenceValue = close;
            so.ApplyModifiedPropertiesWithoutUndo();
            return popup;
        }

        private static Button NewTextButton(string name, Transform parent, string label, Color bg, Color fg, float size = 30f)
        {
            Image img = NewImage(name, parent, s_fill, bg);
            img.type = s_fill != null && s_fill.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.colorMultiplier = 1.2f;
            b.colors = cb;
            TMP_Text t = NewText("Label", img.rectTransform, label, size, fg, TextAlignmentOptions.Center, s_codeFont);
            Stretch(t.rectTransform);
            return b;
        }

        private static void SetArray(SerializedProperty prop, Object[] values)
        {
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void SetLayer(SerializedProperty element, RectTransform rect, float pan, float zoom, float overscale)
        {
            element.FindPropertyRelative("_rect").objectReferenceValue = rect;
            element.FindPropertyRelative("_panFactor").floatValue = pan;
            element.FindPropertyRelative("_zoomFactor").floatValue = zoom;
            element.FindPropertyRelative("_overscale").floatValue = overscale;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }
    }
}
