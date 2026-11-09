#if UNITY_EDITOR
using System;
using System.IO;
using CoffeeSpec.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CoffeeSpec.EditorTools
{
    // One-shot scene authoring tool. No runtime builder and no asset/scene YAML editing.
    public static class IntroSceneBuilder
    {
        private const string IntroPath = "Assets/Scenes/Intro.unity";
        private const string Art = "Assets/_Project/Sprite/Title/";
        private const string Fonts = "Assets/_Project/Fonts/";
        private static TMP_FontAsset _uiFont;
        private static TMP_FontAsset _codeFont;

        private const string IntroArt = "Assets/_Project/Sprite/Intro/";
        [MenuItem("Tools/CoffeeSpec/Build Intro Scene")]
        private static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(IntroPath)) { Debug.LogError("Intro 씬이 없습니다."); return; }
            if (SceneManager.GetActiveScene().path != IntroPath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(IntroPath, OpenSceneMode.Single);
            }
            Scene scene = SceneManager.GetActiveScene();
            var old = new System.Collections.Generic.List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "IntroCanvas" || root.name == "IntroDirector") old.Add(root);
            if (old.Count > 0 && !EditorUtility.DisplayDialog("Intro 재구성", "기존 IntroCanvas / IntroDirector를 백업한 뒤 삭제하고 다시 만듭니다. 계속할까요?", "재구성", "취소")) return;
            string backup;
            try { backup = Backup(); ImportSprites(); }
            catch (Exception e) { Debug.LogException(e); return; }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rebuild CoffeeSpec Intro");
            try
            {
                foreach (GameObject go in old) Undo.DestroyObjectImmediate(go);
                _codeFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "D2Coding-Ver1.4.0-20261003 SDF.asset");
                _uiFont = _codeFont != null ? _codeFont : TMP_Settings.defaultFontAsset;
                RectTransform canvasRect = Rect("IntroCanvas", null);
                Canvas canvas = Undo.AddComponent<Canvas>(canvasRect.gameObject);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.pixelPerfect = true;
                CanvasScaler scaler = Undo.AddComponent<CanvasScaler>(canvas.gameObject);
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920,1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);
                EnsureEventSystem(scene);
                PanelFull("Background", canvasRect, Hex("0B0E13"));
                RectTransform world = Rect("World", canvasRect);
                world.sizeDelta = new Vector2(1920,1080);
                RectTransform lit = Full("OfficeLightsOn", world);
                Pixel("Room",lit,"intro_room_lit",Vector2.zero,new Vector2(1920,1080));
                Pixel("Desk",lit,"intro_desk_lit",Vector2.zero,new Vector2(1920,1080));
                RectTransform dark = Full("OfficeLightsOff",world);
                Pixel("Room",dark,"intro_room_dark",Vector2.zero,new Vector2(1920,1080));
                Pixel("Desk",dark,"intro_desk_dark",Vector2.zero,new Vector2(1920,1080));
                RectTransform after = Full("AfterSpillDark",world);
                Pixel("Room",after,"intro_room_dark",Vector2.zero,new Vector2(1920,1080));
                Pixel("DeskWithoutUprightMug",after,"intro_desk_after_dark",Vector2.zero,new Vector2(1920,1080));
                Pixel("CoffeePuddle",after,"intro_coffee_puddle",Vector2.zero,new Vector2(1920,1080));
                Pixel("FallenMug",after,"intro_fallen_mug",Vector2.zero,new Vector2(1920,1080));
                RectTransform stain=Pixel("CoffeeStainOnPaper",world,"intro_paper_stain",Vector2.zero,new Vector2(1920,1080));
                // Existing desk art already contains the one paper sheet. Do not add a floating card.
                RectTransform noteLit=Pixel("AttachedPostItLit",world,"intro_postit",new Vector2(-443,-210),new Vector2(60,45));
                RectTransform noteDark=Pixel("AttachedPostItDark",world,"intro_postit_dark",new Vector2(-443,-210),new Vector2(60,45));
                // Source 1024x576 desk: measured screen interior x571..833,y118..292.
                RectTransform screen = Rect("MonitorScreen",world);
                screen.anchoredPosition = new Vector2(356.25f,155.625f);
                screen.sizeDelta = new Vector2(491.25f,326.25f);
                Undo.AddComponent<RectMask2D>(screen.gameObject);
                RectTransform content = Rect("ScreenContent",screen);
                content.sizeDelta = new Vector2(960,638);
                content.localScale = Vector3.one * (491.25f/960f);

                RectTransform chat = Panel("Messenger",content,Vector2.zero,new Vector2(960,638),Hex("BCCAD2"));
                Panel("TopBar",chat,new Vector2(0,250),new Vector2(960,138),Hex("F4F1E3"));
                Pixel("Profile",chat,"intro_profile",new Vector2(-396,250),new Vector2(84,84));
                Text("Name",chat,"기획자",new Vector2(-185,277),new Vector2(290,52),34,_uiFont,Hex("252B34"));
                RectTransform statusGroup=Rect("StatusGroup",chat);
                statusGroup.anchoredPosition=new Vector2(-70,224);statusGroup.sizeDelta=new Vector2(610,50);
                // Name and status content share x=150. Two spaces reserve a 24px inline island only on vacation.
                RectTransform island=Pixel("Island",statusGroup,"intro_island",new Vector2(-248,-2),new Vector2(24,24));
                TMP_Text status=Text("Status",statusGroup,"근무 중",new Vector2(-10,0),new Vector2(500,50),28,_uiFont,Hex("57636F"));
                RectTransform partner = Rect("PartnerMessage",chat);
                partner.sizeDelta = chat.sizeDelta;
                Pixel("Profile",partner,"intro_profile",new Vector2(-405,75),new Vector2(68,68));
                RectTransform pb = Pixel("LeftBubble",partner,"intro_bubble_left",new Vector2(-95,65),new Vector2(500,110),true);
                Text("Text",pb,"책상에 두고 갑니다~",Vector2.zero,new Vector2(450,90),32,_uiFont,Hex("252B34"));
                Text("Time",partner,"오후 8:12",new Vector2(240,20),new Vector2(160,45),22,_uiFont,Hex("626C76"));
                RectTransform mine = Rect("MyMessage",chat); mine.sizeDelta=chat.sizeDelta;
                RectTransform mb = Pixel("RightBubble",mine,"intro_bubble_right",new Vector2(120,-115),new Vector2(590,110),true);
                Text("Text",mb,"기획서에 커피를 좀...",Vector2.zero,new Vector2(540,90),32,_uiFont,Hex("252B34"));
                RectTransform meta=Rect("UnreadAndTime",mb);Anchor(meta,Vector2.zero);
                meta.anchoredPosition=new Vector2(-80,38);meta.sizeDelta=new Vector2(140,80);
                TMP_Text unread=Text("Unread",meta,"1",new Vector2(0,20),new Vector2(140,34),24,_uiFont,Hex("886C1E"));unread.alignment=TextAlignmentOptions.MidlineRight;
                TMP_Text sentTime=Text("Time",meta,"새벽 2:07",new Vector2(0,-15),new Vector2(140,34),22,_uiFont,Hex("626C76"));sentTime.alignment=TextAlignmentOptions.MidlineRight;

                TMP_Text[] lines;
                RectTransform editor=EditorScreen(content,"CommentEditor",true,out lines);
                TMP_Text[] ordinaryLines;
                RectTransform nightEditor=EditorScreen(content,"NightEditor",false,out ordinaryLines);

                RectTransform close=Full("StainedDocumentCloseup",canvasRect);
                Undo.AddComponent<CanvasGroup>(close.gameObject);
                RectTransform paper=Picture("Paper",close,"title_doc_closeup.png",Vector2.zero,new Vector2(1920,1080),Color.white);
                Picture("Post",paper,"ui_postit_build.png",new Vector2(400,-260),new Vector2(512,512),Color.white);
                // HUD is above camera contents. Top bar reserves space outside the zoomed monitor.
                TMP_Text time=Text("TimeLabel",canvasRect,"오후 8:12",new Vector2(-760,-48),new Vector2(310,64),32,_uiFont,Color.white);
                Anchor((RectTransform)time.transform,new Vector2(.5f,1));
                RectTransform skipRect=Pixel("BTN_Skip",canvasRect,"intro_panel",new Vector2(-110,-48),new Vector2(150,58),true);
                Anchor(skipRect,Vector2.one);
                skipRect.GetComponent<Image>().raycastTarget=true;
                Button skip=Undo.AddComponent<Button>(skipRect.gameObject); skip.targetGraphic=skipRect.GetComponent<Image>();
                Navigation nav=skip.navigation;nav.mode=Navigation.Mode.None;skip.navigation=nav;
                TMP_Text skipText=Text("Text",skipRect,"Skip",Vector2.zero,new Vector2(140,54),28,_uiFont,Hex("252B34"));skipText.alignment=TextAlignmentOptions.Center;
                Image overlay=PanelFull("BlackOverlay",canvasRect,new Color(0,0,0,0)).GetComponent<Image>();
                GameObject obj=new GameObject("IntroDirector"); SceneManager.MoveGameObjectToScene(obj,scene);Undo.RegisterCreatedObjectUndo(obj,"Create IntroDirector");
                IntroDirector director=Undo.AddComponent<IntroDirector>(obj);
                AudioSource audio=Undo.AddComponent<AudioSource>(obj);audio.playOnAwake=false;audio.spatialBlend=0;
                SerializedObject so=new SerializedObject(director);
                so.FindProperty("_world").objectReferenceValue=world;
                so.FindProperty("_maxCameraScale").floatValue=3;
                so.FindProperty("_stageSceneName").stringValue="Stage1Scene";
                so.FindProperty("_blackOverlay").objectReferenceValue=overlay;
                so.FindProperty("_btnSkip").objectReferenceValue=skip;
                so.FindProperty("_audioSource").objectReferenceValue=audio;
                SerializedProperty cuts=so.FindProperty("_cuts");cuts.arraySize=6;
                SerializedProperty beats=Cut(cuts,0,"오후 8:12 / 수신 후 모니터 줌",new[]{world.gameObject,lit.gameObject,noteLit.gameObject,chat.gameObject,time.gameObject},time,"오후 8:12",6,.35f,3);
                Camera(cuts,0,Vector2.zero,1,0);
                Beat(beats,0,"시작 상태 명시",0,deactivate:island.gameObject,target:status,text:"근무 중");
                Beat(beats,1,"메시지 수신 / 알림음 / 모니터 줌",.7f,partner.gameObject);
                BeatCamera(beats,1,new Vector2(-300,-100),1.7f,1);
                Beat(beats,2,"수신 뒤 퇴근으로 변경",.5f,target:status,text:"퇴근");
                Cut(cuts,1,"새벽 1:58 / 딸깍: Sfx On Start",new[]{world.gameObject,dark.gameObject,noteDark.gameObject,nightEditor.gameObject,time.gameObject},time,"새벽 1:58",3,0,0);
                Camera(cuts,1,new Vector2(-70,-60),1.35f,.8f);
                beats=Cut(cuts,2,"암전",new[]{world.gameObject,dark.gameObject,noteDark.gameObject},null,"",2.2f,0,2);
                Camera(cuts,2,new Vector2(-70,-60),1.35f,0,blackAfter:true);
                Beat(beats,0,"툭",.4f);Beat(beats,1,"주르륵",.8f);
                Cut(cuts,3,"새벽 2:07 / 어두운 책상과 쓰러진 잔",new[]{world.gameObject,after.gameObject,noteDark.gameObject,stain.gameObject,nightEditor.gameObject,time.gameObject},time,"새벽 2:07",1.5f,.5f,0);
                Camera(cuts,3,new Vector2(1469,578),2.7f,1.3f,blackBefore:true,reveal:close.gameObject);
                cuts.GetArrayElementAtIndex(3).FindPropertyRelative("_holdBeforeMove").floatValue=.65f;
                beats=Cut(cuts,4,"모니터로 이동 / 연락 두절",new[]{world.gameObject,after.gameObject,noteDark.gameObject,stain.gameObject,chat.gameObject},null,"",4,0,4);
                Camera(cuts,4,new Vector2(-748.125f,-376.8125f),2.1f,0);
                cuts.GetArrayElementAtIndex(4).FindPropertyRelative("_hardCutOnEnter").boolValue=true;
                Beat(beats,0,"이전 대화 유지 / 퇴근",0,partner.gameObject,deactivate:island.gameObject,target:status,text:"퇴근");
                Beat(beats,1,"전송 / 읽지 않음 1",.5f,mine.gameObject);
                Beat(beats,2,"상단 휴가 상태 변경",1.2f,island.gameObject,target:status,text:"  휴가 중 (~월)");
                Beat(beats,3,"여운",1);
                beats=Cut(cuts,5,"추측해서 구현",new[]{world.gameObject,after.gameObject,noteDark.gameObject,stain.gameObject,editor.gameObject},null,"",1,0,3);
                Camera(cuts,5,new Vector2(-748.125f,-376.8125f),2.1f,0);
                cuts.GetArrayElementAtIndex(5).FindPropertyRelative("_hardCutOnEnter").boolValue=true;
                Beat(beats,0,"NOTE 1",0,target:lines[0],text:"// NOTE: 기획서에 커피 쏟음",typewriter:true);
                Beat(beats,1,"NOTE 2",.25f,target:lines[1],text:"// NOTE: 기획자 연락 안 됨",typewriter:true);
                Beat(beats,2,"TODO",.25f,target:lines[2],text:"// TODO: 남은 글자로 추측해서 구현",typewriter:true);
                so.ApplyModifiedProperties();
                dark.gameObject.SetActive(false);after.gameObject.SetActive(false);noteDark.gameObject.SetActive(false);nightEditor.gameObject.SetActive(false);editor.gameObject.SetActive(false);close.gameObject.SetActive(false);stain.gameObject.SetActive(false);
                partner.gameObject.SetActive(false);mine.gameObject.SetActive(false);island.gameObject.SetActive(false);
                EditorSceneManager.MarkSceneDirty(scene);Selection.activeGameObject=obj;Undo.CollapseUndoOperations(group);
                Debug.Log("[Intro] 리워크 생성 완료. Ctrl+S. 컷2 Sfx On Start에 딸깍, 컷3 Beats에 툭/주르륵, 메시지 비트에 효과음을 할당하세요. 백업: "+backup,director);
                bool registered=false;foreach(EditorBuildSettingsScene entry in EditorBuildSettings.scenes) if(entry.path==IntroPath && entry.enabled) registered=true;
                if(!registered) Debug.LogWarning("Build Profiles Scene List에 Intro 씬을 등록해 주세요.");
            }
            catch(Exception e){Undo.RevertAllDownToGroup(group);Debug.LogException(e);}
        }
        private static RectTransform EditorScreen(Transform parent,string name,bool comments,out TMP_Text[] lines)
        {
            RectTransform editor=Panel(name,parent,Vector2.zero,new Vector2(960,638),Hex("1E1F22"));
            Panel("TitleBar",editor,new Vector2(0,286),new Vector2(960,66),Hex("2D2F34"));
            Text("Title",editor,"CoffeeSpec - Editor",new Vector2(-140,286),new Vector2(600,60),27,_codeFont,Hex("D4D4D4"));
            Panel("TabBar",editor,new Vector2(0,225),new Vector2(960,56),Hex("18191C"));
            Panel("Tab",editor,new Vector2(-295,225),new Vector2(310,56),Hex("1E1F22"));
            Text("TabName",editor,"title.cs",new Vector2(-275,225),new Vector2(270,50),30,_codeFont,Hex("D4D4D4"));
            string[] code=comments?new[]{"","",""}:new[]{"void Update()","{","    ReadSpec();","}"};
            lines=new TMP_Text[code.Length];
            for(int i=0;i<code.Length;i++)
            {
                float y=140-i*82;
                Text("LineNumber"+(i+1),editor,(i+1).ToString(),new Vector2(-420,y),new Vector2(55,74),34,_codeFont,Hex("5C6370"));
                lines[i]=Text("Line"+(i+1),editor,code[i],new Vector2(25,y),new Vector2(800,74),34,_codeFont,Hex(comments?"4EC9B0":"D4D4D4"));
            }
            RectTransform cursor=Panel("BlinkingCursor",editor,new Vector2(-350,140-(code.Length-1)*82),new Vector2(5,38),Hex("D4D4D4"));
            CanvasGroup cg=Undo.AddComponent<CanvasGroup>(cursor.gameObject);
            IntroPixelBlink blink=Undo.AddComponent<IntroPixelBlink>(cursor.gameObject);
            SerializedObject bs=new SerializedObject(blink);bs.FindProperty("_target").objectReferenceValue=cg;
            SerializedProperty follow=bs.FindProperty("_lines");follow.arraySize=lines.Length;
            for(int i=0;i<lines.Length;i++)follow.GetArrayElementAtIndex(i).objectReferenceValue=lines[i];
            bs.ApplyModifiedProperties();return editor;
        }
        private static void BeatCamera(SerializedProperty beats,int index,Vector2 position,float scale,float duration)
        {
            SerializedProperty b=beats.GetArrayElementAtIndex(index);
            b.FindPropertyRelative("_moveCamera").boolValue=true;
            b.FindPropertyRelative("_cameraPosition").vector2Value=position;
            b.FindPropertyRelative("_cameraScale").floatValue=scale;
            b.FindPropertyRelative("_moveDuration").floatValue=duration;
        }
        private static void Camera(SerializedProperty cuts,int index,Vector2 position,float scale,float duration,bool blackBefore=false,bool blackAfter=false,GameObject reveal=null)
        {
            SerializedProperty c=cuts.GetArrayElementAtIndex(index);
            c.FindPropertyRelative("_cameraEnabled").boolValue=true;
            c.FindPropertyRelative("_cameraPosition").vector2Value=position;
            c.FindPropertyRelative("_cameraScale").floatValue=scale;
            c.FindPropertyRelative("_moveDuration").floatValue=duration;
            c.FindPropertyRelative("_holdBeforeMove").floatValue=0;
            c.FindPropertyRelative("_hardCutOnEnter").boolValue=false;
            c.FindPropertyRelative("_revealDuration").floatValue=0;
            c.FindPropertyRelative("_blackBefore").boolValue=blackBefore;
            c.FindPropertyRelative("_blackAfter").boolValue=blackAfter;
            c.FindPropertyRelative("_afterMoveActivate").objectReferenceValue=reveal;
        }
        private static void ImportSprites()
        {
            PointTitleSprite("title_doc_closeup.png");
            PointTitleSprite("ui_postit_build.png");
            string[] files=Directory.GetFiles(IntroArt,"*.png");
            foreach(string file in files)
            {
                string path=file.Replace('\\','/');
                AssetDatabase.ImportAsset(path);
                TextureImporter importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null)continue;
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
                importer.filterMode=FilterMode.Point;importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled=false;importer.sRGBTexture=true;importer.alphaIsTransparency=true;importer.spritePixelsPerUnit=100;
                importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=2048;
                importer.spriteBorder=(file.Contains("bubble") || file.Contains("panel"))?new Vector4(8,8,8,8):Vector4.zero;
                importer.SaveAndReimport();
            }
        }
        private static void PointTitleSprite(string file)
        {
            TextureImporter importer=AssetImporter.GetAtPath(Art+file) as TextureImporter;
            if(importer==null)return;
            if(importer.filterMode==FilterMode.Point && !importer.mipmapEnabled && importer.textureCompression==TextureImporterCompression.Uncompressed)return;
            importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        }
        private static RectTransform Pixel(string name,Transform parent,string file,Vector2 pos,Vector2 size,bool sliced=false)
        {
            RectTransform rt=Panel(name,parent,pos,size,Color.white);
            Image image=rt.GetComponent<Image>();image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(IntroArt+file+".png");
            if(image.sprite==null)Debug.LogWarning("Missing Intro Sprite: "+file);
            if(sliced){image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=1;}
            return rt;
        }

        private static string Backup()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string folder = Path.Combine(root, "Backup", "Intro_rework3_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(folder);
            foreach (string path in new[] { IntroPath, "Assets/_Project/Scripts/UI/IntroDirector.cs", "Assets/_Project/Scripts/Editor/IntroSceneBuilder.cs", "Assets/_Project/Scripts/UI/IntroPixelBlink.cs", Art + "title_doc_closeup.png", Art + "ui_postit_build.png", "ProjectSettings/EditorBuildSettings.asset" })
            {
                if (File.Exists(path)) File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), false);
                if (File.Exists(path + ".meta")) File.Copy(path + ".meta", Path.Combine(folder, Path.GetFileName(path) + ".meta"), false);
            }
            return folder;
        }

        private static void EnsureEventSystem(Scene scene)
        {
            EventSystem existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing != null)
            {
                if (existing.GetComponent<InputSystemUIInputModule>() == null)
                {
                    Debug.LogWarning("[IntroSceneBuilder] 기존 EventSystem은 유지했습니다. InputSystemUIInputModule을 확인해 주세요.", existing);
                }
                return;
            }
            GameObject go = new GameObject("EventSystem");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
            Undo.AddComponent<EventSystem>(go);
            InputSystemUIInputModule module = Undo.AddComponent<InputSystemUIInputModule>(go);
            module.AssignDefaultActions();
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            else SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            return rt;
        }
        private static void Anchor(RectTransform rt, Vector2 anchor) { rt.anchorMin = rt.anchorMax = anchor; }
        private static RectTransform Full(string name, Transform parent)
        {
            RectTransform rt = Rect(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }
        private static RectTransform Panel(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            RectTransform rt = Rect(name, parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image image = Undo.AddComponent<Image>(rt.gameObject);
            image.color = color;
            image.raycastTarget = false;
            return rt;
        }
        private static RectTransform PanelFull(string name, Transform parent, Color color)
        {
            RectTransform rt = Full(name, parent);
            Image image = Undo.AddComponent<Image>(rt.gameObject);
            image.color = color;
            image.raycastTarget = false;
            return rt;
        }
        private static RectTransform Picture(string name, Transform parent, string file, Vector2 pos, Vector2 size, Color tint)
        {
            RectTransform rt = Panel(name, parent, pos, size, tint);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + file);
            if (sprite == null)
            {
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(Art + file))
                    if (asset is Sprite candidate) { sprite = candidate; break; }
            }
            if (sprite == null) Debug.LogWarning("[IntroSceneBuilder] Sprite 누락, 단색 대체: " + Art + file);
            rt.GetComponent<Image>().sprite = sprite;
            return rt;
        }
        private static TMP_Text Text(string name, Transform parent, string value, Vector2 pos, Vector2 size, float fontSize, TMP_FontAsset font, Color color)
        {
            RectTransform rt = Rect(name, parent);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            TextMeshProUGUI text = Undo.AddComponent<TextMeshProUGUI>(rt.gameObject);
            if (font != null) text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }
        private static void CleanPaper(Transform parent, Vector2 pos, Vector2 size, Color? tint = null)
        {
            RectTransform rt = Panel("CleanDocument", parent, pos, size, tint ?? Hex("F9F9FB"));
            Panel("Heading", rt, new Vector2(-12, 28), new Vector2(125, 5), Hex("727A83"));
            for (int i = 0; i < 4; i++)
                Panel("Rule" + i, rt, new Vector2(0, 10 - i * 12), new Vector2(145, 3), Hex("B4BDC5"));
        }
        private static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color color); return color; }
        private static SerializedProperty Cut(SerializedProperty cuts, int index, string label, GameObject[] objects, TMP_Text time, string timeText, float duration, float fade, int count)
        {
            SerializedProperty c = cuts.GetArrayElementAtIndex(index);
            c.FindPropertyRelative("_label").stringValue = label;
            SerializedProperty list = c.FindPropertyRelative("_objects");
            list.arraySize = objects.Length;
            for (int i = 0; i < objects.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
            c.FindPropertyRelative("_timeLabel").objectReferenceValue = time;
            c.FindPropertyRelative("_timeText").stringValue = timeText;
            c.FindPropertyRelative("_duration").floatValue = duration;
            c.FindPropertyRelative("_fadeIn").floatValue = fade;
            c.FindPropertyRelative("_sfxOnStart").objectReferenceValue = null;
            SerializedProperty beats = c.FindPropertyRelative("_beats");
            beats.arraySize = count;
            return beats;
        }
        private static void Beat(SerializedProperty beats, int index, string label, float delay, GameObject activate = null, GameObject deactivate = null, TMP_Text target = null, string text = "", bool typewriter = false)
        {
            SerializedProperty b = beats.GetArrayElementAtIndex(index);
            b.FindPropertyRelative("_label").stringValue = label;
            b.FindPropertyRelative("_delay").floatValue = delay;
            b.FindPropertyRelative("_activate").objectReferenceValue = activate;
            b.FindPropertyRelative("_deactivate").objectReferenceValue = deactivate;
            b.FindPropertyRelative("_textTarget").objectReferenceValue = target;
            b.FindPropertyRelative("_text").stringValue = text;
            b.FindPropertyRelative("_typewriter").boolValue = typewriter;
            b.FindPropertyRelative("_charInterval").floatValue = 0.05f;
            b.FindPropertyRelative("_sfx").objectReferenceValue = null;
            b.FindPropertyRelative("_emphasize").objectReferenceValue = null;
            b.FindPropertyRelative("_moveCamera").boolValue = false;
            b.FindPropertyRelative("_cameraPosition").vector2Value = Vector2.zero;
            b.FindPropertyRelative("_cameraScale").floatValue = 1;
            b.FindPropertyRelative("_moveDuration").floatValue = 0;
        }
    }
}
#endif
