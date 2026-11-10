using UnityEngine;
using CoffeeSpec.Player;
using CoffeeSpec.Rules;

namespace CoffeeSpec.Rules.Entities
{
    /// <summary>
    /// 규칙의 대상. 프리팹에 붙여 두고 어떤 명사인지는 WordId로만 구분한다.
    /// 바뀔 수 있는 값만 열어 두고 규칙 내용은 모른다.
    /// </summary>
    // 네임스페이스를 옮겨도 이전 프리팹에 저장된 클래스 이름을 찾을 수 있게 한다.
    [UnityEngine.Scripting.APIUpdating.MovedFrom("CoffeeSpec.Entities")]
    [RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
    public class RuleEntity : MonoBehaviour, ISerializationCallbackReceiver
    {

        [SerializeField] private WordId _word;
        // 예전 enum 숫자가 저장된 칸. 불러올 때 _word로 옮기고 -1로 비운다
        [SerializeField, HideInInspector] private int _id = -1;
        [SerializeField] private float _moveSpeed = 2f;
        [Tooltip("박스 콜라이더 모서리를 둥글게 깎는 정도. 바닥 타일 이음매에 걸려 멈추는 것을 막는다 (겉 크기는 그대로)")]
        [SerializeField, Min(0f)] private float _colliderCornerRadius = 0.02f;
        [Tooltip("걷다가 이 거리 이내로 발이 뜨면 바닥에 다시 붙인다. 이음매를 넘을 때 통통 튀는 것을 막는다")]
        [SerializeField, Min(0f)] private float _groundSnapDistance = 0.1f;
        [Tooltip("이보다 깊게 바닥에 박히면 바로 빼낸다. 너무 작으면 물리 엔진과 싸워 떨린다 (1/32 = 1픽셀)")]
        [SerializeField, Min(0.01f)] private float _sinkTolerance = 0.03f;
        [Tooltip("변신한 뒤 이 시간(초) 동안은 사망하지 않는다. 사망하면 다른 모습으로 살아난 직전에 닿아 있던 것에 바로 또 죽지 않게 한다")]
        [SerializeField, Min(0f)] private float _transformInvincibleTime = 0.2f;
        [Tooltip("변신한 뒤 이 시간(초) 동안은 다른 것으로 다시 변신하지 않는다. 연달아 걸리는 변신 문장 때문에 바로 또 바뀌는 것을 막는다")]
        [SerializeField, Min(0f)] private float _transformLockTime = 0.1f;
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

        // Unity 메시지와 규칙 상태는 유지하고, 물리 부품과 실행 순서는 EntityPhysics에 맡긴다.
        private EntityPhysics _physics;
        private EntityContacts _contacts;
        private EntityAppearance _appearance;
        private PlatformerPlayer _player;
        internal event System.Action<RuleEntity> ContactsInvalidated;

        internal EntityMotor Motor => _physics?.Motor;
        internal EntityFly Fly => _physics?.Fly;
        internal EntityMove Move => _physics?.Move;
        internal EntityContacts Contacts => _contacts;
        internal EntityAppearance Appearance => _appearance;
        internal float MoveSpeed => _moveSpeed;
        internal RigidbodyType2D BodyType { get => _bodyType; set => _bodyType = value; }
        internal Vector2 FlyHeightRange => _flyHeightRange;
        internal RuleManager Manager
        {
            get
            {
                if (_manager == null && isActiveAndEnabled) BindManager(RuleManager.Instance);
                return _manager != null ? _manager : null;
            }
        }
        internal bool FaceMoveDirection => _faceMoveDirection;
        internal float SinkTolerance => _sinkTolerance;
        internal bool BaseFreezeRotation { get => _baseFreezeRotation; set => _baseFreezeRotation = value; }
        internal float ColliderCornerRadius => _colliderCornerRadius;
        internal float BobAmplitude => _bobAmplitude;
        internal float FlyMaxSpeed => _flyMaxSpeed;
        internal float FlyGroundSearchDistance => _flyGroundSearchDistance;
        internal float FlyFollowStrength => _flyFollowStrength;
        internal float BobPeriod => _bobPeriod;
        internal WordId Word { get => _word; set => _word = value; }
        internal float GroundSnapDistance => _groundSnapDistance;
        internal float DefaultGravity { get => _defaultGravity; set => _defaultGravity = value; }

        /// <summary>아래로 쏜 결과가 벽 이음매가 아닌 밟을 수 있는 바닥인지 확인한다.</summary>
        public static bool IsGroundHit(RaycastHit2D hit, Collider2D mine) => EntityMotor.IsGroundHit(hit, mine);

        public bool IsTreaded => _contacts != null && _contacts.IsTreaded;
        public bool IsTouched => _contacts != null && _contacts.IsTouched;
        public void GetTouchingEntities(System.Collections.Generic.List<RuleEntity> into) => _contacts?.GetTouchingEntities(into);
        public void BlockGroundSnap(float seconds) => _physics.Motor.BlockGroundSnap(seconds);
        public void SetSelfDriven(bool driven) => _physics.Motor.SetSelfDriven(driven);
        public void BlockBySolids() => _physics.Motor.BlockBySolids();
        public void SetSprite(Sprite sprite) => _appearance.SetSprite(sprite);
        public void TransformInto(WordId id, RuleEntity source) => _appearance.TransformInto(id, source);
        public void SetRandomMove(bool on) => _physics.Move.SetRandomMove(on);
        public void SetFly(bool on) => _physics.Fly.SetFly(on);
        public void SetTouchable(bool on) => _contacts.SetTouchable(on);
        public void SetTreadable(bool on) => _contacts.SetTreadable(on);

        private SpriteRenderer _renderer;
        private Rigidbody2D _rigidbody;
        private RuleManager _manager;
        // 지금 모습의 바디 타입. 변신하면 새 모습 프리팹의 타입으로 바뀐다
        private RigidbodyType2D _bodyType;
        private Collider2D _collider;
        private WordId _originalId;
        private float _defaultGravity;
        private bool _baseFreezeRotation; // 지금 모습(프리팹) 설정의 회전 고정. Static 대상은 안 막혀 있을 수 있어서 날거나 조종받는 동안만 막는다
        private bool _controlled;         // 문장으로 조종(Control)을 받는 중
        // 사망(Remove)할 때 꺼 둔 콜라이더. 변신으로 다시 살아날 때 이것만 다시 켠다
        private readonly System.Collections.Generic.List<Collider2D> _collidersOffByRemove = new();

        /// <summary>현재 태그. Is 문장으로 바뀔 수 있다.</summary>
        public WordId Id => _word;
        /// <summary>배치될 때의 태그. 바뀔 모습을 찾을 때 기준이 된다.</summary>
        public WordId OriginalId => _originalId;
        public Sprite OriginalSprite => _appearance?.OriginalSprite;
        /// <summary>규칙(Move)으로 이동 중인지. 플레이어는 이때 좌우 입력을 양보한다.</summary>
        public bool IsRandomMoving => _physics != null && _physics.Move.IsRandomMoving;
        /// <summary>규칙(난다)으로 공중에 떠 있는지. 플레이어는 이때 점프하지 않고 중력을 켜지 않는다.</summary>
        public bool IsFlying => _physics != null && _physics.Fly.IsFlying;
        /// <summary>플레이어가 직접 조작 중이면 true. 이때는 Move 이동이 속도를 덮어쓰지 않는다.</summary>
        public bool InputOverride { get; set; }
        /// <summary>조종받는 대상을 플레이어가 움직이는 중인지(이동·점프). 규칙의 "움직이면" 조건에 걸린다.</summary>
        public bool IsInputMoving { get; private set; }
        /// <summary>문장으로 조종(Control)을 받아 플레이어가 조작하는 객체인지. 접촉 규칙(출구 등)에서 플레이어를 구분할 때 쓴다.</summary>
        public bool IsControlled => _controlled;
        /// <summary>지금 상태에서 써야 할 중력. 나는 중이면 0, 중력 없는 모습이 조종받으면 조종용 중력.</summary>
        public float Gravity => IsFlying ? 0f : (_controlled && _defaultGravity <= 0f ? _controlGravity : _defaultGravity);
        /// <summary>
        /// 사망한(숨겨진) 상태. Die 문장으로 이 상태가 된다.
        /// 규칙에서는 "사망하면" 조건에만 걸리고, 접촉에서는 빠진다. 변신 원본으로는 계속 쓰인다.
        /// </summary>
        public bool IsRemoved { get; private set; }
        /// <summary>변신 직후 무적 시간 중인지. 이 동안은 Die 문장으로 죽지 않는다.</summary>
        public bool IsInvincible => Time.time < _invincibleUntil;
        private float _invincibleUntil = -1f;
        private Coroutine _invincibleRoutine;
        private float _transformLockedUntil = -1f;
        private Coroutine _transformLockRoutine;

        /// <summary>지금 변신할 수 있는지. 변신 직후 잠깐은 다른 것으로 바뀌지 않는다.</summary>
        public bool CanTransform => !Application.isPlaying || Time.time >= _transformLockedUntil;

        /// <summary>변신할 때 부른다. 잠금이 끝나면 규칙을 다시 따져서 그동안 막힌 변신이 그때 일어나게 한다.</summary>
        internal void StartTransformLock()
        {
            if (!Application.isPlaying || _transformLockTime <= 0f || !isActiveAndEnabled) return;
            _transformLockedUntil = Time.time + _transformLockTime;
            if (_transformLockRoutine != null) StopCoroutine(_transformLockRoutine);
            _transformLockRoutine = StartCoroutine(EndTransformLockAfter(_transformLockTime));
        }

        private System.Collections.IEnumerator EndTransformLockAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _transformLockRoutine = null;
            Manager?.NotifyTransformLockEnded(this);
        }
        /// <summary>변신할 때 부른다. 무적 시간을 새로 시작하고, 끝나면 규칙을 다시 따지게 알린다 (계속 닿아 있으면 그때 죽는다).</summary>
        internal void StartTransformInvincible()
        {
            if (!Application.isPlaying || _transformInvincibleTime <= 0f || !isActiveAndEnabled) return;
            _invincibleUntil = Time.time + _transformInvincibleTime;
            if (_invincibleRoutine != null) StopCoroutine(_invincibleRoutine);
            _invincibleRoutine = StartCoroutine(EndInvincibleAfter(_transformInvincibleTime));
        }

        private System.Collections.IEnumerator EndInvincibleAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _invincibleRoutine = null;
            Manager?.NotifyInvincibleEnded(this);
        }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _rigidbody = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();
            _physics = new EntityPhysics(this, _rigidbody, _collider, _renderer);
            _contacts = new EntityContacts(this, _rigidbody, _collider);
            _appearance = new EntityAppearance(this, _rigidbody, _collider, _renderer);
            _appearance.OriginalSprite = _renderer.sprite;
            // 변신 대상이 될 때 그대로 넘겨줄 수 있게 배치 당시의 "보이는 크기"를 같이 기억한다
            _appearance.OriginalDrawMode = _renderer.drawMode;
            _appearance.OriginalSize = _renderer.size;
            _appearance.OriginalScale = transform.localScale;
            _originalId = _word;
            _defaultGravity = _rigidbody.gravityScale;
            _appearance.OriginalIsTrigger = _collider != null && _collider.isTrigger;
            _contacts.BaseIsTrigger = _appearance.OriginalIsTrigger;
            _appearance.OriginalConstraints = _rigidbody.constraints;
            _physics.Motor.BaseFreezeX = (_appearance.OriginalConstraints & RigidbodyConstraints2D.FreezePositionX) != 0;
            _baseFreezeRotation = (_appearance.OriginalConstraints & RigidbodyConstraints2D.FreezeRotation) != 0;
            _appearance.OriginalBodyType = _rigidbody.bodyType;
            _bodyType = _appearance.OriginalBodyType;

