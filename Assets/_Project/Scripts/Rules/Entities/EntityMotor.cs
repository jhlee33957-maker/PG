using UnityEngine;
using CoffeeSpec.Rules;

namespace CoffeeSpec.Rules.Entities
{
    /// <summary>바닥 위치 보정, 밀림 방지, 접촉면으로 미는 속도 정리, 통과 판정 몸의 고정물 앞 이동 제한을 담당한다.</summary>
    internal sealed class EntityMotor
    {
        private static readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];
        private static readonly RaycastHit2D[] _solidHits = new RaycastHit2D[8];
        // 통과 판정(트리거) 몸이 고정물 앞에서 멈출 때 남겨 두는 틈. 트리거는 물리 충돌이 없어서 이 방식으로만 막을 수 있다.
        // 부딪히는 몸(플레이어·생물 등)에는 쓰지 않는다. 그런 몸은 물리 엔진이 실제로 접촉시킨다
        private const float SolidSkin = 0.01f;
        // 접촉 법선이 이 값 이상으로 가로(또는 아래)를 향할 때만 벽면(천장면)으로 본다.
        // 바닥 타일 이음매 모서리에서 생기는 비스듬한 법선(약 0.7)은 건드리지 않는다
        private const float FaceNormal = 0.9f;
        private static readonly ContactPoint2D[] _contactPoints = new ContactPoint2D[16];
        // 가로 이동 중 이 높이 안쪽으로 발밑보다 낮거나 살짝 높은 윗면은 바닥 이음매로 보고 막지 않는다
        private const float FloorSeamStep = 0.05f;
        private static PhysicsMaterial2D _noFriction;
        private static readonly System.Collections.Generic.List<Collider2D> _overlaps = new();
        // 겹친 상대와 가로로 내 폭의 이 비율보다 덜 겹치면 옆에 붙은 벽으로 보고 위로 빼내지 않는다
        private const float SideOverlapRatio = 0.25f;
        // 바닥으로 칠 접점은 내 좌우 끝에서 이만큼 안쪽이어야 한다
        private const float GroundEdgeMargin = 0.05f;
        private bool _wasGrounded;
        private float _snapBlockTimer;
        // 서로 밀지 못하게: 스스로 움직이지 않는 동안은 가로 위치를 잠가서 부딪혀도 밀리지 않는다
        private const float StopSpeed = 0.05f;
        private bool _baseFreezeX;    // 프리팹(지금 모습) 설정에 원래 가로 고정이 있으면 항상 잠근다
        private bool _selfDriven;     // 플레이어 조작·대쉬 중 (플레이어가 알려 준다)
        private bool _keepMomentum;   // 스스로 움직이다 멈추는 중. 속도가 거의 0이 될 때까지는 잠그지 않는다

        private readonly RuleEntity _owner;
        private readonly Rigidbody2D _rigidbody;
        private readonly Collider2D _collider;

        internal EntityMotor(RuleEntity owner, Rigidbody2D rigidbody, Collider2D collider)
        {
            _owner = owner;
            _rigidbody = rigidbody;
            _collider = collider;
        }

        internal static RaycastHit2D[] GroundHits => _groundHits;

        internal bool BaseFreezeX { get => _baseFreezeX; set => _baseFreezeX = value; }
        internal bool WasGrounded { get => _wasGrounded; set => _wasGrounded = value; }

