using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Rules.Effects
{
    public sealed class MoveEffect : VerbEffect
    {
        public override WordId Id => WordId.Move;
        // 직접 입력 중이어도 움직임 조건은 참이지만 자동 이동은 켜지 않는다.
        public override bool IsActive(RuleEntity entity, bool assigned) => assigned || entity.IsInputMoving;
        public override void Apply(RuleEntity entity, bool assigned, VerbEffectCollection effects) => entity.SetRandomMove(assigned);
    }
}
