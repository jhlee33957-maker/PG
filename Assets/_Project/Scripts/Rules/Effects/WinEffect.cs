namespace CoffeeSpec.Rules.Effects
{
    public sealed class WinEffect : VerbEffect
    {
        public override WordId Id => new("Win");
        // 접촉 시 클리어 판정은 규칙 관리자가 이 표식을 조회한다.
        public override bool IsGoal => true;
    }
}
