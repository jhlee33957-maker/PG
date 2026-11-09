using System.Globalization;
using System.Text.RegularExpressions;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// 스테이지 번호와 씬 이름(Stage1Scene, Stage2Scene ...)을 서로 바꾼다.
    /// 타이틀(스테이지 선택)과 GameManager(클리어 기록)가 같은 규칙을 쓰도록 한 곳에 둔다.
    /// </summary>
    public static class StageScenes
    {
        private static readonly Regex StageScenePattern = new Regex(@"^Stage(\d+)Scene$", RegexOptions.Compiled);

        public static string GetSceneName(int stageNumber) => $"Stage{stageNumber}Scene";

        /// <summary>스테이지 씬이면 번호를 돌려준다. Title, Intro 같은 씬이면 false.</summary>
        public static bool TryGetStageNumber(string sceneName, out int stageNumber)
        {
            stageNumber = 0;
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            Match match = StageScenePattern.Match(sceneName);
            return match.Success
                && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out stageNumber)
                && stageNumber >= 1;
        }
    }
}