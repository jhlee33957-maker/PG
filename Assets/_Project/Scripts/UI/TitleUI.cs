using System;
using System.Collections.Generic;
using CoffeeSpec.Core;
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

            [Tooltip("이 스테이지의 씬 이름. Build Profiles의 Scene List에 등록되어 있어야 한다")]
            [SerializeField] private string _sceneName;

            public Button Button => _button;
            public Image Image => _image;
            public string SceneName => _sceneName;
        }

        [Header("타이틀 버튼")]
        [SerializeField] private Button _btnGameStart;
        [SerializeField] private Button _btnStageSelect;
        [SerializeField] private Button _btnGuide;
        [SerializeField] private Button _btnExit;

        [Header("전체 클리어 팝업 (게임 시작 시)")]
        [SerializeField] private Popup _clearedPopup;
        [Tooltip("예: Intro 씬으로 이동해 처음부터 다시 시작")]
        [SerializeField] private Button _btnClearedYes;
        [Tooltip("아니오: 팝업을 닫고 타이틀에 남음")]
        [SerializeField] private Button _btnClearedNo;

        [Header("기타 팝업")]
        [SerializeField] private Popup _stageSelectPopup;
        [SerializeField] private Popup _guidePopup;

        [Header("스테이지 목록 (위에서부터 스테이지 1, 2, 3 ...)")]
        [SerializeField] private List<StageEntry> _stages = new List<StageEntry>();

        [Header("씬")]
        [Tooltip("클리어 팝업에서 '예'를 눌렀을 때 이동할 씬")]
        [SerializeField] private string _introSceneName = "Intro";

        [Tooltip("켜면 세이브가 없을 때 '게임 시작'을 누르면 Intro 씬을 먼저 보여 준다. 끄면 바로 스테이지 1")]
        [SerializeField] private bool _playIntroOnNewGame = true;

        [Header("테스트용 가짜 세이브 (에디터에서만 동작)")]
        [Tooltip("켜면 SaveData.csv 대신 아래 값을 사용한다")]
        [SerializeField] private bool _useDebugSave;
        [Tooltip("끄면 '기록 없음' 상태")]
        [SerializeField] private bool _debugHasRecord = true;
        [Tooltip("0 = 진행 중인 판 없음 (엔딩 직후)")]
        [SerializeField, Min(0)] private int _debugLastPlayedStage = 1;
        [SerializeField, Min(0)] private int _debugHighestClearedStage;

        private static readonly Color LockedImageColor = Color.black;

        // 잠금을 풀 때 원래 색으로 되돌리기 위해 저장해 둔다
        private readonly Dictionary<Image, Color> _originalImageColors = new Dictionary<Image, Color>();

        private void Awake()
        {
            Bind(_btnGameStart, OnClickGameStart, "BTN_GameStart");
            Bind(_btnStageSelect, OnClickStageSelect, "BTN_StageSelect");
            Bind(_btnGuide, OnClickGuide, "BTN_Guide");
            Bind(_btnExit, OnClickExit, "BTN_Exit");
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
            HidePopup(_stageSelectPopup);
            HidePopup(_guidePopup);
        }

        private void Start()
        {
            RefreshTitleButtons();
        }

        /// <summary>
        /// 세이브 기록이 없으면 스테이지 선택 버튼을 비활성화한다.
        /// 타이틀 씬은 들어올 때마다 새로 로드되므로 시작할 때 한 번 갱신하면 된다.
        /// </summary>
        private void RefreshTitleButtons()
        {
            if (_btnStageSelect != null)
            {
                _btnStageSelect.interactable = LoadSave() != null;
            }
        }

        // ---------------- 버튼 동작 ----------------

        private void OnClickGameStart()
        {
            if (SceneFader.IsLoading)
            {
                return;
            }

            SaveData save = LoadSave();

            // 세이브가 없으면 인트로 대신 스테이지 1로 바로 시작한다
            if (save == null)
            {
                if (_playIntroOnNewGame)
                {
                    SceneFader.LoadScene(_introSceneName);
                }
                else
                {
                    StartStage(1);
                }
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

            // 새로 시작하는 판은 스토리 인트로부터 보여 준다
            HidePopup(_clearedPopup);
            SceneFader.LoadScene(_introSceneName);
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

            SceneFader.LoadScene(_stages[stageNumber - 1].SceneName);
        }

        /// <summary>
        /// 해제된 스테이지는 버튼을 켜고 이미지를 원래 색으로,
        /// 잠긴 스테이지는 버튼을 끄고 이미지를 검은색으로 만든다.
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
        /// - 그 외: 클리어한 가장 높은 스테이지까지
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

            return Mathf.Clamp(save.HighestClearedStage, 1, _stages.Count);
        }

        /// <summary>마지막 스테이지까지 클리어했으면 전체 클리어로 본다.</summary>
        private bool IsAllCleared(SaveData save)
        {
            return save != null && _stages.Count > 0 && save.HighestClearedStage >= _stages.Count;
        }

        // ---------------- 공용 ----------------

        private SaveData LoadSave()
        {
            // 빌드에서 실수로 켜 둔 채 나가도 진짜 세이브를 쓰도록 에디터에서만 적용한다
            if (_useDebugSave && Application.isEditor)
            {
                // 실제 세이브와 같은 규칙: 플레이 기록과 클리어 기록이 둘 다 0이면 기록 없음
                bool hasAnyRecord = _debugLastPlayedStage > 0 || _debugHighestClearedStage > 0;
                return _debugHasRecord && hasAnyRecord
                    ? new SaveData(_debugLastPlayedStage, _debugHighestClearedStage)
                    : null;
            }
            return SaveDataStore.Load();
        }

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
