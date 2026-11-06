using UnityEngine;
using UnityEngine.InputSystem;
using CoffeeSpec.Entities;

namespace CoffeeSpec.Player
{
    /// <summary>
    /// 2D 플랫포머 플레이어. 좌우 이동(A/D, ←/→), Z 점프, X 공격, C 대쉬.
    /// 규칙 대상이 되도록 RuleEntity와 같이 붙여서 쓴다(태그는 RuleEntity에서 Person으로 지정).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(RuleEntity))]
    public class PlatformerPlayer : MonoBehaviour
    {
        [Header("이동")]
        [SerializeField] private float _moveSpeed = 6f;
        [SerializeField] private float _groundAcceleration = 80f;
        [SerializeField] private float _airAcceleration = 50f;

        [Header("점프 (Z)")]
        [Tooltip("점프 시작 속도. 높이는 속도의 제곱에 비례한다")]
        [SerializeField] private float _jumpSpeed = 8.5f;
        [Tooltip("발판에서 떨어진 직후에도 점프를 받아주는 시간(초)")]
        [SerializeField] private float _coyoteTime = 0.1f;
        [Tooltip("착지 직전에 누른 점프를 기억하는 시간(초)")]
        [SerializeField] private float _jumpBufferTime = 0.1f;
        [Tooltip("점프 키를 일찍 떼면 상승 속도에 곱하는 값. 짧게 누르면 낮게 뛴다")]
        [SerializeField, Range(0f, 1f)] private float _jumpCutMultiplier = 0.5f;
        [SerializeField] private float _groundCheckDistance = 0.05f;

        [Header("대쉬 (C)")]
        [SerializeField] private float _dashSpeed = 16f;
        [SerializeField] private float _dashDuration = 0.12f;
        [SerializeField] private float _dashCooldown = 0.4f;
        [Tooltip("C를 누르고 있는 동안 이동 속도에 곱하는 값")]
        [SerializeField] private float _sprintMultiplier = 1.4f;

        [Header("공격 (X)")]
        [SerializeField] private Vector2 _attackSize = new(1f, 0.8f);
        [Tooltip("몸 중심에서 바라보는 방향으로 얼마나 떨어진 곳에 판정을 만들지")]
        [SerializeField] private float _attackOffset = 0.8f;
        [SerializeField] private float _attackDuration = 0.1f;
        [SerializeField] private float _attackCooldown = 0.3f;

        private Rigidbody2D _rigidbody;
        private Collider2D _collider;
        private RuleEntity _ruleEntity;
        private SpriteRenderer _renderer;
        private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];

        private float _inputX;
        private bool _sprintHeld;
        private int _facing = 1;
        private bool _grounded;
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private bool _jumpCutRequested;

        private bool _dashRequested;
        private float _dashTimer;
        private float _dashCooldownTimer;
        private bool _airDashAvailable = true;
        private float _defaultGravity;

        private float _attackCooldownTimer;

        public int Facing => _facing;
        public bool IsGrounded => _grounded;
        public bool IsDashing => _dashTimer > 0f;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();
            _ruleEntity = GetComponent<RuleEntity>();
            _renderer = GetComponent<SpriteRenderer>();
            _defaultGravity = _rigidbody.gravityScale;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            float x = 0f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            _inputX = x;
            if (x != 0f && !IsDashing) SetFacing((int)x);

            if (kb.zKey.wasPressedThisFrame) _jumpBufferTimer = _jumpBufferTime;
            if (kb.zKey.wasReleasedThisFrame) _jumpCutRequested = true;
            if (kb.cKey.wasPressedThisFrame) _dashRequested = true;
            _sprintHeld = kb.cKey.isPressed;
            // 입력이 있을 때만 랜덤 이동을 눌러서, 손을 떼면 규칙 이동이 다시 이어지게 한다
            _ruleEntity.InputOverride = x != 0f;
            if (kb.xKey.wasPressedThisFrame) TryAttack();

            // 타이머는 입력 타이밍에 맞춰 Update에서 줄인다
            _jumpBufferTimer -= Time.deltaTime;
            _dashCooldownTimer -= Time.deltaTime;
            _attackCooldownTimer -= Time.deltaTime;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            UpdateGrounded();

