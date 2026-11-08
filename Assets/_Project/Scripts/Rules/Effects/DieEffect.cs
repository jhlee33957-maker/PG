using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Rules.Effects
{
    public sealed class DieEffect : VerbEffect
    {
        public override WordId Id => new("Die");
        public override bool AllowsRemovedCondition => true;
        public override bool IsActive(RuleEntity entity, bool assigned) => entity.IsRemoved || assigned;
        public override void Apply(RuleEntity entity, bool assigned, VerbEffectCollection effects)
        {
            if (!assigned) return;
            // 조종 해제 전에 죽었음을 알려 기존 사망 판정을 유지한다.
            effects.BeforeDeath(entity);
            entity.Remove();
        }
    }
}
