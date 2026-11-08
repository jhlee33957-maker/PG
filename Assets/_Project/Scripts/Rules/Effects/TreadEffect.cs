using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Rules.Effects
{
    public sealed class TreadEffect : VerbEffect
    {
        public override WordId Id => new("Tread");
        // 밟힘은 조건으로만 쓰고, 해당 조건의 주어는 밟을 수 있게 한다.
        public override bool AcceptsResult => false;
        public override bool RequiresSolidSubject => true;
        public override bool IsActive(RuleEntity entity, bool assigned) => entity.IsTreaded;
    }
}
