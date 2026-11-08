using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Rules.Effects
{
    public sealed class TouchEffect : VerbEffect
    {
        public override WordId Id => new("Touch");
        public override bool ObservesTouch => true;
        // 결과의 충돌 설정과 조건의 실제 접촉은 서로 다른 상태다.
        public override bool IsActive(RuleEntity entity, bool assigned) => entity.IsTouched;
        public override void Apply(RuleEntity entity, bool assigned, VerbEffectCollection effects) => entity.SetTouchable(assigned);
    }
}