            if (_grounded) { _coyoteTimer = _coyoteTime; _airDashAvailable = true; }
            else _coyoteTimer -= dt;

            if (UpdateDash(dt)) return;

            // 규칙으로 랜덤 이동 중이어도 입력이 있으면 플레이어가 우선한다. 입력이 없을 때만 RuleEntity에 맡긴다
            if (!_ruleEntity.IsRandomMoving || _inputX != 0f)
            {
                float speed = _sprintHeld ? _moveSpeed * _sprintMultiplier : _moveSpeed;
                float target = _inputX * speed;
                float accel = _grounded ? _groundAcceleration : _airAcceleration;
                var v = _rigidbody.linearVelocity;
                v.x = Mathf.MoveTowards(v.x, target, accel * dt);
                _rigidbody.linearVelocity = v;
            }

            HandleJump();
        }

        private void UpdateGrounded()
        {
            // 레이어 설정 없이도 동작하도록 내 콜라이더를 아래로 살짝 쏴서 바닥을 찾는다
            int count = _collider.Cast(Vector2.down, _groundHits, _groundCheckDistance);
            _grounded = false;
            for (int i = 0; i < count; i++)
            {
                if (_groundHits[i].collider.isTrigger) continue;
                if (_groundHits[i].normal.y > 0.5f) { _grounded = true; break; }
            }
        }

        private void HandleJump()
        {
            var v = _rigidbody.linearVelocity;
            if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
            {
                v.y = _jumpSpeed;
                // 점프 직후에 바닥으로 다시 끌어당기지 않게 한다
                _ruleEntity.BlockGroundSnap(0.15f);
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
                _jumpCutRequested = false;
            }
            else if (_jumpCutRequested)
            {
                if (v.y > 0f) v.y *= _jumpCutMultiplier;
                _jumpCutRequested = false;
            }
            _rigidbody.linearVelocity = v;
        }

        /// <returns>대쉬 중이면 true (일반 이동을 건너뛴다)</returns>
        private bool UpdateDash(float dt)
        {
            if (_dashRequested)
            {
                _dashRequested = false;
                bool canDash = _dashCooldownTimer <= 0f && (_grounded || _airDashAvailable);
                if (canDash)
                {
                    _dashTimer = _dashDuration;
                    _dashCooldownTimer = _dashCooldown;
                    if (!_grounded) _airDashAvailable = false;
                    // 대쉬 중에는 수평으로 곧게 나가도록 중력을 끈다
                    _rigidbody.gravityScale = 0f;
                }
            }

            if (_dashTimer <= 0f) return false;

            _rigidbody.linearVelocity = new Vector2(_facing * _dashSpeed, 0f);
            _dashTimer -= dt;
            if (_dashTimer <= 0f)
            {
                _rigidbody.gravityScale = _defaultGravity;
                // 대쉬가 끝나면 이동 속도로 줄여서 미끄러지지 않게 한다
                _rigidbody.linearVelocity = new Vector2(_facing * _moveSpeed, 0f);
            }
            return true;
        }

        private void TryAttack()
        {
            if (_attackCooldownTimer > 0f) return;
            _attackCooldownTimer = _attackCooldown;

            var go = new GameObject("AttackHitbox");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(_facing * _attackOffset, 0f, 0f);
            go.layer = gameObject.layer;

            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = _attackSize;

            var hitbox = go.AddComponent<AttackHitbox>();
            hitbox.Init(gameObject, _attackDuration);
        }

        private void SetFacing(int dir)
        {
            _facing = dir > 0 ? 1 : -1;
            if (_renderer != null) _renderer.flipX = _facing < 0;
        }

        private void OnDisable()
        {
            if (_rigidbody != null) _rigidbody.gravityScale = _defaultGravity;
            _dashTimer = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            // 스프라이트가 없어도 공격 범위를 씬 뷰에서 볼 수 있게 한다
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            var center = transform.position + new Vector3(_facing * _attackOffset, 0f, 0f);
            Gizmos.DrawWireCube(center, _attackSize);
        }
    }
}