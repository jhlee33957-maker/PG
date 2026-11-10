using UnityEngine;
using CoffeeSpec.Rules;

namespace CoffeeSpec.Rules.Entities
{
    /// <summary>Move의 진행 방향을 유지하고 실제 이동이 막혔을 때 돌아선다.</summary>
    internal sealed class EntityMove
    {
        private bool _randomMove;
        private Vector2 _direction;
        // Move로 가는 쪽(1 오른쪽, -1 왼쪽, 0 아직 안 정함). 문장을 다시 적용해도 돌던 방향을 이어 가도록 꺼도 지우지 않는다
        private float _moveSign;
        // Move가 꺼진 프레임. 같은 프레임에 다시 켜지면(문장 재적용) 가던 방향을 잇고,
        // 그 밖에는 지금 보는 쪽으로 출발한다. 변신하고 다른 쪽을 보다가 다시 Move가 켜졌는데 옛 방향으로 뒤집히지 않게 한다
        private int _moveOffFrame = -1;
        // 지난 물리 프레임에 Move로 밀어 준 뒤의 가로 위치와 가야 했던 거리. 실제로 이만큼 못 갔으면 막힌 것이다
        private bool _hasMoveSample;
        private float _moveSampleX;
        private float _moveExpectedDx;
        // 가야 했던 거리의 이 비율보다 덜 갔으면 막혔다고 보고 돌아선다
        private const float StallRatio = 0.25f;

        private readonly RuleEntity _owner;
        private readonly Rigidbody2D _rigidbody;
        private readonly SpriteRenderer _renderer;

        internal EntityMove(RuleEntity owner, Rigidbody2D rigidbody, SpriteRenderer renderer)
        {
            _owner = owner;
            _rigidbody = rigidbody;
            _renderer = renderer;
        }

        internal bool IsRandomMoving => _randomMove;

        internal void SetRandomMove(bool on)
        {
            if (_randomMove == on) return;
            _randomMove = on;
            _hasMoveSample = false;
            if (!on)
            {
                _direction = Vector2.zero;
                _moveOffFrame = Time.frameCount;
                return;
            }
            // 새로 켜질 때는 보고 있는 쪽으로 간다
            if (_moveSign == 0f || _moveOffFrame != Time.frameCount) _moveSign = _renderer != null && _renderer.flipX ? -1f : 1f;
            _direction = new Vector2(_moveSign, 0f);
            FaceDirection(_moveSign);
        }

        // Move 방향을 반대로 바꾼다
        internal void TurnAround()
        {
            _moveSign = -_moveSign;
            _direction = new Vector2(_moveSign, 0f);
            FaceDirection(_moveSign);
        }

        // 가는 방향을 보게 한다. 멈춘 방향(0)이면 보던 쪽을 그대로 둔다
        private void FaceDirection(float x)
        {
            if (!_owner.FaceMoveDirection || _owner.IsControlled || x == 0f) return;
            _renderer.flipX = x < 0f;
        }

        // Move: 보는 쪽으로 쭉 가다가 무언가에 부딪히면 반대로 돌아선다
        internal void UpdateRandomMove()
        {
            // Move는 항상 좌우로만 움직인다. 중력을 받는 대상(나는 중 포함)은 속도로, 아닌 대상은 위치로 옮긴다
            bool usesGravity = (_rigidbody.gravityScale > 0f || _owner.Fly.IsFlying) && _rigidbody.bodyType == RigidbodyType2D.Dynamic;

            // 부딪힌 상대 쪽 접촉 방향으로 따지면 벽 블록 이음매나 모서리에서 놓치는 경우가 있어,
            // 지난 프레임에 밀어 준 만큼 실제로 갔는지로 막힘을 본다 (벽·생물 등 무엇에 막혀도 같다).
            // 통과 판정 몸은 충돌하지 않아 아래에서 고정물을 미리 쓸어 보고 돌아선다
            if (_hasMoveSample && !_owner.Motor.IsPassThrough)
            {
                float moved = (_rigidbody.position.x - _moveSampleX) * _moveSign;
                if (moved < _moveExpectedDx * StallRatio)
                {
                    TurnAround();
                }
            }
            _hasMoveSample = false;

            // 플레이어 입력이 우선이라 조작 중에는 속도를 건드리지 않는다
            if (_owner.InputOverride) return;

            if (usesGravity)
                _rigidbody.linearVelocity = new Vector2(_direction.x * _owner.MoveSpeed, _rigidbody.linearVelocity.y);
            else
            {
                Vector2 delta = _direction * (_owner.MoveSpeed * Time.fixedDeltaTime);
                if (_owner.Motor.IsPassThrough)
                {
                    // 통과 판정 몸은 충돌이 안 일어나서, 고정물에 막혀 덜 가게 되면 그때 돌아선다
                    float limitedX = _owner.Motor.LimitBySolid(Vector2.right, delta.x);
                    if (Mathf.Abs(limitedX) < Mathf.Abs(delta.x) - 0.00001f) TurnAround();
                    delta = new Vector2(limitedX, _owner.Motor.LimitBySolid(Vector2.up, delta.y));
                }
                _rigidbody.MovePosition(_rigidbody.position + delta);
            }
            _moveSampleX = _rigidbody.position.x;
            _moveExpectedDx = _owner.MoveSpeed * Time.fixedDeltaTime;
            _hasMoveSample = true;
        }
    }
}