            // 조종은 문장으로만 받는다. 프리팹에 조작 스크립트가 남아 있어도 문장이 줄 때까지 꺼 둔다
            _player = GetComponent<PlatformerPlayer>();
            if (_player != null) _player.enabled = false;
            if (_collider is BoxCollider2D box) _physics.Motor.RoundCorners(box, box.size);
            _physics.Motor.ApplyNoFriction();
            // 빠르게 떨어지면 한 프레임 사이에 타일 속으로 들어갔다가 천천히 밀려 나온다. 연속 충돌 검사로 착지 순간에 막는다
            _rigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        private bool _warnedMissingManager;

        private void OnEnable()
        {
            RuleManager.Available += BindManager;
            BindManager(RuleManager.Instance);
        }

        private void Start()
        {
            // 모든 Awake가 끝난 뒤에만 경고해 정상적인 생성 순서에는 경고하지 않는다.
            if (Manager == null && !_warnedMissingManager)
            {
                _warnedMissingManager = true;
                Debug.LogWarning("[RuleEntity] RuleManager가 없어 규칙 적용을 기다리는 중", this);
            }
        }

        private void BindManager(RuleManager manager)
        {
            if (!isActiveAndEnabled || manager == null || _manager == manager) return;
            if (_manager != null) _manager.Unregister(this);
            _manager = manager;
            _manager.Register(this);
        }

