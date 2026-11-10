using System.Collections;
using System.Text.RegularExpressions;
using CoffeeSpec.Rules;
using CoffeeSpec.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoffeeSpec.Core
{
    /// <summary>규칙의 결과 이벤트를 받아 클리어 화면과 스테이지 이동만 맡는다.</summary>
    public sealed class GameManager : MonoBehaviour
    {
        [Tooltip("규칙 관리자. 비워 두면 씬에서 찾는다")]
        [SerializeField] private RuleManager _ruleManager;
        [Tooltip("클리어하고 다음 스테이지로 넘어가기까지 기다리는 시간(초). 시간이 멈춰 있어도 흐른다")]
        [SerializeField, Min(0f)] private float _nextStageDelay = 2f;
        [Tooltip("다음 스테이지 씬이 없을 때 돌아갈 씬 이름")]
        [SerializeField] private string _titleSceneName = "Title";
        [Tooltip("고양이가 캣도어로 들어가는 연출이 끝난 뒤 다음 스테이지로 넘어가기까지 더 기다리는 시간(초)")]
        [SerializeField, Min(0f)] private float _afterEnterDelay = 0.4f;

        private RuleManager _subscribedManager;
        private Coroutine _nextStageRoutine;
        private bool _cleared;
        private bool _gameOver;
        private bool _warnedMissing;

        private void Awake() => ResolveManager();

        // 스테이지에 들어오면 타이틀의 "게임 시작"이 이 스테이지로 이어지도록 기록한다
        private void Start()
        {
            if (StageScenes.TryGetStageNumber(SceneManager.GetActiveScene().name, out int stageNumber))
            {
                SaveDataStore.RecordStageEntered(stageNumber);
            }
        }

        private void OnEnable()
        {
            RuleManager.Available += Bind;
            ResolveManager();
        }

        private void ResolveManager()
        {
            if (_ruleManager == null) _ruleManager = RuleManager.Instance;
            if (_ruleManager == null) _ruleManager = FindAnyObjectByType<RuleManager>();
            if (_ruleManager == null)
            {
                if (!_warnedMissing) Debug.LogWarning("[GameManager] RuleManager가 없어 진행 이벤트를 기다리는 중", this);
                _warnedMissing = true;
                return;
            }
            if (isActiveAndEnabled) Bind(_ruleManager);
        }

        private void Bind(RuleManager manager)
        {
            if (!isActiveAndEnabled || manager == null || _subscribedManager == manager) return;
            // 인스펙터로 지정한 관리자가 있으면 다른 관리자의 생성 이벤트는 무시한다.
            if (_ruleManager != null && _ruleManager != manager) return;
            Unsubscribe();
            _ruleManager = manager;
            _subscribedManager = manager;
            manager.Cleared += OnCleared;
            manager.ControlledDeathChanged += OnControlledDeathChanged;
            manager.Reset += OnReset;
            // 늦게 켜져도 이미 확정된 규칙 상태와 화면을 맞춘다.
            if (manager.IsCleared) OnCleared();
            else OnControlledDeathChanged(manager.AllControlledDead);
        }

        private void OnDisable()
        {
            RuleManager.Available -= Bind;
            Unsubscribe();
            CancelNextStage();
            _cleared = false;
            _gameOver = false;
            ExitEnterAnimation.Cancel();
            ClearScreen.Hide();
        }

        private void Unsubscribe()
        {
            if (_subscribedManager == null) return;
            _subscribedManager.Cleared -= OnCleared;
            _subscribedManager.ControlledDeathChanged -= OnControlledDeathChanged;
            _subscribedManager.Reset -= OnReset;
            _subscribedManager = null;
        }

        private void OnCleared()
        {
            if (_cleared || _gameOver) return;
            _cleared = true;
            RecordClear();
            ClearScreen.Show();
            CancelNextStage();
            // 고양이가 문으로 들어가는 연출이 있으면 플랩이 닫힐 때까지는 다음 스테이지로 넘어가지 않는다
            float delay = _nextStageDelay;
            if (ExitEnterAnimation.TryPlay(out float enterTime)) delay = Mathf.Max(delay, enterTime + _afterEnterDelay);
            _nextStageRoutine = StartCoroutine(GoToNextStage(delay));
        }

        private void OnControlledDeathChanged(bool allDead)
        {
            // 사망 문구는 띄우지 않는다. 조종 대상이 없는 동안은 카메라를 방향키로 움직여 맵을 볼 수 있다
            _gameOver = allDead;
        }

        private void OnReset()
        {
            _cleared = false;
            _gameOver = false;
            CancelNextStage();
            ExitEnterAnimation.Cancel();
            ClearScreen.Hide();
        }

        private void CancelNextStage()
        {
            if (_nextStageRoutine == null) return;
            StopCoroutine(_nextStageRoutine);
            _nextStageRoutine = null;
        }

        private IEnumerator GoToNextStage(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            _nextStageRoutine = null;
            SceneFader.LoadScene(NextSceneName());
        }

        // 클리어 기록을 남겨 타이틀의 스테이지 선택 해제와 게임 시작(이어서 하기)에 반영한다.
        // 다음 스테이지 씬이 없으면 마지막 스테이지 = 엔딩으로 본다.
        private void RecordClear()
        {
            string current = SceneManager.GetActiveScene().name;
            if (!StageScenes.TryGetStageNumber(current, out int stageNumber))
            {
                return;
            }

            bool isLastStage = !Application.CanStreamedLevelBeLoaded(StageScenes.GetSceneName(stageNumber + 1));
            SaveDataStore.RecordStageCleared(stageNumber, isLastStage);
        }

        // 마지막 숫자를 하나 올린 씬이 없으면 타이틀로 돌아간다.
        private string NextSceneName()
        {
            string current = SceneManager.GetActiveScene().name;
            var match = Regex.Match(current, @"(\d+)(?!.*\d)");
            if (match.Success && int.TryParse(match.Value, out int number))
            {
                string next = current.Substring(0, match.Index) + (number + 1) + current.Substring(match.Index + match.Length);
                if (Application.CanStreamedLevelBeLoaded(next)) return next;
                Debug.Log($"[GameManager] 다음 스테이지 씬 '{next}'이 Build Settings에 없어 {_titleSceneName}(으)로 돌아감");
            }
            return _titleSceneName;
        }
    }
}
