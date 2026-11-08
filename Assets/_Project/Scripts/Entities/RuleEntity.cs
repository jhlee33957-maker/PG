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
        [Tooltip("켜면 플레이어 공격에 맞았을 때 사라진다(숨김). 다른 객체가 이 모습으로 변신하면 이 설정도 따라간다")]
        [SerializeField] private bool _removedByAttack;
        [Tooltip("켜면 Move로 움직일 때 가는 방향을 보도록 좌우를 뒤집는다. 스프라이트는 오른쪽을 보게 그려야 한다 (플레이어는 따로 처리)")]
        [SerializeField] private bool _faceMoveDirection = true;
        [Tooltip("중력이 없는 모습(벽 등)이 조종(Control)을 받았을 때 대신 쓸 중력. 원래 중력이 있으면 그 값을 쓴다")]
        [SerializeField, Min(0f)] private float _controlGravity = 1.5f;

        [Header("날기 (Fly)")]
        [Tooltip("날기 시작할 때 발밑 바닥에서 띄울 높이 범위(칸). 대상마다 이 안에서 랜덤으로 고른다")]
        [SerializeField] private Vector2 _flyHeightRange = new(6f, 8f);
        [Tooltip("떠 있는 높이에서 위아래로 둥실거리는 폭(칸)")]
        [SerializeField, Min(0f)] private float _bobAmplitude = 0.15f;
        [Tooltip("둥실거림 한 번(위-아래-위)에 걸리는 시간(초)")]
        [SerializeField, Min(0.1f)] private float _bobPeriod = 2f;
        [Tooltip("목표 높이를 따라가는 세기. 클수록 빨리 떠오른다")]
        [SerializeField, Min(0.1f)] private float _flyFollowStrength = 8f;
        [Tooltip("떠오르고 내려가는 최대 속도(칸/초)")]
        [SerializeField, Min(0.1f)] private float _flyMaxSpeed = 6f;
        [Tooltip("날기 시작할 때 바닥을 찾는 최대 거리(칸). 이 안에 바닥이 없으면 지금 위치를 바닥으로 본다")]
        [SerializeField, Min(0f)] private float _flyGroundSearchDistance = 20f;

        private static readonly RaycastHit2D[] GroundHits = new RaycastHit2D[4];
        private static readonly RaycastHit2D[] SolidHits = new RaycastHit2D[8];
        // 고정물 앞에서 멈출 때 남겨 두는 틈. 딱 붙이면 다음 검사에서 겹친 것으로 잡혀 떨린다
        private const float SolidSkin = 0.01f;
        private static PhysicsMaterial2D _noFriction;
        private static readonly System.Collections.Generic.List<Collider2D> Overlaps = new();

        private SpriteRenderer _renderer;
        private Rigidbody2D _rigidbody;
        private RuleManager _manager;
        private Sprite _originalSprite;
        private SpriteDrawMode _originalDrawMode;
        private Vector2 _originalSize;
        private Vector3 _originalScale;
        // 변신 대상에게 넘겨줄 배치 당시의 판정 설정 (출구는 통과되는 트리거, 비둘기는 막히는 몸 등)
        private bool _originalIsTrigger;
        private RigidbodyConstraints2D _originalConstraints;
        private RigidbodyType2D _originalBodyType;
        private bool _originalRemovedByAttack;
        // 지금 모습의 바디 타입. 변신하면 새 모습 프리팹의 타입으로 바뀐다
        private RigidbodyType2D _bodyType;
        private Collider2D _collider;
        private WordId _originalId;
        private bool _randomMove;
        private Vector2 _direction;
        private float _timer;
        private bool _wasGrounded;
        private float _snapBlockTimer;
        private float _defaultGravity;
        private bool _flying;
        private float _flyBaseY;      // 둥실거림의 가운데 높이 (Rigidbody 위치 기준)
        private float _bobPhase;      // 대상마다 다르게 둥실거리도록 하는 시작 박자
        private float _flyTime;
        private int _flyOffFrame = -1; // 날기가 꺼진 프레임. 같은 프레임에 다시 켜지면 높이를 새로 고르지 않는다
        private bool _baseFreezeRotation; // 지금 모습(프리팹) 설정의 회전 고정. Static 대상은 안 막혀 있을 수 있어서 날거나 조종받는 동안만 막는다
        private bool _controlled;         // 문장으로 조종(Control)을 받는 중
        // 사망(Remove)할 때 꺼 둔 콜라이더. 변신으로 다시 살아날 때 이것만 다시 켠다
        private readonly System.Collections.Generic.List<Collider2D> _collidersOffByRemove = new();
        // 지금 머리 위에 올라서 있는 대상들. 닿을 때와 떨어질 때만 바뀐다
        private readonly System.Collections.Generic.HashSet<RuleEntity> _treaders = new();
        // 위에서 눌렀다고 볼 접촉 법선의 기준 (이 대상 쪽으로 향하는 법선이 이만큼 아래를 향해야 한다)
        private const float TreadNormalY = -0.5f;
        // 지금 모습(프리팹) 설정의 통과 판정. 밟힐 수 있는 동안만 잠깐 끄고, 끝나면 이 값으로 돌아간다
        private bool _baseIsTrigger;
        private bool _treadable;
        // 만져진다(Touch)를 받은 동안. 통과되는 대상도 이때는 부딪히는 몸이 된다
        private bool _touchable;
        // 지금 닿아 있는 콜라이더 (벽·바닥 포함, 통과 판정 겹침 포함). 닿을 때와 떨어질 때만 바뀐다
        private readonly System.Collections.Generic.HashSet<Collider2D> _touching = new();

        // 서로 밀지 못하게: 스스로 움직이지 않는 동안은 가로 위치를 잠가서 부딪혀도 밀리지 않는다
        private const float StopSpeed = 0.05f;
        private bool _baseFreezeX;    // 프리팹(지금 모습) 설정에 원래 가로 고정이 있으면 항상 잠근다
        private bool _selfDriven;     // 플레이어 조작·대쉬 중 (플레이어가 알려 준다)
        private bool _keepMomentum;   // 스스로 움직이다 멈추는 중. 속도가 거의 0이 될 때까지는 잠그지 않는다

        /// <summary>현재 태그. Is 문장으로 바뀔 수 있다.</summary>
        public WordId Id => _word;
        /// <summary>배치될 때의 태그. 바뀔 모습을 찾을 때 기준이 된다.</summary>
        public WordId OriginalId => _originalId;
        public Sprite OriginalSprite => _originalSprite;
        /// <summary>규칙으로 랜덤 이동 중인지. 플레이어는 이때 좌우 입력을 양보한다.</summary>
        public bool IsRandomMoving => _randomMove;
        /// <summary>규칙(난다)으로 공중에 떠 있는지. 플레이어는 이때 점프하지 않고 중력을 켜지 않는다.</summary>
        public bool IsFlying => _flying;
        /// <summary>플레이어가 직접 조작 중이면 true. 이때는 랜덤 이동이 속도를 덮어쓰지 않는다.</summary>
        public bool InputOverride { get; set; }
        /// <summary>조종받는 대상을 플레이어가 움직이는 중인지(이동·대쉬·점프·공격). 규칙의 "움직이면" 조건에 걸린다.</summary>
        public bool IsInputMoving { get; private set; }
        /// <summary>문장으로 조종(Control)을 받아 플레이어가 조작하는 객체인지. 접촉 규칙(출구 등)에서 플레이어를 구분할 때 쓴다.</summary>
        public bool IsPlayer => _controlled;
        /// <summary>지금 상태에서 써야 할 중력. 나는 중이면 0, 중력 없는 모습이 조종받으면 조종용 중력.</summary>
        public float Gravity => _flying ? 0f : (_controlled && _defaultGravity <= 0f ? _controlGravity : _defaultGravity);
        /// <summary>
        /// 사망한(숨겨진) 상태. 공격과 Die 문장 모두 이 상태가 된다.
        /// 규칙에서는 "사망하면" 조건에만 걸리고, 접촉에서는 빠진다. 변신 원본으로는 계속 쓰인다.
        /// </summary>
        public bool IsRemoved { get; private set; }
        /// <summary>다른 대상이 지금 머리 위에 올라서 있는지. 규칙의 "밟히면" 조건에 걸린다. 내려가면 풀린다.</summary>
        public bool IsTreaded
        {
            get
            {
                foreach (var t in _treaders)
                    if (t != null && !t.IsRemoved) return true;
                return false;
            }
        }

        /// <summary>지금 무언가(벽·바닥 포함)에 닿아 있는지. 규칙의 "만지면" 조건에 걸린다. 떨어지면 풀린다.</summary>
        public bool IsTouched
        {
            get
            {
                foreach (var c in _touching)
                {
                    if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    var owner = c.GetComponentInParent<RuleEntity>();
                    if (owner != null && owner.IsRemoved) continue;
                    return true;
                }
                return false;
            }
        }

        /// <summary>지금 닿아 있는 다른 대상들(벽·바닥 대상 포함, 사망한 대상 제외)을 into에 담는다. 같은 대상은 한 번만 담는다.</summary>
        public void GetTouchingEntities(System.Collections.Generic.List<RuleEntity> into)
        {
            foreach (var c in _touching)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                var owner = c.GetComponentInParent<RuleEntity>();
                if (owner == null || owner == this || owner.IsRemoved || into.Contains(owner)) continue;
                into.Add(owner);
            }
        }

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
            _defaultGravity = _rigidbody.gravityScale;
            _originalIsTrigger = _collider != null && _collider.isTrigger;
            _baseIsTrigger = _originalIsTrigger;
            _originalConstraints = _rigidbody.constraints;
            _baseFreezeX = (_originalConstraints & RigidbodyConstraints2D.FreezePositionX) != 0;
            _baseFreezeRotation = (_originalConstraints & RigidbodyConstraints2D.FreezeRotation) != 0;
            _originalBodyType = _rigidbody.bodyType;
            _bodyType = _originalBodyType;
            _originalRemovedByAttack = _removedByAttack;
            _manager = FindAnyObjectByType<RuleManager>();
            // 조종은 문장으로만 받는다. 프리팹에 조작 스크립트가 남아 있어도 문장이 줄 때까지 꺼 둔다
            var presetPlayer = GetComponent<PlatformerPlayer>();
            if (presetPlayer != null) presetPlayer.enabled = false;
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
        /// 판정도 새 모습을 따라간다: 바디 타입(Static/Dynamic), 트리거 여부, 중력, 회전/이동 고정을 원래 그 객체의 설정으로 바꾼다.
        /// </summary>
        public void TransformInto(WordId id, RuleEntity source)
        {
            if (source == null) return;
            // 사망한 대상이 변신하면 새 모습으로 다시 살아난다 (사망 상태는 넘어가지 않는다)
            if (IsRemoved) Revive();
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
            CopyPhysicsFrom(source);
            ApplyBodyType();

            if (_collider != null)
            {
                Physics2D.SyncTransforms();
                float diff = oldBottom - _collider.bounds.min.y;
                _rigidbody.position += new Vector2(0f, diff);
                transform.position += new Vector3(0f, diff, 0f);
            }
        }

        private void CopyPhysicsFrom(RuleEntity source)
        {
            _baseIsTrigger = source._originalIsTrigger;
            ApplyTrigger();
            _bodyType = source._originalBodyType;
            _defaultGravity = source._defaultGravity;
            // 공격에 사라지는 성질도 새 모습을 따라간다
            _removedByAttack = source._originalRemovedByAttack;
            // 나는 중이거나 조종받는 중이면 중력과 회전 고정은 그 상태에 맞춰 두고, 끝날 때 새 설정으로 돌아간다
            _rigidbody.constraints = source._originalConstraints;
            _baseFreezeRotation = (source._originalConstraints & RigidbodyConstraints2D.FreezeRotation) != 0;
            ApplyGravity();
            ApplyRotationLock();
            _baseFreezeX = (source._originalConstraints & RigidbodyConstraints2D.FreezePositionX) != 0;
            UpdatePushLock();
        }

        /// <summary>
        /// 플레이어가 움직이는 중인지 알려 준다. 바뀔 때만 RuleManager에 알려서 "움직이면" 조건을 다시 따지게 한다.
        /// </summary>
        public void SetInputMoving(bool moving)
        {
            if (IsRemoved) moving = false;
            if (IsInputMoving == moving) return;
            IsInputMoving = moving;
            if (_manager != null) _manager.NotifyInputMovingChanged(this);
        }

        /// <summary>플레이어가 직접 움직이는 중인지 알려 준다(조작·대쉬). 이때만 가로 잠금이 풀린다.</summary>
        public void SetSelfDriven(bool driven)
        {
            _selfDriven = driven;
            UpdatePushLock();
        }

        // 스스로 움직일 때(조작, 대쉬, Move 규칙)와 그 움직임이 멈출 때까지만 가로로 움직일 수 있다.
        // 그 밖에는 가로 위치를 잠가서 누가 부딪혀도 밀리지 않는다. 세로(중력, 날기)는 그대로다
        private void UpdatePushLock()
        {
            if (IsRemoved || _rigidbody.bodyType != RigidbodyType2D.Dynamic) return;
            bool driven = _selfDriven || _randomMove;
            if (driven) _keepMomentum = true;
            else if (_keepMomentum && Mathf.Abs(_rigidbody.linearVelocity.x) < StopSpeed) _keepMomentum = false;

            bool lockX = _baseFreezeX || !(driven || _keepMomentum);
            var current = _rigidbody.constraints;
            var next = lockX
                ? current | RigidbodyConstraints2D.FreezePositionX
                : current & ~RigidbodyConstraints2D.FreezePositionX;
            if (next != current) _rigidbody.constraints = next;
        }

        // 지금 모습의 바디 타입(프리팹 기준)을 적용한다. 나는 중이거나 조종받는 중에는 Static 모습이어도 Dynamic으로 둔다
        private void ApplyBodyType()
        {
            var type = _flying || _controlled ? RigidbodyType2D.Dynamic : _bodyType;
            if (_rigidbody.bodyType == type) return;
            _rigidbody.bodyType = type;
            // Static으로 굳는 순간 남아 있던 속도 때문에 튀지 않게 한다
            if (type == RigidbodyType2D.Dynamic) _rigidbody.linearVelocity = Vector2.zero;
        }

        private void ApplyGravity() => _rigidbody.gravityScale = Gravity;

        // 날거나 조종받는 동안은 굴러가지 않게 회전을 막고, 끝나면 모습 설정대로 되돌린다
        private void ApplyRotationLock() => _rigidbody.freezeRotation = _baseFreezeRotation || _flying || _controlled;

        /// <summary>
        /// 조종(Control): 켜면 조작 스크립트(PlatformerPlayer)를 붙여 플레이어가 움직이게 하고, 끄면 그 스크립트를 꺼 둔다.
        /// Static 대상(벽, 출구)도 조종받는 동안은 Dynamic으로 풀리고, 중력이 없는 모습이면 조종용 중력을 쓴다.
        /// </summary>
        public void SetControl(bool on)
        {
            if (_controlled == on) return;
            _controlled = on;
            var player = GetComponent<PlatformerPlayer>();
            if (on)
            {
                ApplyBodyType();
                ApplyRotationLock();
                ApplyGravity();
                if (player == null) player = gameObject.AddComponent<PlatformerPlayer>();
                player.enabled = true;
                return;
            }

            if (player != null) player.enabled = false;
            InputOverride = false;
            SetInputMoving(false);
            SetSelfDriven(false);
            ApplyGravity();
            ApplyRotationLock();
            ApplyBodyType();
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

        /// <summary>
        /// 난다: 중력을 끄고 발밑 바닥에서 랜덤 높이까지 떠올라 위아래로 둥실거린다. 끄면 중력이 돌아와 떨어진다.
        /// Static 대상(벽, 출구)은 나는 동안만 Dynamic으로 풀고, 날기가 꺼지면 그 자리에서 다시 Static으로 고정된다.
        /// </summary>
        public void SetFly(bool on)
        {
            if (_flying == on) return;

            _flying = on;
            if (!on)
            {
                ApplyGravity();
                ApplyRotationLock();
                _flyOffFrame = Time.frameCount;
                ApplyBodyType();
                return;
            }

            ApplyBodyType();
            ApplyRotationLock();
            ApplyGravity();
            _wasGrounded = false;
            // 문장을 다시 적용할 때는 같은 프레임에 껐다 켜진다. 이때는 높이를 새로 고르지 않고 하던 대로 둥실거린다
            if (_flyOffFrame == Time.frameCount) return;

            float height = Random.Range(Mathf.Min(_flyHeightRange.x, _flyHeightRange.y), Mathf.Max(_flyHeightRange.x, _flyHeightRange.y));
            _flyBaseY = _rigidbody.position.y - DistanceToGround() + height;
            _bobPhase = Random.Range(0f, Mathf.PI * 2f);
            _flyTime = 0f;
        }

        // 발밑 바닥까지의 거리. 바닥이 없으면 0 (지금 위치를 바닥으로 본다)
        private float DistanceToGround()
        {
            if (_collider == null) return 0f;
            float best = float.MaxValue;
            int count = _collider.Cast(Vector2.down, GroundHits, _flyGroundSearchDistance);
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.collider.isTrigger || hit.normal.y <= 0.5f) continue;
                best = Mathf.Min(best, hit.distance);
            }
            return best == float.MaxValue ? 0f : best;
        }

        // 목표 높이(가운데 높이 + 사인 둥실거림)를 세로 속도로 따라간다. 속도로 움직여서 천장에 닿으면 막힌다
        private void UpdateFly()
        {
            _flyTime += Time.fixedDeltaTime;
            float targetY = _flyBaseY + Mathf.Sin(_flyTime * Mathf.PI * 2f / _bobPeriod + _bobPhase) * _bobAmplitude;
            float vy = Mathf.Clamp((targetY - _rigidbody.position.y) * _flyFollowStrength, -_flyMaxSpeed, _flyMaxSpeed);
            _rigidbody.linearVelocity = new Vector2(_rigidbody.linearVelocity.x, vy);
        }

        /// <summary>
        /// 플레이어 공격에 맞았다. 공격으로 사라지는 대상이면 숨기고 true를 돌려준다.
        /// 지우지 않고 숨기기만 해서 변신 원본(원래 모습)으로는 계속 쓸 수 있다. 리트·빌드 때 레벨을 다시 깔면 돌아온다.
        /// </summary>
        public bool TryRemoveByAttack()
        {
            if (!_removedByAttack || IsRemoved || IsPlayer) return false;
            Remove();
            // Die 문장으로 죽은 것과 똑같이 "사망하면" 조건을 다시 따지게 알린다 (죽는 순간에만 한 번)
            if (_manager != null) _manager.NotifyDied(this);
            return true;
        }

        /// <summary>
        /// 화면에서 숨기고 충돌·물리를 꺼서 더 이상 상호작용하지 않게 한다. 공격과 Die 문장이 같이 쓴다.
        /// 지우지 않으므로 변신 원본으로는 계속 쓸 수 있고, 레벨을 다시 깔면 돌아온다.
        /// </summary>
        public void Remove()
        {
            if (IsRemoved) return;
            IsRemoved = true;
            ResetState();
            SetControl(false);
            _renderer.enabled = false;
            _collidersOffByRemove.Clear();
            foreach (var col in GetComponents<Collider2D>())
            {
                if (!col.enabled) continue;
                col.enabled = false;
                _collidersOffByRemove.Add(col);
            }
            _rigidbody.linearVelocity = Vector2.zero;
            _rigidbody.simulated = false;
            _treaders.Clear(); // 사라지면 아무도 올라서 있을 수 없다
            _touching.Clear(); // 사라지면 아무것에도 닿아 있지 않다
        }

        // 사망 상태를 풀고 다시 보이게, 부딪히게 한다. 변신으로 살아날 때만 쓴다
        private void Revive()
        {
            IsRemoved = false;
            _renderer.enabled = true;
            foreach (var col in _collidersOffByRemove)
            {
                if (col != null) col.enabled = true;
            }
            _collidersOffByRemove.Clear();
            _rigidbody.simulated = true;
            _rigidbody.linearVelocity = Vector2.zero;
            // 변신에서 발 위치를 재려면 다시 켠 콜라이더의 크기가 바로 맞아야 한다
            Physics2D.SyncTransforms();
        }

        // 문장을 다시 적용하기 전에 지속 효과(이동, 날기)만 끈다. Is로 바뀐 모습과 태그는 영구 변화라 되돌리지 않는다.
        // 조종은 껐다 켜면 속도와 대쉬가 끊기므로 여기서 끄지 않고 RuleManager가 다시 적용할 때 맞춘다
        public void ResetState()
        {
            SetRandomMove(false);
            SetFly(false);
        }

        // 닿은 상대를 RuleManager에 알리기만 한다. 무엇이 일어날지는 RuleManager가 태그를 보고 정한다
        private void OnTriggerEnter2D(Collider2D other)
        {
            ReportContact(other);
            UpdateTouch(other, true);
        }

        private void OnTriggerExit2D(Collider2D other) => UpdateTouch(other, false);

        private void OnCollisionEnter2D(Collision2D collision)
        {
            ReportContact(collision.collider);
            UpdateTread(collision, true);
            UpdateTouch(collision.collider, true);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            UpdateTread(collision, false);
            UpdateTouch(collision.collider, false);
        }

        // 닿은 콜라이더를 기억하고, 떨어지면 지운다. 닿음 여부가 바뀔 때만 RuleManager에 알린다
        private void UpdateTouch(Collider2D other, bool entering)
        {
            // 자기 몸의 다른 콜라이더는 닿은 것으로 치지 않는다
            if (other == null || (_rigidbody != null && other.attachedRigidbody == _rigidbody)) return;
            bool was = IsTouched;
            bool setChanged = entering ? _touching.Add(other) : _touching.Remove(other);
            if (setChanged && _manager != null) _manager.NotifyTouchChanged(this, was != IsTouched);
        }

        /// <summary>
        /// 만져진다(Touch)를 받은 동안 켠다. 출구처럼 통과되는 대상도 이때는 부딪히는 몸이 된다.
        /// 부딪혀도 닿은 것으로 보고하므로 클리어 같은 접촉 규칙은 그대로 걸린다.
        /// </summary>
        public void SetTouchable(bool on)
        {
            if (_touchable == on) return;
            _touchable = on;
            ApplyTrigger();
        }

        // 위에서 내려와 머리에 닿은 대상을 기억하고, 떨어지면 지운다. 밟힘 여부가 바뀔 때만 RuleManager에 알린다
        private void UpdateTread(Collision2D collision, bool entering)
        {
            var other = collision.collider != null ? collision.collider.GetComponentInParent<RuleEntity>() : null;
            if (other == null || other == this) return;
            bool was = IsTreaded;
            if (entering)
            {
                if (IsPressedFromAbove(collision)) _treaders.Add(other);
            }
            else
            {
                _treaders.Remove(other);
            }
            if (was != IsTreaded && _manager != null) _manager.NotifyTreadChanged(this);
        }

        /// <summary>
        /// "밟히면" 조건 문장의 주어인 동안 켠다. 출구처럼 통과되는 대상도 이때는 부딪히는 몸이 되어 밟을 수 있다.
        /// 부딪혀도 닿은 것으로 보고하므로 클리어 같은 접촉 규칙은 그대로 걸린다.
        /// </summary>
        public void SetTreadable(bool on)
        {
            if (_treadable == on) return;
            _treadable = on;
            ApplyTrigger();
        }

        // 통과 판정은 밟힐 수 있거나 만져질 수 있는 동안만 끄고, 아니면 지금 모습 설정을 따른다
        private void ApplyTrigger()
        {
            if (_collider == null) return;
            bool trigger = _baseIsTrigger && !_treadable && !_touchable;
            if (_collider.isTrigger == trigger) return;
            _collider.isTrigger = trigger;
            // 판정이 바뀌면 지금 접촉은 물리 엔진이 새로 알려 주므로 기억한 접촉을 비운다
            bool wasTouched = IsTouched;
            bool hadContacts = _touching.Count > 0;
            _touching.Clear();
            if (hadContacts && _manager != null) _manager.NotifyTouchChanged(this, wasTouched);
            if (!trigger) return;
            // 다시 통과되면 아무도 올라서 있을 수 없다
            bool was = IsTreaded;
            _treaders.Clear();
            if (was && _manager != null) _manager.NotifyTreadChanged(this);
        }

        // 접촉 법선은 상대에서 이 대상 쪽으로 향한다. 아래를 향하면 상대가 위에서 누르는 것이다
        private static bool IsPressedFromAbove(Collision2D collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
                if (collision.GetContact(i).normal.y <= TreadNormalY) return true;
            return false;
        }

        private void ReportContact(Collider2D other)
        {
            if (_manager == null) return;
            var otherEntity = other.GetComponentInParent<RuleEntity>();
            if (otherEntity != null && otherEntity != this) _manager.ReportContact(this, otherEntity);
        }

        // 가는 방향을 보게 한다. 멈춘 방향(0)이면 보던 쪽을 그대로 둔다
        private void FaceDirection(float x)
        {
            if (!_faceMoveDirection || IsPlayer || x == 0f) return;
            _renderer.flipX = x < 0f;
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
            if (IsRemoved) return;
            UpdatePushLock();
            SnapToGround();
            if (_flying) UpdateFly();
            if (_randomMove) UpdateRandomMove();
            // 플레이어 조작이 이 뒤에 속도를 넣는 경우는 PlatformerPlayer가 한 번 더 부른다
            BlockBySolids();
        }

        // Move: 정해진 간격마다 좌우 방향을 랜덤으로 바꾸며 움직인다
        private void UpdateRandomMove()
        {
            // Move는 항상 좌우로만 움직인다. 중력을 받는 대상(나는 중 포함)은 속도로, 아닌 대상은 위치로 옮긴다
            bool usesGravity = (_rigidbody.gravityScale > 0f || _flying) && _rigidbody.bodyType == RigidbodyType2D.Dynamic;
            _timer -= Time.fixedDeltaTime;
            if (_timer <= 0f)
            {
                _direction = SideDirections[Random.Range(0, SideDirections.Length)];
                _timer = _directionInterval;
                FaceDirection(_direction.x);
            }

            // 플레이어 입력이 우선이라 조작 중에는 속도를 건드리지 않는다
            if (InputOverride) return;

            if (usesGravity)
                _rigidbody.linearVelocity = new Vector2(_direction.x * _moveSpeed, _rigidbody.linearVelocity.y);
            else
            {
                Vector2 delta = _direction * (_moveSpeed * Time.fixedDeltaTime);
                if (IsPassThrough) delta = new Vector2(LimitBySolid(Vector2.right, delta.x), LimitBySolid(Vector2.up, delta.y));
                _rigidbody.MovePosition(_rigidbody.position + delta);
            }
        }

        // 통과 판정(트리거) 몸인지. 트리거는 물리 충돌을 하지 않아서 그냥 두면 벽과 바닥을 뚫는다
        private bool IsPassThrough => !IsRemoved && _collider != null && _collider.isTrigger;

        /// <summary>
        /// 통과 판정(출구 등) 몸이 스스로 움직일 때(날기, Move, 조종) 벽·바닥 같은 고정물을 뚫지 않게 한다.
        /// 이번 물리 프레임에 갈 거리만큼 미리 쓸어 보고, 고정물(Static 바디나 바디 없는 콜라이더)에 닿으면 그 앞까지만 가게 속도를 줄인다.
        /// 플레이어·생물처럼 움직이는 몸은 그대로 통과한다. 속도를 넣은 뒤에 부른다.
        /// </summary>
        public void BlockBySolids()
        {
            if (!IsPassThrough || _rigidbody.bodyType != RigidbodyType2D.Dynamic) return;
            float dt = Time.fixedDeltaTime;
            Vector2 v = _rigidbody.linearVelocity;
            // 중력은 이번 프레임 속도에 더해지므로 아래로 갈 거리에 미리 넣어 둔다
            float vy = v.y + Physics2D.gravity.y * _rigidbody.gravityScale * dt;
            float limitedX = LimitBySolid(Vector2.right, v.x * dt) / dt;
            float limitedY = LimitBySolid(Vector2.up, vy * dt);
            bool blockedY = Mathf.Abs(limitedY) < Mathf.Abs(vy * dt) - 0.0001f;
            // 막혔으면 이번 프레임 중력이 더해진 뒤에 딱 막힌 곳까지만 가도록 중력만큼 미리 빼 둔다 (물리 엔진은 속도에 중력을 더한 뒤 움직인다)
            float nextVy = blockedY ? limitedY / dt - Physics2D.gravity.y * _rigidbody.gravityScale * dt : v.y;
            _rigidbody.linearVelocity = new Vector2(limitedX, blockedY ? nextVy : v.y);
        }

        // axis 방향(부호는 move)으로 move만큼 갈 때 고정물에 막히면 그 앞까지의 거리로 줄인다
        private float LimitBySolid(Vector2 axis, float move)
        {
            float distance = Mathf.Abs(move);
            if (distance < 0.00001f) return move;
            Vector2 dir = axis * Mathf.Sign(move);

            var filter = new ContactFilter2D();
            filter.NoFilter();
            filter.useTriggers = false;
            int count = _collider.Cast(dir, filter, SolidHits, distance + SolidSkin, true);
            float allowed = distance;
            for (int i = 0; i < count; i++)
            {
                var hit = SolidHits[i];
                var other = hit.collider;
                if (other == null || other.isTrigger) continue;
                var body = other.attachedRigidbody;
                if (body == _rigidbody) continue;
                // 움직이는 몸(플레이어, 생물, 나는 벽 등)은 고정물이 아니라 통과한다
                if (body != null && body.bodyType != RigidbodyType2D.Static) continue;
                // 가는 방향을 마주 보는 면만 막는다. 발밑 바닥처럼 이미 닿아 있는 면에서 옆이나 위로 갈 때는 막지 않는다
                if (Vector2.Dot(hit.normal, dir) > -0.5f) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - SolidSkin));
            }
            return Mathf.Sign(move) * allowed;
        }
    }
}