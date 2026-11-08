using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Rules.Effects
{
    public sealed class ControlEffect : VerbEffect
    {
        public override WordId Id => new("Control");
        public override bool ControlsEntity => true;
        public override void Apply(RuleEntity entity, bool assigned, VerbEffectCollection effects) => entity.SetControl(assigned);
    }
}