        private void OnDisable()
        {
            RuleManager.Available -= BindManager;
            if (_manager != null) _manager.Unregister(this);
            _manager = null;
            _contacts?.ClearTreaders();
            _contacts?.ClearTouching();
            ContactsInvalidated?.Invoke(this);
            // 꺼지면 코루틴도 멈추므로 무적도 같이 끝낸다
            _invincibleRoutine = null;
            _invincibleUntil = -1f;
            _transformLockRoutine = null;
            _transformLockedUntil = -1f;
        }

        // X는 Y다: 모습과 태그를 함께 바꿔서 이후 문장에서는 Y로 취급되게 한다
        public void TransformInto(WordId id, Sprite sprite)
        {
            _word = id;
            _appearance.SetSprite(sprite);
            ApplyBodyType();
        }

        /// <summary>
        /// 플레이어가 움직이는 중인지 알려 준다. 바뀔 때만 RuleManager에 알려서 "움직이면" 조건을 다시 따지게 한다.
        /// </summary>
        public void SetInputMoving(bool moving)
        {
            if (IsRemoved) moving = false;
            if (IsInputMoving == moving) return;
            IsInputMoving = moving;
            Manager?.NotifyInputMovingChanged(this);
        }

        // 지금 모습의 바디 타입(프리팹 기준)을 적용한다. 나는 중이거나 조종받는 중에는 Static 모습이어도 Dynamic으로 둔다
        internal void ApplyBodyType()
        {
            var type = _physics.Fly.IsFlying || _controlled ? RigidbodyType2D.Dynamic : _bodyType;
            if (_rigidbody.bodyType == type) return;
            _rigidbody.bodyType = type;
            // Static으로 굳는 순간 남아 있던 속도 때문에 튀지 않게 한다
            if (type == RigidbodyType2D.Dynamic) _rigidbody.linearVelocity = Vector2.zero;
        }

