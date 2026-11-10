using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Rules.Effects
{
    public sealed class DieEffect : VerbEffect
    {
        // 벽과 출구는 맵의 뼈대라서 사망 효과로 사라지면 스테이지를 깰 수 없게 된다. Die 문장을 받아도 죽지 않는다
        private static readonly WordId WallId = new("Wall");
        private static readonly WordId ExitId = new("Exit");

        public override WordId Id => new("Die");
        public override bool AllowsRemovedCondition => true;
        // 죽지 않는 대상은 효과를 받아도 "사망하면" 조건에 걸리지 않게 한다
        public override bool IsActive(RuleEntity entity, bool assigned) => entity.IsRemoved || (assigned && CanDie(entity) && !entity.IsInvincible);
        public override void Apply(RuleEntity entity, bool assigned, VerbEffectCollection effects)
        {
            if (!assigned || !CanDie(entity) || entity.IsInvincible) return; // 변신 직후 무적이면 죽지 않는다
            // 조종 해제 전에 죽었음을 알려 기존 사망 판정을 유지한다.
            effects.BeforeDeath(entity);
            entity.Remove();
        }

        private static bool CanDie(RuleEntity entity) => entity.Id != WallId && entity.Id != ExitId;
    }
}