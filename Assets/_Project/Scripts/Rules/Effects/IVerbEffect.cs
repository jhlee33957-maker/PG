using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Rules.Effects
{
    /// <summary>동사 하나의 결과 적용과 조건 판정을 한곳에 모은다.</summary>
    public interface IVerbEffect
    {
        WordId Id { get; }
        bool AcceptsResult { get; }
        bool AllowsRemovedCondition { get; }
        bool RequiresSolidSubject { get; }
        bool ObservesTouch { get; }
        bool ControlsEntity { get; }
        bool IsGoal { get; }
        bool IsActive(RuleEntity entity, bool assigned);
        void Apply(RuleEntity entity, bool assigned, VerbEffectCollection effects);
    }

    /// <summary>특별한 조건이 없는 동사는 적용 함수만 구현하면 된다.</summary>
    public abstract class VerbEffect : IVerbEffect
    {
        public abstract WordId Id { get; }
        public virtual bool AcceptsResult => true;
        public virtual bool AllowsRemovedCondition => false;
        public virtual bool RequiresSolidSubject => false;
        public virtual bool ObservesTouch => false;
        public virtual bool ControlsEntity => false;
        public virtual bool IsGoal => false;
        public virtual bool IsActive(RuleEntity entity, bool assigned) => assigned;
        public virtual void Apply(RuleEntity entity, bool assigned, VerbEffectCollection effects) { }
    }
}