        internal void ApplyGravity() => _rigidbody.gravityScale = Gravity;

        // 날거나 조종받는 동안은 굴러가지 않게 회전을 막고, 끝나면 모습 설정대로 되돌린다
        internal void ApplyRotationLock() => _rigidbody.freezeRotation = _baseFreezeRotation || _physics.Fly.IsFlying || _controlled;

        /// <summary>
        /// 조종(Control): 켜면 조작 스크립트(PlatformerPlayer)를 붙여 플레이어가 움직이게 하고, 끄면 그 스크립트를 꺼 둔다.
        /// Static 대상(벽, 출구)도 조종받는 동안은 Dynamic으로 풀리고, 중력이 없는 모습이면 조종용 중력을 쓴다.
        /// </summary>
        public void SetControl(bool on)
        {
            if (_controlled == on) return;
            _controlled = on;
            if (_player == null) _player = GetComponent<PlatformerPlayer>();
            if (on)
            {
                ApplyBodyType();
                ApplyRotationLock();
                ApplyGravity();
                if (_player == null) _player = gameObject.AddComponent<PlatformerPlayer>();
                _player.enabled = true;
                return;
            }

            if (_player != null) _player.enabled = false;
            InputOverride = false;
            SetInputMoving(false);
            _physics.Motor.SetSelfDriven(false);
            ApplyGravity();
            ApplyRotationLock();
            ApplyBodyType();
        }


        /// <summary>
        /// 화면에서 숨기고 충돌·물리를 꺼서 더 이상 상호작용하지 않게 한다. Die 문장이 쓴다.
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
            RuleManager.Trace($"Remove {name} id={Id} pos={transform.position}"); // TEMP TRACE
            _contacts.ClearTreaders(); // 사라지면 아무도 올라서 있을 수 없다
            _contacts.ClearTouching(); // 사라지면 아무것에도 닿아 있지 않다
            ContactsInvalidated?.Invoke(this);
        }

        // 사망 상태를 풀고 다시 보이게, 부딪히게 한다. 변신으로 살아날 때만 쓴다
        internal void Revive()
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
            _physics.Move.SetRandomMove(false);
            _physics.Fly.SetFly(false);
        }

        // 닿은 상대를 RuleManager에 알리기만 한다. 무엇이 일어날지는 RuleManager가 태그를 보고 정한다
        private void OnTriggerEnter2D(Collider2D other)
        {
            _contacts.ReportContact(other);
            _contacts.UpdateTouch(other, true);
        }

        private void OnTriggerExit2D(Collider2D other) => _contacts.UpdateTouch(other, false);

        private void OnCollisionEnter2D(Collision2D collision)
        {
            _contacts.ReportContact(collision.collider);
            _contacts.UpdateTread(collision, true);
            _contacts.UpdateTouch(collision.collider, true);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            _contacts.UpdateTread(collision, false);
            _contacts.UpdateTouch(collision.collider, false);
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
            _physics.Step();
        }

}
}
