using UnityEngine;
using CoffeeSpec.Player;
using CoffeeSpec.Rules;

namespace CoffeeSpec.Entities
{
    /// <summary>
    /// 규칙의 대상. 프리팹에 붙여 두고 어떤 명사인지는 WordId로만 구분한다.
    /// 바뀔 수 있는 값만 열어 두고 규칙 내용은 모른다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
    public class RuleEntity : MonoBehaviour, ISerializationCallbackReceiver
    {
        private static readonly Vector2[] Directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        private static readonly Vector2[] SideDirections = { Vector2.left, Vector2.right };

        [SerializeField] private WordId _word;
        // 예전 enum 숫자가 저장된 칸. 불러올 때 _word로 옮기고 -1로 비운다
        [SerializeField, HideInInspector] private int _id = -1;
        [SerializeField] private float _moveSpeed = 2f;
        [Tooltip("랜덤 방향을 다시 고르는 간격(초)")]
        [SerializeField] private float _directionInterval = 1f;
        [Tooltip("박스 콜라이더 모서리를 둥글게 깎는 정도. 바닥 타일 이음매에 걸려 멈추는 것을 막는다 (겉 크기는 그대로)")]
        [SerializeField, Min(0f)] private float _colliderCornerRadius = 0.02f;
        [Tooltip("걷다가 이 거리 이내로 발이 뜨면 바닥에 다시 붙인다. 이음매를 넘을 때 통통 튀는 것을 막는다")]
        [SerializeField, Min(0f)] private float _groundSnapDistance = 0.1f;
        [Tooltip("이보다 깊게 바닥에 박히면 바로 빼낸다. 너무 작으면 물리 엔진과 싸워 떨린다 (1/32 = 1픽셀)")]
        [SerializeField, Min(0.01f)] private float _sinkTolerance = 0.03f;

        private static readonly RaycastHit2D[] GroundHits = new RaycastHit2D[4];
        private static PhysicsMaterial2D _noFriction;
        private static readonly System.Collections.Generic.List<Collider2D> Overlaps = new();

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
        private bool _wasGrounded;
        private float _snapBlockTimer;

        /// <summary>현재 태그. Is 문장으로 바뀔 수 있다.</summary>
        public WordId Id => _word;
        /// <summary>배치될 때의 태그. 바뀔 모습을 찾을 때 기준이 된다.</summary>
        public WordId OriginalId => _originalId;
        public Sprite OriginalSprite => _originalSprite;
        /// <summary>규칙으로 랜덤 이동 중인지. 플레이어는 이때 좌우 입력을 양보한다.</summary>
        public bool IsRandomMoving => _randomMove;
        /// <summary>플레이어가 직접 조작 중이면 true. 이때는 랜덤 이동이 속도를 덮어쓰지 않는다.</summary>
        public bool InputOverride { get; set; }
        /// <summary>플레이어가 조작하는 객체인지. 접촉 규칙(출구 등)에서 플레이어를 구분할 때 쓴다.</summary>
        public bool IsPlayer { get; private set; }

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
            _originalId = _word;
            _manager = FindAnyObjectByType<RuleManager>();
            IsPlayer = GetComponent<PlatformerPlayer>() != null;
            if (_collider is BoxCollider2D box) RoundCorners(box, box.size);
            ApplyNoFriction();
            // 빠르게 떨어지면 한 프레임 사이에 타일 속으로 들어갔다가 천천히 밀려 나온다. 연속 충돌 검사로 착지 순간에 막는다
            _rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        // 타일마다 콜라이더가 따로 있으면 각진 박스가 이음매 모서리에 걸려 가로 이동이 막힌다.
        // 겉 크기(fullSize)는 그대로 두고 모서리만 둥글게 해서 이음매를 타고 넘게 한다
        private void RoundCorners(BoxCollider2D box, Vector2 fullSize)
        {
            float r = Mathf.Min(_colliderCornerRadius, fullSize.x * 0.5f, fullSize.y * 0.5f);
            box.edgeRadius = r;
            box.size = fullSize - new Vector2(r * 2f, r * 2f);
        }

        // 마찰을 없애 벽에 붙거나 바닥에 끌리지 않게 한다. 재질을 직접 넣어 둔 경우는 그대로 둔다
        private void ApplyNoFriction()
        {
            if (_rigidbody.sharedMaterial != null || (_collider != null && _collider.sharedMaterial != null)) return;
            if (_noFriction == null) _noFriction = new PhysicsMaterial2D("NoFriction") { friction = 0f, bounciness = 0f };
            _rigidbody.sharedMaterial = _noFriction;
        }

        // 착지할 때 타일 속으로 들어간 만큼 바로 위로 빼낸다. 물리 엔진은 여러 프레임에 걸쳐 천천히 밀어내서
        // 박혀 있다가 걸으면서 풀리는 것처럼 보인다
        private void PushOutOfGround()
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.NoFilter();
            filter.useTriggers = false;
            int n = _collider.Overlap(filter, Overlaps);
            float up = 0f;
            for (int i = 0; i < n; i++)
            {
                var other = Overlaps[i];
                if (other.isTrigger || other.attachedRigidbody == _rigidbody) continue;
                var d = _collider.Distance(other);
                if (!d.isOverlapped) continue;
                // 겹쳤을 때 pointA(내 표면)에서 pointB(상대 표면)로 가는 만큼 움직이면 빠져나온다. 위로 빼야 하는 경우만 처리한다
                Vector2 separation = d.pointB - d.pointA;
                if (separation.y > Mathf.Abs(separation.x) && separation.y > up) up = separation.y;
            }
            // 바닥에 서 있으면 원래 아주 살짝(contact offset) 겹쳐 있다. 그것까지 빼면 엔진과 싸워 떨리므로
            // 눈에 보일 만큼(1픽셀 이상) 박혔을 때만 빼고, 엔진이 쓰는 간격은 남긴다
            float slop = Physics2D.defaultContactOffset;
            if (up <= _sinkTolerance) return;
            _rigidbody.position += Vector2.up * (up - slop);
            var v = _rigidbody.linearVelocity;
            if (v.y < 0f) _rigidbody.linearVelocity = new Vector2(v.x, 0f);
        }

