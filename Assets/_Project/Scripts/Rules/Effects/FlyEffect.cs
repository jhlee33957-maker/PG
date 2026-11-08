using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Rules.Effects
{
    public sealed class FlyEffect : VerbEffect
    {
        public override WordId Id => new("Fly");
        public override void Apply(RuleEntity entity, bool assigned, VerbEffectCollection effects) => entity.SetFly(assigned);
    }
}
