using System;
using System.Collections.Generic;
using CoffeeSpec.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 타이틀 화면. 버튼, 팝업, 스테이지 목록은 모두 Inspector에서 할당한다.
    /// 세이브 상태에 따른 동작 규칙은 docs/decisions/003-save-data-and-stage-unlock.md 참고.
    /// </summary>
    public class TitleUI : MonoBehaviour
    {
        [Serializable]
        private class StageEntry
        {
            [Tooltip("스테이지 버튼 (BTN_Stage1, BTN_Stage2 ...)")]
            [SerializeField] private Button _button;

            [Tooltip("스테이지 이미지. 잠긴 스테이지는 검은색이 된다")]
            [SerializeField] private Image _image;

            [Tooltip("이 스테이지의 씬 이름. 목록 순서대로 Stage1Scene, Stage2Scene ...이 자동으로 들어간다. Build Profiles의 Scene List에 등록되어 있어야 한다")]
            [SerializeField, ReadOnly] private string _sceneName;

            public Button Button => _button;
            public Image Image => _image;
            public string SceneName => _sceneName;

            public void SetSceneName(string sceneName) => _sceneName = sceneName;
        }

        [Header("타이틀 버튼")]
        [SerializeField] private Button _btnGameStart;
        [Tooltip("클리어 기록을 모두 지우고 스테이지 1부터 시작")]
        [SerializeField] private Button _btnNewGame;
        [SerializeField] private Button _btnStageSelect;
        [SerializeField] private Button _btnGuide;
        [SerializeField] private Button _btnExit;

        [Header("전체 클리어 팝업 (게임 시작 시)")]
        [SerializeField] private Popup _clearedPopup;
        [Tooltip("예: 스테이지 1부터 다시 시작")]
        [SerializeField] private Button _btnClearedYes;
        [Tooltip("아니오: 팝업을 닫고 타이틀에 남음")]
        [SerializeField] private Button _btnClearedNo;

        [Header("기타 팝업")]
        [SerializeField] private Popup _stageSelectPopup;
        [SerializeField] private Popup _guidePopup;

        [Header("스테이지 목록 (위에서부터 스테이지 1, 2, 3 ...)")]
        [SerializeField] private List<StageEntry> _stages = new List<StageEntry>();

        [Header("테스트용 가짜 세이브 (에디터에서만 동작)")]
        [Tooltip("켜면 SaveData.csv 대신 아래 값을 사용한다")]
        [SerializeField] private bool _useDebugSave;
        [Tooltip("끄면 '기록 없음' 상태")]
        [SerializeField] private bool _debugHasRecord = true;
        [Tooltip("0 = 진행 중인 판 없음 (엔딩 직후)")]
        [SerializeField, Min(0)] private int _debugLastPlayedStage = 1;
        [SerializeField, Min(0)] private int _debugHighestClearedStage;

        private static readonly Color LockedImageColor = Color.black;

        private const string NewGameTitle = "정말 처음부터 할까요?";
        // 팝업 Message는 자동 줄바꿈이 꺼져 있어(NoWrap) 길면 창 밖으로 나간다. 그래서 문구 안에서 직접 줄을 나눈다.
        // 둘째 줄 앞에는 첫 줄의 '⚠ '와 똑같은 글자를 투명하게 넣어, 글꼴과 상관없이 '스테이지'가 '클리어'와 같은 세로줄에서 시작하게 한다
        private const string NewGameMessage = "<color=#E8913B>\u26A0</color> 클리어 기록이 모두 지워지고\n<color=#00000000>\u26A0</color> 스테이지 1부터 시작합니다.";

        // 새 게임 확인 팝업. 전체 클리어 팝업과 모양·버튼 배치가 같아서 실행할 때 복제해 문구만 바꿔 쓴다
        private Popup _newGamePopup;

        // 잠금을 풀 때 원래 색으로 되돌리기 위해 저장해 둔다
        private readonly Dictionary<Image, Color> _originalImageColors = new Dictionary<Image, Color>();

        private void Awake()
        {
            // 원본 버튼에 리스너를 달기 전에 복제해야 복제본 버튼이 깨끗하다
            CreateNewGamePopup();

            Bind(_btnGameStart, OnClickGameStart, "BTN_GameStart");
            Bind(_btnNewGame, OnClickNewGame, "BTN_NewGame");
            Bind(_btnStageSelect, OnClickStageSelect, "BTN_StageSelect");
            Bind(_btnGuide, OnClickGuide, "BTN_Guide");
            Bind(_btnExit, OnClickExit, "BTN_Exit");
#if UNITY_WEBGL && !UNITY_EDITOR
            // 브라우저에서는 게임을 끌 수 없으므로 Exit 줄을 없앤다
            HideExitLine();
#endif
            Bind(_btnClearedYes, OnClickClearedYes, "전체 클리어 팝업의 예 버튼");
            Bind(_btnClearedNo, OnClickClearedNo, "전체 클리어 팝업의 아니오 버튼");

            for (int i = 0; i < _stages.Count; i++)
            {
                int stageNumber = i + 1; // 람다가 반복 변수를 잡지 않도록 복사
                StageEntry stage = _stages[i];

                Bind(stage.Button, () => StartStage(stageNumber), $"스테이지 {stageNumber} 버튼");

                if (stage.Image != null)
                {
                    _originalImageColors[stage.Image] = stage.Image.color;
                }
            }

            // 에디터에서 켜 둔 채 저장했더라도 시작할 때는 모든 팝업을 닫는다
            HidePopup(_clearedPopup);
            HidePopup(_newGamePopup);
            HidePopup(_stageSelectPopup);
            HidePopup(_guidePopup);
        }

        private void Start()
        {
            RefreshTitleButtons();
        }


        // 목록에 요소를 추가하거나 순서를 바꾸면 씬 이름을 번호에 맞게 다시 채운다.
        // 인스펙터에서는 읽기 전용이라 사람이 직접 고칠 수 없다.
        private void OnValidate()
        {
            if (_stages == null)
            {
                return;
            }

            for (int i = 0; i < _stages.Count; i++)
            {
                if (_stages[i] == null)
                {
                    _stages[i] = new StageEntry();
                }
                _stages[i].SetSceneName(StageScenes.GetSceneName(i + 1));
            }
        }

        /// <summary>
        /// 세이브 기록이 없으면 스테이지 선택 버튼을 비활성화한다.
        /// 타이틀 씬은 들어올 때마다 새로 로드되므로 시작할 때 한 번 갱신하면 된다.
        /// </summary>
        private void RefreshTitleButtons()
        {
            SaveData save = LoadSave();
            if (_btnStageSelect != null)
            {
                _btnStageSelect.interactable = save != null;
            }
            RefreshStageButtons(save);
        }

        // ---------------- 버튼 동작 ----------------

        private void OnClickGameStart()
        {
            if (SceneFader.IsLoading)
            {
                return;
            }

            SaveData save = LoadSave();

            // 세이브가 없으면 스테이지 1부터 시작한다 (인트로 씬은 쓰지 않는다)
            if (save == null)
            {
                StartStage(1);
                return;
            }

            // 진행 중인 판이 있으면 엔딩을 본 적이 있어도 이어서 한다
            if (save.HasRunInProgress)
            {
                StartStage(save.LastPlayedStage);
                return;
            }

            // 진행 중인 판이 없고 전체 클리어 = 엔딩 직후
            if (IsAllCleared(save))
            {
                OpenPopup(_clearedPopup, "전체 클리어 팝업");
                return;
            }

            // 진행 중인 판도 없고 전체 클리어도 아닌 경우는 세이브 규칙상 생기면 안 된다
            Debug.LogWarning($"[TitleUI] 진행 중인 판 없이 스테이지 {save.HighestClearedStage}까지만 클리어된 세이브입니다. 스테이지 1부터 시작합니다.", this);
            StartStage(1);
        }

        private void OnClickNewGame()
        {
            if (SceneFader.IsLoading)
            {
                return;
            }

            // 기록을 되돌릴 수 없으니 한 번 더 묻는다. 팝업을 못 만들었으면 묻지 않고 바로 시작한다
            if (_newGamePopup == null)
            {
                StartNewGame();
                return;
            }
            OpenPopup(_newGamePopup, "새 게임 확인 팝업");
        }

        private void StartNewGame()
        {
            if (SceneFader.IsLoading)
            {
                return;
            }

            HidePopup(_newGamePopup);
            SaveDataStore.DeleteAll();

            // 기록이 없어졌으니 스테이지 선택도 다시 잠근다 (씬이 넘어가기 전 잠깐 보이는 화면도 맞추기 위해)
            RefreshTitleButtons();
            StartStage(1);
        }

        /// <summary>
        /// 전체 클리어 팝업(POPUP_Restart)을 복제해 새 게임 확인 팝업을 만든다.
        /// 씬에 팝업을 하나 더 두면 디자인을 바꿀 때 두 군데를 고쳐야 하므로, 원본 하나만 두고 복제해서 쓴다.
        /// 예/아니오 버튼은 원본 버튼과 같은 위치의 오브젝트를, 문구는 Title/Message 오브젝트를 찾아 바꾼다.
        /// </summary>
        private void CreateNewGamePopup()
        {
            if (_clearedPopup == null || _btnClearedYes == null || _btnClearedNo == null)
            {
                Debug.LogWarning("[TitleUI] 전체 클리어 팝업이 연결되지 않아 새 게임 확인 팝업 없이 바로 시작합니다.", this);
                return;
            }

            Transform source = _clearedPopup.transform;
            string yesPath = GetRelativePath(source, _btnClearedYes.transform);
            string noPath = GetRelativePath(source, _btnClearedNo.transform);
            if (yesPath == null || noPath == null)
            {
                Debug.LogWarning("[TitleUI] 전체 클리어 팝업의 예/아니오 버튼이 팝업 안에 없어 새 게임 확인 팝업을 만들지 못했습니다.", this);
                return;
            }

            Popup popup = Instantiate(_clearedPopup, source.parent);
            popup.name = "POPUP_NewGame";
            Transform root = popup.transform;

            Button yes = root.Find(yesPath)?.GetComponent<Button>();
            Button no = root.Find(noPath)?.GetComponent<Button>();
            if (yes == null || no == null)
            {
                Destroy(popup.gameObject);
                return;
            }
            Bind(yes, StartNewGame, "새 게임 확인 팝업의 예 버튼");
            Bind(no, () => HidePopup(_newGamePopup), "새 게임 확인 팝업의 아니오 버튼");

            SetText(root, "Title", NewGameTitle);
            SetText(root, "Message", NewGameMessage);

            _newGamePopup = popup;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            string path = null;
            for (Transform t = target; t != null; t = t.parent)
            {
                if (t == root)
                {
                    return path ?? string.Empty;
                }
                path = path == null ? t.name : $"{t.name}/{path}";
            }
            return null; // root 밖에 있음
        }

        private void SetText(Transform root, string objectName, string text)
        {
            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.name == objectName)
                {
                    label.text = text;
                    return;
                }
            }
            Debug.LogWarning($"[TitleUI] 새 게임 확인 팝업에서 {objectName} 텍스트를 찾지 못해 원래 문구를 그대로 씁니다.", this);
        }

        private void OnClickStageSelect()
        {
            SaveData save = LoadSave();

            // 버튼이 비활성화되어 있어 보통은 여기까지 오지 않지만,
            // 타이틀에 있는 동안 세이브가 사라지는 경우를 대비해 한 번 더 막는다
            if (save == null)
            {
                return;
            }

            RefreshStageButtons(save);
            OpenPopup(_stageSelectPopup, "스테이지 선택 팝업");
        }

        private void OnClickGuide()
        {
            OpenPopup(_guidePopup, "가이드 팝업");
        }

        /// <summary>
        /// 웹 빌드용. Exit 버튼과 그 코드 줄(Line14_BTN_Exit)을 숨기고, 바로 아래 '}' 줄의 글자를 이 자리로 올려 빈 줄이 남지 않게 한다.
        /// </summary>
        private void HideExitLine()
        {
            if (_btnExit != null)
            {
                _btnExit.gameObject.SetActive(false);
            }

            TitleMenuLine exitLine = null;
            foreach (TitleMenuLine line in FindObjectsByType<TitleMenuLine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (line.name.EndsWith("BTN_Exit", StringComparison.Ordinal))
                {
                    exitLine = line;
                    break;
                }
            }
            if (exitLine == null)
            {
                return;
            }

            // 줄 번호(14)는 그대로 두고 코드 글자만 '}'로 바꾼다. 컴포넌트를 끄면 마우스에도 반응하지 않는다
            exitLine.enabled = false;
            Transform parent = exitLine.transform.parent;
            int index = exitLine.transform.GetSiblingIndex();
            TMP_Text code = FindCodeText(exitLine.transform);
            TMP_Text brace = null;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform sibling = parent.GetChild(i);
                    if (i == index) continue;
                    TMP_Text text = FindCodeText(sibling);
                    if (text != null && text.text.Trim() == "}")
                    {
                        brace = text;
                        break;
                    }
                }
            }

            if (code != null)
            {
                code.text = brace != null ? brace.text : string.Empty;
                if (brace != null) code.color = brace.color;
            }
            if (brace != null)
            {
                brace.transform.parent.gameObject.SetActive(false);
            }
            foreach (Image image in exitLine.GetComponentsInChildren<Image>(true))
            {
                if (image.gameObject != exitLine.gameObject) image.enabled = false; // 하이라이트, 커서
                else image.raycastTarget = false;
            }
        }

        private static TMP_Text FindCodeText(Transform line)
        {
            Transform code = line.Find("Code");
            return code != null ? code.GetComponent<TMP_Text>() : null;
        }

        private void OnClickExit()
        {
            if (SceneFader.IsLoading)
            {
                return;
            }

#if UNITY_EDITOR
            // 에디터에서는 Application.Quit()이 무시되므로 Play 모드를 끝내는 것으로 대신한다
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnClickClearedYes()
        {
            if (SceneFader.IsLoading)
            {
                return;
            }

            // 새로 시작하는 판은 스테이지 1부터
            HidePopup(_clearedPopup);
            StartStage(1);
        }

        private void OnClickClearedNo()
        {
            HidePopup(_clearedPopup);
        }

        // ---------------- 스테이지 ----------------

        private void StartStage(int stageNumber)
        {
            if (SceneFader.IsLoading)
            {
                return;
            }

            if (stageNumber < 1 || stageNumber > _stages.Count)
            {
                Debug.LogError($"[TitleUI] 스테이지 {stageNumber}이(가) 스테이지 목록에 없습니다. (등록된 스테이지 수: {_stages.Count})", this);
                return;
            }

            // 씬을 다시 저장하지 않았더라도 규칙대로 로드되도록 직렬화 값 대신 번호로 이름을 만든다
            SceneFader.LoadScene(StageScenes.GetSceneName(stageNumber));
        }

        /// <summary>
        /// 해제된 스테이지는 버튼을 켜고 이미지를 원래 색으로, 섬네일을 보여 준다.
        /// 잠긴 스테이지는 버튼을 끄고 이미지를 검은색으로 만들며 섬네일을 숨긴다.
        /// </summary>
        private void RefreshStageButtons(SaveData save)
        {
            int unlockedCount = GetUnlockedStageCount(save);

            for (int i = 0; i < _stages.Count; i++)
            {
                StageEntry stage = _stages[i];
                bool unlocked = i < unlockedCount;

                if (stage.Button != null)
                {
                    stage.Button.interactable = unlocked;
                    Transform thumb = stage.Button.transform.Find("Thumb");
                    if (thumb != null)
                    {
                        thumb.gameObject.SetActive(unlocked);
                    }
                }

                if (stage.Image != null)
                {
                    stage.Image.color = unlocked ? _originalImageColors[stage.Image] : LockedImageColor;
                }
            }
        }

        /// <summary>
        /// 열 수 있는 스테이지 개수.
        /// - 전체 클리어: 전부
        /// - 그 외: 클리어한 가장 높은 스테이지의 다음 스테이지까지 (1을 깼으면 2까지)
        /// - 단, 처음 하는 사람도 고를 수 있도록 스테이지 1은 항상 열린다
        /// </summary>
        private int GetUnlockedStageCount(SaveData save)
        {
            if (_stages.Count == 0)
            {
                return 0;
            }

            if (save == null)
            {
                return 1;
            }

            if (IsAllCleared(save))
            {
                return _stages.Count;
            }

            // 클리어하면 바로 다음 스테이지를 골라 시작할 수 있어야 한다
            return Mathf.Clamp(save.HighestClearedStage + 1, 1, _stages.Count);
        }

        /// <summary>마지막 스테이지까지 클리어했으면 전체 클리어로 본다.</summary>
        private bool IsAllCleared(SaveData save)
        {
            return save != null && _stages.Count > 0 && save.HighestClearedStage >= _stages.Count;
        }

        // ---------------- 공용 ----------------

        private static SaveData LoadSave() => SaveDataStore.Load();

        private void OpenPopup(Popup popup, string label)
        {
            if (popup == null)
            {
                Debug.LogError($"[TitleUI] {label}이(가) 할당되지 않았습니다.", this);
                return;
            }
            popup.Open();
        }

        private static void HidePopup(Popup popup)
        {
            if (popup != null)
            {
                popup.Close();
            }
        }

        private void Bind(Button button, UnityAction action, string label)
        {
            if (button == null)
            {
                Debug.LogError($"[TitleUI] {label}이(가) 할당되지 않았습니다.", this);
                return;
            }
            button.onClick.AddListener(action);
        }
    }
}
