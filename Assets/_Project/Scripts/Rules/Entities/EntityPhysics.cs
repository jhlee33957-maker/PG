using UnityEngine;

namespace CoffeeSpec.Rules.Entities
{
    /// <summary>물리 부품의 소유와 실행 순서를 한곳에서 관리한다.</summary>
    internal sealed class EntityPhysics
    {
        internal EntityMotor Motor { get; }
        internal EntityFly Fly { get; }
        internal EntityMove Move { get; }

        internal EntityPhysics(RuleEntity owner, Rigidbody2D rigidbody, Collider2D collider, SpriteRenderer renderer)
        {
            Motor = new EntityMotor(owner, rigidbody, collider);
            Fly = new EntityFly(owner, rigidbody, collider);
            Move = new EntityMove(owner, rigidbody, renderer);
        }

        internal void Step()
        {
            Motor.UpdatePushLock();
            Motor.SnapToGround();
            if (Fly.IsFlying) Fly.UpdateFly();
            if (Move.IsRandomMoving) Move.UpdateRandomMove();
            // 물리 순서 통합 뒤 걸림이 재발해 기존 Unity 콜백을 복원한다. 플레이어도 자기 속도를 넣은 뒤 다시 제한한다.
            Motor.BlockBySolids();
        }
    }
}
