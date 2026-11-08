using UnityEngine;
using CoffeeSpec.Rules;

namespace CoffeeSpec.Rules.Entities
{
    /// <summary>날기 상태와 목표 높이를 관리하고 둥실거리는 속도를 계산한다.</summary>
    internal sealed class EntityFly
    {
        private bool _flying;
        private float _flyBaseY;      // 둥실거림의 가운데 높이 (Rigidbody 위치 기준)
        private float _bobPhase;      // 대상마다 다르게 둥실거리도록 하는 시작 박자
        private float _flyTime;
        private int _flyOffFrame = -1; // 날기가 꺼진 프레임. 같은 프레임에 다시 켜지면 높이를 새로 고르지 않는다

        private readonly RuleEntity _owner;
        private readonly Rigidbody2D _rigidbody;
        private readonly Collider2D _collider;

        internal EntityFly(RuleEntity owner, Rigidbody2D rigidbody, Collider2D collider)
        {
            _owner = owner;
            _rigidbody = rigidbody;
            _collider = collider;
        }

        internal bool IsFlying => _flying;

        /// <summary>
        /// 난다: 중력을 끄고 발밑 바닥에서 랜덤 높이까지 떠올라 위아래로 둥실거린다. 끄면 중력이 돌아와 떨어진다.
        /// Static 대상(벽, 출구)은 나는 동안만 Dynamic으로 풀고, 날기가 꺼지면 그 자리에서 다시 Static으로 고정된다.
        /// </summary>
        internal void SetFly(bool on)
        {
            if (_flying == on) return;

            _flying = on;
            if (!on)
            {
                _owner.ApplyGravity();
                _owner.ApplyRotationLock();
                _flyOffFrame = Time.frameCount;
                _owner.ApplyBodyType();
                return;
            }

            _owner.ApplyBodyType();
            _owner.ApplyRotationLock();
            _owner.ApplyGravity();
            _owner.Motor.WasGrounded = false;
            // 문장을 다시 적용할 때는 같은 프레임에 껐다 켜진다. 이때는 높이를 새로 고르지 않고 하던 대로 둥실거린다
            if (_flyOffFrame == Time.frameCount) return;

            float height = Random.Range(Mathf.Min(_owner.FlyHeightRange.x, _owner.FlyHeightRange.y), Mathf.Max(_owner.FlyHeightRange.x, _owner.FlyHeightRange.y));
            _flyBaseY = _rigidbody.position.y - DistanceToGround() + height;
            _bobPhase = Random.Range(0f, Mathf.PI * 2f);
            _flyTime = 0f;
        }

        // 발밑 바닥까지의 거리. 바닥이 없으면 0 (지금 위치를 바닥으로 본다)
        private float DistanceToGround()
        {
            if (_collider == null) return 0f;
            float best = float.MaxValue;
            int count = _collider.Cast(Vector2.down, EntityMotor.GroundHits, _owner.FlyGroundSearchDistance);
            for (int i = 0; i < count; i++)
            {
                var hit = EntityMotor.GroundHits[i];
                if (!EntityMotor.IsGroundHit(hit, _collider)) continue;
                best = Mathf.Min(best, hit.distance);
            }
            return best == float.MaxValue ? 0f : best;
        }

        // 목표 높이(가운데 높이 + 사인 둥실거림)를 세로 속도로 따라간다. 속도로 움직여서 천장에 닿으면 막힌다
        internal void UpdateFly()
        {
            _flyTime += Time.fixedDeltaTime;
            float targetY = _flyBaseY + Mathf.Sin(_flyTime * Mathf.PI * 2f / _owner.BobPeriod + _bobPhase) * _owner.BobAmplitude;
            float vy = Mathf.Clamp((targetY - _rigidbody.position.y) * _owner.FlyFollowStrength, -_owner.FlyMaxSpeed, _owner.FlyMaxSpeed);
            _rigidbody.linearVelocity = new Vector2(_rigidbody.linearVelocity.x, vy);
        }
    }
}
