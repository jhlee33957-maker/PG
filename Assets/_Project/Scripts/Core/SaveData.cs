namespace CoffeeSpec.Core
{
    /// <summary>
    /// 세이브 파일 한 개의 내용. 스테이지 번호는 1부터 시작한다.
    /// 값의 의미와 해제 규칙은 docs/decisions/003-save-data-and-stage-unlock.md 참고.
    /// </summary>
    public sealed class SaveData
    {
        /// <summary>
        /// 마지막으로 플레이한 스테이지 번호 (1부터).
        /// 0이면 진행 중인 판이 없다는 뜻이다 (엔딩을 본 직후 세이브 쪽에서 0으로 기록).
        /// </summary>
        public int LastPlayedStage { get; }

        /// <summary>진행 중인 판이 있으면 true (게임 시작 시 이어서 할 스테이지가 있음).</summary>
        public bool HasRunInProgress => LastPlayedStage >= 1;

        /// <summary>클리어한 가장 높은 스테이지 번호. 0이면 아직 클리어한 스테이지가 없다.</summary>
        public int HighestClearedStage { get; }

        public SaveData(int lastPlayedStage, int highestClearedStage)
        {
            LastPlayedStage = lastPlayedStage;
            HighestClearedStage = highestClearedStage;
        }
    }
}