        /// <summary>
        /// 아래로 쏜 결과가 밟을 수 있는 바닥인지. 벽을 1칸 블록으로 쌓으면 옆에 붙었을 때 블록 이음매의 윗면이
        /// 내 좌우 끝에 걸려 바닥처럼 잡히므로, 접점이 내 몸 안쪽에 있을 때만 바닥으로 본다 (벽 타고 오르기 방지).
        /// </summary>
        internal static bool IsGroundHit(RaycastHit2D hit, Collider2D mine)
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.normal.y <= 0.5f) return false;
            Bounds bounds = mine.bounds;
            return hit.point.x > bounds.min.x + GroundEdgeMargin && hit.point.x < bounds.max.x - GroundEdgeMargin;
        }

        // 타일마다 콜라이더가 따로 있으면 각진 박스가 이음매 모서리에 걸려 가로 이동이 막힌다.
        // 겉 크기(fullSize)는 그대로 두고 모서리만 둥글게 해서 이음매를 타고 넘게 한다
        internal void RoundCorners(BoxCollider2D box, Vector2 fullSize)
        {
            float r = Mathf.Min(_owner.ColliderCornerRadius, fullSize.x * 0.5f, fullSize.y * 0.5f);
            box.edgeRadius = r;
            box.size = fullSize - new Vector2(r * 2f, r * 2f);
        }

        // 마찰을 없앤다. 벽 쪽으로 미는 힘(수직 항력)에 마찰이 곱해지면 그만큼 세로 속도가 깎여서
        // 벽에 붙은 채 점프가 먹히거나 공중에 걸린다. 재질을 직접 넣어 둔 경우는 그대로 둔다.
        // 합치는 방식을 Minimum으로 둬서, 상대 콜라이더가 기본 재질(마찰 0.4, Mean)이어도 접촉 마찰은 0이 된다
        // (Unity는 두 재질의 합치는 방식 중 우선순위가 높은 쪽을 쓴다: Maximum > Minimum > Multiply > Mean > Average).
        internal void ApplyNoFriction()
        {
            if (_noFriction == null)
                _noFriction = new PhysicsMaterial2D("NoFriction")
                {
                    friction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine2D.Minimum,
                };
            if (_rigidbody.sharedMaterial == null) _rigidbody.sharedMaterial = _noFriction;
            if (_collider != null && _collider.sharedMaterial == null) _collider.sharedMaterial = _noFriction;
        }

        // 착지할 때 타일 속으로 들어간 만큼 바로 위로 빼낸다. 물리 엔진은 여러 프레임에 걸쳐 천천히 밀어내서
        // 박혀 있다가 걸으면서 풀리는 것처럼 보인다
        private void PushOutOfGround()
        {
            var filter = ContactFilter2D.noFilter;
            filter.useTriggers = false;
            int n = _collider.Overlap(filter, _overlaps);
            float up = 0f;
            Bounds mine = _collider.bounds;
            for (int i = 0; i < n; i++)
            {
                var other = _overlaps[i];
                if (other.isTrigger || other.attachedRigidbody == _rigidbody) continue;
                // 옆으로 살짝 파고든 벽은 바닥이 아니다. 벽을 1칸 블록으로 쌓으면 블록 이음매의 윗면이 발밑에 걸려
                // 바닥으로 잘못 보고 매 프레임 위로 빼내서, 공중에서 벽에 붙은 채 떠 있게 된다
                Bounds theirs = other.bounds;
                float overlapX = Mathf.Min(mine.max.x, theirs.max.x) - Mathf.Max(mine.min.x, theirs.min.x);
                if (overlapX < mine.size.x * SideOverlapRatio) continue;
                var d = _collider.Distance(other);
                if (!d.isOverlapped) continue;
                // 겹쳤을 때 pointA(내 표면)에서 pointB(상대 표면)로 가는 만큼 움직이면 빠져나온다. 위로 빼야 하는 경우만 처리한다
                Vector2 separation = d.pointB - d.pointA;
                if (separation.y > Mathf.Abs(separation.x) && separation.y > up) up = separation.y;
            }
            // 바닥에 서 있으면 원래 아주 살짝(contact offset) 겹쳐 있다. 그것까지 빼면 엔진과 싸워 떨리므로
            // 눈에 보일 만큼(1픽셀 이상) 박혔을 때만 빼고, 엔진이 쓰는 간격은 남긴다
            float slop = Physics2D.defaultContactOffset;
            if (up <= _owner.SinkTolerance) return;
            _rigidbody.position += Vector2.up * (up - slop);
            var v = _rigidbody.linearVelocity;
            if (v.y < 0f) _rigidbody.linearVelocity = new Vector2(v.x, 0f);
        }

        /// <summary>점프처럼 일부러 땅에서 떨어질 때 부른다. 그동안은 바닥에 붙이지 않는다.</summary>
        internal void BlockGroundSnap(float seconds)
        {
            _snapBlockTimer = Mathf.Max(_snapBlockTimer, seconds);
            _wasGrounded = false;
        }

        // 직전에 땅에 있었는데 발밑 조금 아래에 바닥이 있으면 이음매를 넘다 뜬 것이므로 바닥에 다시 붙인다.
        // 공중에서 내려오는 중이면 붙이지 않고 자연스럽게 착지하게 둔다
        internal void SnapToGround()
        {
            _snapBlockTimer -= Time.fixedDeltaTime;
            bool usesGravity = _rigidbody.gravityScale > 0f && _rigidbody.bodyType == RigidbodyType2D.Dynamic;
            if (!usesGravity || _collider == null)
            {
                _wasGrounded = false;
                return;
            }

            PushOutOfGround();

            float gap = float.MaxValue;
            int count = _collider.Cast(Vector2.down, _groundHits, _owner.GroundSnapDistance);
            for (int i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (!IsGroundHit(hit, _collider)) continue;
                gap = Mathf.Min(gap, hit.distance);
            }

            bool nearGround = gap <= _owner.GroundSnapDistance;
            if (nearGround && _wasGrounded && _snapBlockTimer <= 0f)
            {
                // 물리 엔진이 원래 띄워 두는 간격(contact offset)까지 누르면 다시 밀려 올라와 떨리므로 그만큼은 남긴다
                float keep = Physics2D.defaultContactOffset * 2f;
                if (gap > keep) _rigidbody.position += Vector2.down * (gap - keep);
                var v = _rigidbody.linearVelocity;
                if (v.y > 0f) _rigidbody.linearVelocity = new Vector2(v.x, 0f);
                _wasGrounded = true;
            }
            else
            {
                // 내려오다 닿은 경우는 거의 붙었을 때만 땅으로 본다
                _wasGrounded = nearGround && gap <= Physics2D.defaultContactOffset * 3f && _snapBlockTimer <= 0f;
            }
        }

        /// <summary>플레이어가 직접 움직이는 중인지 알려 준다(조작·대쉬). 이때만 가로 잠금이 풀린다.</summary>
        internal void SetSelfDriven(bool driven)
        {
            _selfDriven = driven;
            UpdatePushLock();
        }

        // 스스로 움직일 때(조작, 대쉬, Move 규칙)와 그 움직임이 멈출 때까지만 가로로 움직일 수 있다.
        // 그 밖에는 가로 위치를 잠가서 누가 부딪혀도 밀리지 않는다. 세로(중력, 날기)는 그대로다
        internal void UpdatePushLock()
        {
            if (_owner.IsRemoved || _rigidbody.bodyType != RigidbodyType2D.Dynamic) return;
            bool driven = _selfDriven || _owner.Move.IsRandomMoving;
            if (driven) _keepMomentum = true;
            else if (_keepMomentum && Mathf.Abs(_rigidbody.linearVelocity.x) < StopSpeed) _keepMomentum = false;

            bool lockX = _baseFreezeX || !(driven || _keepMomentum);
            var current = _rigidbody.constraints;
            var next = lockX
                ? current | RigidbodyConstraints2D.FreezePositionX
                : current & ~RigidbodyConstraints2D.FreezePositionX;
            if (next == current) return;
            // 벽에 파고든 채로 가로를 잠그면 엔진이 위아래로만 밀어낼 수 있어서, 쌓인 벽 블록 이음매에 받쳐져 공중에 걸린다.
            // 잠그는 순간에만 파고든 만큼 가로로 되돌린다 (닿아 있는 상태는 유지한다)
            if (lockX) PushOutOfWallSides();
            _rigidbody.constraints = next;
        }

        // 고정물에 옆으로 파고든 만큼만 가로로 되돌린다. 위아래로 빼야 하는 겹침(바닥)은 건드리지 않는다.
        // 틈을 벌리지 않고 물리 엔진이 원래 허용하는 아주 작은 겹침(접촉 여유의 1/4)만큼은 남겨서, 닿아 있던 상대와의 접촉(충돌 판정)이 끊기지 않게 한다
        private void PushOutOfWallSides()
        {
            if (_collider == null || _collider.isTrigger) return;
            var filter = ContactFilter2D.noFilter;
            filter.useTriggers = false;
            int n = _collider.Overlap(filter, _overlaps);
            float push = 0f;
            for (int i = 0; i < n; i++)
            {
                var other = _overlaps[i];
                if (other.isTrigger || other.attachedRigidbody == _rigidbody) continue;
                var body = other.attachedRigidbody;
                if (body != null && body.bodyType != RigidbodyType2D.Static) continue;
                var d = _collider.Distance(other);
                if (!d.isOverlapped) continue;
                Vector2 separation = d.pointB - d.pointA;
                if (Mathf.Abs(separation.x) <= Mathf.Abs(separation.y)) continue;
                if (Mathf.Abs(separation.x) > Mathf.Abs(push)) push = separation.x;
            }
            float keep = 0.0025f; // Box2D linearSlop(0.005)의 절반
            if (Mathf.Abs(push) <= keep) return;
            _rigidbody.position += new Vector2(push - Mathf.Sign(push) * keep, 0f);
        }

        // 통과 판정(트리거) 몸인지. 트리거는 물리 충돌을 하지 않아서 그냥 두면 벽과 바닥을 뚫는다
        internal bool IsPassThrough => !_owner.IsRemoved && _collider != null && _collider.isTrigger;

        /// <summary>
        /// 스스로 넣은 속도를 막힌 면에 맞게 정리한다. 속도를 넣은 뒤에 부른다.
        /// 부딪히는 몸: 미리 멈추지 않는다. 물리 엔진이 실제로 접촉시키고(충돌 판정이 그대로 난다),
        /// 이미 닿아 있는 벽·천장 쪽으로 미는 속도만 뺀다. 대상 종류와 관계없이 모든 접촉에 똑같이 적용한다.
        /// 통과 판정(트리거) 몸: 물리 충돌이 없어서 벽·바닥을 뚫으므로, 갈 거리만큼 미리 쓸어 보고 고정물 앞까지만 가게 한다.
        /// </summary>
        internal void BlockBySolids()
        {
            if (_rigidbody.bodyType != RigidbodyType2D.Dynamic) return;
            if (!IsPassThrough)
            {
                CancelVelocityIntoContacts();
                return;
            }
            float dt = Time.fixedDeltaTime;
            Vector2 v = _rigidbody.linearVelocity;
            // 중력은 이번 프레임 속도에 더해지므로 아래로 갈 거리에 미리 넣어 둔다
            float vy = v.y + Physics2D.gravity.y * _rigidbody.gravityScale * dt;
            float limitedX = LimitBySolid(Vector2.right, v.x * dt) / dt;
            // 통과 판정 몸은 충돌이 안 일어나서, Move 중 가로로 막히면 여기서 돌아선다
            if (_owner.Move.IsRandomMoving && !_owner.InputOverride && Mathf.Abs(limitedX) < Mathf.Abs(v.x) - 0.0001f) _owner.Move.TurnAround();
            float limitedY = LimitBySolid(Vector2.up, vy * dt);
            bool blockedY = Mathf.Abs(limitedY) < Mathf.Abs(vy * dt) - 0.0001f;
            // 막혔으면 이번 프레임 중력이 더해진 뒤에 딱 막힌 곳까지만 가도록 중력만큼 미리 빼 둔다 (물리 엔진은 속도에 중력을 더한 뒤 움직인다)
            float nextVy = blockedY ? limitedY / dt - Physics2D.gravity.y * _rigidbody.gravityScale * dt : v.y;
            _rigidbody.linearVelocity = new Vector2(limitedX, blockedY ? nextVy : v.y);
        }

        // 물리 엔진이 지금 실제로 접촉시켜 둔 면(= 이미 OnCollisionEnter2D가 난 상대) 쪽으로 미는 속도 성분만 뺀다.
        // 마찰이 0이어도 매 프레임 벽 쪽으로 속도를 넣으면 그만큼 엔진이 되밀어야 해서 몸이 면으로 파고들 여지가 생긴다.
        // 실제 접촉 법선과의 내적으로 '면 안쪽으로 가는 성분'만 빼므로, 면을 따라 미끄러지는 성분(벽을 따라 위아래 등)은 그대로다.
        // 바닥 쪽 접촉은 중력·바닥 붙이기가 맡으므로 건드리지 않는다. 천장 쪽은 접점이 내 몸 위쪽 절반에 있을 때만 천장으로 본다
        // (몸 옆이나 아래 모서리에서 생긴 접점은 천장이 아니다). 위치는 옮기지 않아 접촉은 유지되고, 떨어질 때 엔진이 Exit를 보낸다.
        private void CancelVelocityIntoContacts()
        {
            if (_collider == null || _collider.isTrigger || !_collider.enabled) return;
            var filter = ContactFilter2D.noFilter;
            filter.useTriggers = false;
            int n = _rigidbody.GetContacts(filter, _contactPoints);
            if (n <= 0) return;
            Vector2 v = _rigidbody.linearVelocity;
            Bounds bounds = _collider.bounds;
            Vector2 center = bounds.center;
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                var contact = _contactPoints[i];
                if (!contact.enabled) continue;
                // 법선 부호 규칙에 기대지 않고, 접점에서 내 몸 쪽을 향하게 맞춘다 (닿은 면이 나를 밀어내는 방향)
                Vector2 normal = contact.normal;
                if (Vector2.Dot(normal, center - contact.point) < 0f) normal = -normal;
                bool wall = Mathf.Abs(normal.x) >= FaceNormal;
                bool ceiling = normal.y <= -FaceNormal && contact.point.y > center.y;
                if (!wall && !ceiling) continue;
                float into = Vector2.Dot(v, normal);
                if (into >= 0f) continue;
                v -= normal * into;
                changed = true;
            }
            if (changed) _rigidbody.linearVelocity = v;
        }

        private bool IsStaticSolid(Collider2D other)
        {
            if (other == null || other.isTrigger) return false;
            var body = other.attachedRigidbody;
            if (body == _rigidbody) return false;
            // 움직이는 몸(플레이어, 생물, 나는 벽 등)은 고정물이 아니라 통과한다
            if (body != null && body.bodyType != RigidbodyType2D.Static) return false;
            return true;
        }

        private bool IsBlockingSolidHit(RaycastHit2D hit, Vector2 dir, bool horizontalSeams)
        {
            var other = hit.collider;
            if (!IsStaticSolid(other)) return false;
            // 가는 방향을 마주 보는 면만 막는다. 발밑 바닥처럼 이미 닿아 있는 면에서 옆이나 위로 갈 때는 막지 않는다
            if (Vector2.Dot(hit.normal, dir) > -0.5f) return false;
            if (!horizontalSeams) return true;
            // 가로로 갈 때 윗면이 내 발높이 근처인 고정물은 벽이 아니라 옆 바닥 타일이다. 몸이 바닥에 살짝 박혀 있으면
            // 다음 타일의 옆면이 앞을 막는 면으로 잡혀 이음매에서 멈추므로 막지 않는다
            if (other.bounds.max.y <= _collider.bounds.min.y + FloorSeamStep) return false;
            // 천장도 같다. 날면서 천장에 붙어 있으면 몸이 천장에 살짝 박혀서 옆 천장 타일의 옆면이 앞을 막는다
            if (other.bounds.min.y >= _collider.bounds.max.y - FloorSeamStep) return false;
            return true;
        }

        // axis 방향(부호는 move)으로 move만큼 갈 때 고정물에 막히면 그 앞까지의 거리로 줄인다
        internal float LimitBySolid(Vector2 axis, float move)
        {
            float distance = Mathf.Abs(move);
            if (distance < 0.00001f) return move;
            Vector2 dir = axis * Mathf.Sign(move);

            var filter = ContactFilter2D.noFilter;
            filter.useTriggers = false;
            int count = _collider.Cast(dir, filter, _solidHits, distance + SolidSkin, true);
            float allowed = distance;
            for (int i = 0; i < count; i++)
            {
                var hit = _solidHits[i];
                if (!IsBlockingSolidHit(hit, dir, axis.y == 0f)) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - SolidSkin));
            }
            return Mathf.Sign(move) * allowed;
        }
    }
}
