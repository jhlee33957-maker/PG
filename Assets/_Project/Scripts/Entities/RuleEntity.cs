using UnityEngine;
using CoffeeSpec.Rules;

namespace CoffeeSpec.Entities
{
    /// <summary>
    /// 규칙의 대상. 프리팹에 붙여 두고 어떤 명사인지는 WordId로만 구분한다.
    /// 바뀔 수 있는 값만 열어 두고 규칙 내용은 모른다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
    public class RuleEntity : MonoBehaviour
    {
        private static readonly Vector2[] Directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        private static readonly Vector2[] SideDirections = { Vector2.left, Vector2.right };

        [SerializeField] private WordId _id;
        [SerializeField] private float _moveSpeed = 2f;
        [Tooltip("랜덤 방향을 다시 고르는 간격(초)")]
        [SerializeField] private float _directionInterval = 1f;

        private SpriteRenderer _renderer;
        private Rigidbody2D _rigidbody;
        private RuleManager _manager;
        private Sprite _originalSprite;
        private SpriteDrawMode _originalDrawMode;
        private Vector2 _originalSize;
        private Vector3 _originalScale;
        private Collider2D _collider;
        private WordId _originalId;
        private bool _randomMove;
        private Vector2 _direction;
        private float _timer;

        /// <summary>현재 태그. Is 문장으로 바뀔 수 있다.</summary>
        public WordId Id => _id;
        /// <summary>배치될 때의 태그. 바뀔 모습을 찾을 때 기준이 된다.</summary>
        public WordId OriginalId => _originalId;
        public Sprite OriginalSprite => _originalSprite;
        /// <summary>규칙으로 랜덤 이동 중인지. 플레이어는 이때 좌우 입력을 양보한다.</summary>
        public bool IsRandomMoving => _randomMove;
        /// <summary>플레이어가 직접 조작 중이면 true. 이때는 랜덤 이동이 속도를 덮어쓰지 않는다.</summary>
        public bool InputOverride { get; set; }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _rigidbody = GetComponent<Rigidbody2D>();
            _originalSprite = _renderer.sprite;
            // 변신 대상이 될 때 그대로 넘겨줄 수 있게 배치 당시의 "보이는 크기"를 같이 기억한다
            _originalDrawMode = _renderer.drawMode;
            _originalSize = _renderer.size;
            _originalScale = transform.localScale;
            _collider = GetComponent<Collider2D>();
            _originalId = _id;
            _manager = FindAnyObjectByType<RuleManager>();
        }

        private void OnEnable() { if (_manager != null) _manager.Register(this); }
        private void OnDisable() { if (_manager != null) _manager.Unregister(this); }

        public void SetSprite(Sprite sprite) { if (sprite != null) _renderer.sprite = sprite; }

        // X는 Y다: 모습과 태그를 함께 바꿔서 이후 문장에서는 Y로 취급되게 한다
        public void TransformInto(WordId id, Sprite sprite)
        {
            _id = id;
            SetSprite(sprite);
        }

        /// <summary>
        /// 다른 객체의 원래 모습으로 변신한다. 스프라이트만 바꾸면 그리는 크기가 그대로라 찌그러지므로
        /// 그리는 방식, 크기, 스케일까지 복사하고 콜라이더도 새 모습에 맞춘다.
        /// </summary>
        public void TransformInto(WordId id, RuleEntity source)
        {
            if (source == null) return;
            _id = id;

            // 발 위치를 유지해야 커진 콜라이더가 바닥에 박히지 않는다
            float oldBottom = _collider != null ? _collider.bounds.min.y : transform.position.y;

            _renderer.sprite = source._originalSprite;
            _renderer.drawMode = source._originalDrawMode;
            if (source._originalDrawMode != SpriteDrawMode.Simple) _renderer.size = source._originalSize;
            transform.localScale = new Vector3(
                Mathf.Abs(source._originalScale.x) * Mathf.Sign(transform.localScale.x),
                source._originalScale.y, source._originalScale.z);

            FitColliderToSprite();

            if (_collider != null)
            {
                Physics2D.SyncTransforms();
                float diff = oldBottom - _collider.bounds.min.y;
                _rigidbody.position += new Vector2(0f, diff);
                transform.position += new Vector3(0f, diff, 0f);
            }
        }

        // 판정이 그림과 어긋나지 않도록 콜라이더를 지금 그려지는 영역(로컬 기준)에 맞춘다
        private void FitColliderToSprite()
        {
            if (_collider == null || _renderer.sprite == null) return;
            Vector2 size = _renderer.drawMode == SpriteDrawMode.Simple ? (Vector2)_renderer.sprite.bounds.size : _renderer.size;
            Vector2 center = _renderer.drawMode == SpriteDrawMode.Simple ? (Vector2)_renderer.sprite.bounds.center : Vector2.zero;

            switch (_collider)
            {
                case BoxCollider2D box: box.size = size; box.offset = center; break;
                case CapsuleCollider2D cap: cap.size = size; cap.offset = center; break;
                case CircleCollider2D circle: circle.radius = Mathf.Max(size.x, size.y) * 0.5f; circle.offset = center; break;
                default: Debug.LogWarning($"[RuleEntity] {name}: {_collider.GetType().Name}는 자동 크기 맞춤을 지원하지 않음"); break;
            }
        }

        public void SetRandomMove(bool on)
        {
            if (_randomMove == on) return;
            _randomMove = on;
            _timer = 0f;
            if (!on) _direction = Vector2.zero;
        }

        // 문장을 다시 적용하기 전에 지속 효과(이동)만 끈다. Is로 바뀐 모습과 태그는 영구 변화라 되돌리지 않는다
        public void ResetState() => SetRandomMove(false);

        private void FixedUpdate()
        {
            if (!_randomMove) return;
            // 중력을 받는 대상(플랫포머)은 위아래로 움직이면 물리와 싸우므로 좌우로만 움직인다
            bool usesGravity = _rigidbody.gravityScale > 0f && _rigidbody.bodyType == RigidbodyType2D.Dynamic;
            _timer -= Time.fixedDeltaTime;
            if (_timer <= 0f)
            {
                var dirs = usesGravity ? SideDirections : Directions;
                _direction = dirs[Random.Range(0, dirs.Length)];
                _timer = _directionInterval;
            }

            // 플레이어 입력이 우선이라 조작 중에는 속도를 건드리지 않는다
            if (InputOverride) return;

            if (usesGravity)
                _rigidbody.linearVelocity = new Vector2(_direction.x * _moveSpeed, _rigidbody.linearVelocity.y);
            else
                _rigidbody.MovePosition(_rigidbody.position + _direction * (_moveSpeed * Time.fixedDeltaTime));
        }
    }
}