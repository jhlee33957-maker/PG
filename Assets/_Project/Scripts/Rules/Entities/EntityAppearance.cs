using System.Collections.Generic;
using UnityEngine;
using CoffeeSpec.Rules;

namespace CoffeeSpec.Rules.Entities
{
    /// <summary>배치 당시의 모습을 보관하고 변신할 때 그림과 판정 설정을 복사한다.</summary>
    internal sealed class EntityAppearance
    {
        private Sprite _originalSprite;
        private SpriteDrawMode _originalDrawMode;
        private Vector2 _originalSize;
        private Vector3 _originalScale;
        // 변신 대상에게 넘겨줄 배치 당시의 판정 설정 (출구는 통과되는 트리거, 비둘기는 막히는 몸 등)
        private bool _originalIsTrigger;
        private RigidbodyConstraints2D _originalConstraints;
        private RigidbodyType2D _originalBodyType;
        private bool _originalRemovedByAttack;

        private readonly RuleEntity _owner;
        private readonly Rigidbody2D _rigidbody;
        private readonly Collider2D _collider;
        private readonly SpriteRenderer _renderer;

        internal EntityAppearance(RuleEntity owner, Rigidbody2D rigidbody, Collider2D collider, SpriteRenderer renderer)
        {
            _owner = owner;
            _rigidbody = rigidbody;
            _collider = collider;
            _renderer = renderer;
        }

        internal RigidbodyConstraints2D OriginalConstraints { get => _originalConstraints; set => _originalConstraints = value; }
        internal RigidbodyType2D OriginalBodyType { get => _originalBodyType; set => _originalBodyType = value; }
        internal Vector3 OriginalScale { get => _originalScale; set => _originalScale = value; }
        internal Vector2 OriginalSize { get => _originalSize; set => _originalSize = value; }
        internal SpriteDrawMode OriginalDrawMode { get => _originalDrawMode; set => _originalDrawMode = value; }
        internal bool OriginalRemovedByAttack { get => _originalRemovedByAttack; set => _originalRemovedByAttack = value; }
        internal bool OriginalIsTrigger { get => _originalIsTrigger; set => _originalIsTrigger = value; }
        internal Sprite OriginalSprite { get => _originalSprite; set => _originalSprite = value; }

        internal void SetSprite(Sprite sprite) { if (sprite != null) _renderer.sprite = sprite; }

        /// <summary>
        /// 다른 객체의 원래 모습으로 변신한다. 스프라이트만 바꾸면 그리는 크기가 그대로라 찌그러지므로
        /// 그리는 방식, 크기, 스케일까지 복사하고 콜라이더도 새 모습에 맞춘다.
        /// 판정도 새 모습을 따라간다: 바디 타입(Static/Dynamic), 트리거 여부, 중력, 회전/이동 고정을 원래 그 객체의 설정으로 바꾼다.
        /// </summary>
        internal void TransformInto(WordId id, RuleEntity source)
        {
            if (source == null) return;
            // 방금 변신했으면 잠깐은 다시 바뀌지 않는다
            if (!_owner.CanTransform) return;
            // 사망한 대상이 변신하면 새 모습으로 다시 살아난다 (사망 상태는 넘어가지 않는다)
            bool revived = _owner.IsRemoved;
            if (revived) _owner.Revive();
            _owner.Word = id;
            // 무적은 살아난 직후 같은 가시에 또 죽지 않게 하려는 것이다. 살아 있는 채로 번갈아 변신할 때까지 주면 계속 무적이 되어 죽지 않는다
            if (revived) _owner.StartTransformInvincible();
            RuleManager.Trace($"TransformInto {_owner.name} -> {id} revived={revived} pos={_owner.transform.position}"); // TEMP TRACE
            _owner.StartTransformLock();

            // 발 위치를 유지해야 커진 콜라이더가 바닥에 박히지 않는다
            float oldBottom = _collider != null ? _collider.bounds.min.y : _owner.transform.position.y;
            // 발을 맞춘 채 키가 커지면 머리가 위로 자란다. 천장 바로 아래에서 그러면 머리가 천장에 깊이 박히고,
            // 물리 엔진이 덜 박힌 쪽(천장 위)으로 밀어내서 천장을 뚫고 올라간다. 바뀌기 전에 머리 위 여유를 재 둔다
            float oldTop = _collider != null ? _collider.bounds.max.y : 0f;
            float headroom = _collider != null ? _owner.Motor.LimitBySolid(Vector2.up, 10f) : 0f;

            _renderer.sprite = source.Appearance.OriginalSprite;
            _renderer.drawMode = source.Appearance.OriginalDrawMode;
            if (source.Appearance.OriginalDrawMode != SpriteDrawMode.Simple) _renderer.size = source.Appearance.OriginalSize;
            _owner.transform.localScale = new Vector3(
                Mathf.Abs(source.Appearance.OriginalScale.x) * Mathf.Sign(_owner.transform.localScale.x),
                source.Appearance.OriginalScale.y, source.Appearance.OriginalScale.z);

            FitColliderToSprite();
            CopyPhysicsFrom(source);
            _owner.ApplyBodyType();

            if (_collider != null)
            {
                Physics2D.SyncTransforms();
                float diff = oldBottom - _collider.bounds.min.y;
                // 머리가 천장을 넘으면 넘는 만큼 아래로 내린다 (천장 바로 아래에 머리를 맞춘다)
                float rise = _collider.bounds.max.y + diff - oldTop;
                if (rise > headroom) diff -= rise - headroom;
                _rigidbody.position += new Vector2(0f, diff);
                _owner.transform.position += new Vector3(0f, diff, 0f);
                Physics2D.SyncTransforms();
                LiftRiders(oldTop);
            }
        }