        /// <summary>점프처럼 일부러 땅에서 떨어질 때 부른다. 그동안은 바닥에 붙이지 않는다.</summary>
        public void BlockGroundSnap(float seconds)
        {
            _snapBlockTimer = Mathf.Max(_snapBlockTimer, seconds);
            _wasGrounded = false;
        }

        // 직전에 땅에 있었는데 발밑 조금 아래에 바닥이 있으면 이음매를 넘다 뜬 것이므로 바닥에 다시 붙인다.
        // 공중에서 내려오는 중이면 붙이지 않고 자연스럽게 착지하게 둔다
        private void SnapToGround()
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
            int count = _collider.Cast(Vector2.down, GroundHits, _groundSnapDistance);
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.collider.isTrigger || hit.normal.y <= 0.5f) continue;
                gap = Mathf.Min(gap, hit.distance);
            }

            bool nearGround = gap <= _groundSnapDistance;
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

        private void OnEnable() { if (_manager != null) _manager.Register(this); }
        private void OnDisable() { if (_manager != null) _manager.Unregister(this); }

        public void SetSprite(Sprite sprite) { if (sprite != null) _renderer.sprite = sprite; }

        // X는 Y다: 모습과 태그를 함께 바꿔서 이후 문장에서는 Y로 취급되게 한다
        public void TransformInto(WordId id, Sprite sprite)
        {
            _word = id;
            SetSprite(sprite);
            ApplyBodyType();
        }

        /// <summary>
        /// 다른 객체의 원래 모습으로 변신한다. 스프라이트만 바꾸면 그리는 크기가 그대로라 찌그러지므로
        /// 그리는 방식, 크기, 스케일까지 복사하고 콜라이더도 새 모습에 맞춘다.
        /// </summary>
        public void TransformInto(WordId id, RuleEntity source)
        {
            if (source == null) return;
            _word = id;

            // 발 위치를 유지해야 커진 콜라이더가 바닥에 박히지 않는다
            float oldBottom = _collider != null ? _collider.bounds.min.y : transform.position.y;

            _renderer.sprite = source._originalSprite;
            _renderer.drawMode = source._originalDrawMode;
            if (source._originalDrawMode != SpriteDrawMode.Simple) _renderer.size = source._originalSize;
            transform.localScale = new Vector3(
                Mathf.Abs(source._originalScale.x) * Mathf.Sign(transform.localScale.x),
                source._originalScale.y, source._originalScale.z);

            FitColliderToSprite();
            ApplyBodyType();

            if (_collider != null)
            {
                Physics2D.SyncTransforms();
                float diff = oldBottom - _collider.bounds.min.y;
                _rigidbody.position += new Vector2(0f, diff);
                transform.position += new Vector3(0f, diff, 0f);
            }
        }

        // 태그가 바뀌면 CSV의 IsStatic에 맞춰 Static/Dynamic을 바꾼다
        private void ApplyBodyType()
        {
            var type = WordTags.IsStatic(_word) ? RigidbodyType2D.Static : RigidbodyType2D.Dynamic;
            if (_rigidbody.bodyType == type) return;
            _rigidbody.bodyType = type;
            // Static으로 굳는 순간 남아 있던 속도 때문에 튀지 않게 한다
            if (type == RigidbodyType2D.Dynamic) _rigidbody.linearVelocity = Vector2.zero;
        }

        // 판정이 그림과 어긋나지 않도록 콜라이더를 지금 그려지는 영역(로컬 기준)에 맞춘다
        private void FitColliderToSprite()
        {
            if (_collider == null || _renderer.sprite == null) return;
            Vector2 size = _renderer.drawMode == SpriteDrawMode.Simple ? (Vector2)_renderer.sprite.bounds.size : _renderer.size;
            Vector2 center = _renderer.drawMode == SpriteDrawMode.Simple ? (Vector2)_renderer.sprite.bounds.center : Vector2.zero;

            switch (_collider)
            {
                case BoxCollider2D box: RoundCorners(box, size); box.offset = center; break;
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

        // 닿은 상대를 RuleManager에 알리기만 한다. 무엇이 일어날지는 RuleManager가 태그를 보고 정한다
        private void OnTriggerEnter2D(Collider2D other) => ReportContact(other);
        private void OnCollisionEnter2D(Collision2D collision) => ReportContact(collision.collider);

        private void ReportContact(Collider2D other)
        {
            if (_manager == null) return;
            var otherEntity = other.GetComponentInParent<RuleEntity>();
            if (otherEntity != null && otherEntity != this) _manager.ReportContact(this, otherEntity);
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            if (_id < 0) return;
            if (_word.IsEmpty) _word = WordId.FromLegacy(_id);
            _id = -1;
        }

        private void FixedUpdate()
        {
            SnapToGround();
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