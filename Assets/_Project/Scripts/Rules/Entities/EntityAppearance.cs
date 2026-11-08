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
            // 사망한 대상이 변신하면 새 모습으로 다시 살아난다 (사망 상태는 넘어가지 않는다)
            if (_owner.IsRemoved) _owner.Revive();
            _owner.Word = id;

            // 발 위치를 유지해야 커진 콜라이더가 바닥에 박히지 않는다
            float oldBottom = _collider != null ? _collider.bounds.min.y : _owner.transform.position.y;

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
                _rigidbody.position += new Vector2(0f, diff);
                _owner.transform.position += new Vector3(0f, diff, 0f);
            }
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