        // 위에 올라탄 것으로 치는 높이 오차. 밟는 순간 살짝 떠 있거나 박혀 있어도 올라탄 것으로 본다
        private const float RiderTolerance = 0.1f;

        /// <summary>
        /// 변신 전에 내 윗면(oldTop) 높이에 발이 있던 대상 중, 바뀐 콜라이더 폭 안에 있는 것을 새 윗면 위에 세운다.
        /// 예전 모습보다 넓어지면 가장자리에 있던 대상도 떨어지지 않고, 높아지면 발이 새 모습에 박혀 옆이나 아래로 밀려나지 않는다.
        /// 어떤 대상이 무엇으로 변신하든 같은 규칙이 적용되도록 콜라이더 크기만 본다
        /// </summary>
        private void LiftRiders(float oldTop)
        {
            if (_collider == null || _collider.isTrigger || !_collider.enabled) return;
            Bounds b = _collider.bounds;
            float newTop = b.max.y;
            // 낮아졌으면 위에 있던 대상은 중력으로 새 윗면에 내려앉으므로 따로 할 일이 없다
            if (newTop <= oldTop - RiderTolerance) return;

            float low = oldTop - RiderTolerance, high = newTop + RiderTolerance;
            var area = new Vector2(b.size.x, high - low);
            var center = new Vector2(b.center.x, (low + high) * 0.5f);
            var lifted = new HashSet<Rigidbody2D>();
            foreach (var hit in Physics2D.OverlapBoxAll(center, area, 0f))
            {
                if (hit == null || hit.isTrigger || hit == _collider) continue;
                var body = hit.attachedRigidbody;
                if (body == null || body == _rigidbody || body.bodyType == RigidbodyType2D.Static || !lifted.Add(body)) continue;
                Bounds o = hit.bounds;
                // 발이 예전 윗면 근처에 있던 대상만 올린다. 옆에 붙어 있던 대상(발이 더 아래)은 그대로 둔다
                if (o.min.y < low || o.min.y >= high) continue;
                // 바뀐 폭 바깥이면 원래대로 떨어진다
                if (o.max.x <= b.min.x || o.min.x >= b.max.x) continue;
                float lift = newTop - o.min.y;
                if (lift <= 0f) continue;
                body.position += new Vector2(0f, lift);
                body.transform.position += new Vector3(0f, lift, 0f);
                if (body.linearVelocity.y < 0f) body.linearVelocity = new Vector2(body.linearVelocity.x, 0f);
            }
            if (lifted.Count > 0) Physics2D.SyncTransforms();
        }

        private void CopyPhysicsFrom(RuleEntity source)
        {
            _owner.Contacts.BaseIsTrigger = source.Appearance.OriginalIsTrigger;
            _owner.Contacts.ApplyTrigger();
            _owner.BodyType = source.Appearance.OriginalBodyType;
            _owner.DefaultGravity = source.DefaultGravity;
            // 공격에 사라지는 성질도 새 모습을 따라간다
            _owner.RemovedByAttack = source.Appearance.OriginalRemovedByAttack;
            // 나는 중이거나 조종받는 중이면 중력과 회전 고정은 그 상태에 맞춰 두고, 끝날 때 새 설정으로 돌아간다
            _rigidbody.constraints = source.Appearance.OriginalConstraints;
            _owner.BaseFreezeRotation = (source.Appearance.OriginalConstraints & RigidbodyConstraints2D.FreezeRotation) != 0;
            _owner.ApplyGravity();
            _owner.ApplyRotationLock();
            _owner.Motor.BaseFreezeX = (source.Appearance.OriginalConstraints & RigidbodyConstraints2D.FreezePositionX) != 0;
            _owner.Motor.UpdatePushLock();
        }

        // 판정이 그림과 어긋나지 않도록 콜라이더를 지금 그려지는 영역(로컬 기준)에 맞춘다
        private void FitColliderToSprite()
        {
            if (_collider == null || _renderer.sprite == null) return;
            Vector2 size = _renderer.drawMode == SpriteDrawMode.Simple ? (Vector2)_renderer.sprite.bounds.size : _renderer.size;
            Vector2 center = _renderer.drawMode == SpriteDrawMode.Simple ? (Vector2)_renderer.sprite.bounds.center : Vector2.zero;

            switch (_collider)
            {
                case BoxCollider2D box: _owner.Motor.RoundCorners(box, size); box.offset = center; break;
                case CapsuleCollider2D cap: cap.size = size; cap.offset = center; break;
                case CircleCollider2D circle: circle.radius = Mathf.Max(size.x, size.y) * 0.5f; circle.offset = center; break;
                default: Debug.LogWarning($"[RuleEntity] {_owner.name}: {_collider.GetType().Name}는 자동 크기 맞춤을 지원하지 않음"); break;
            }
        }
    }
}
