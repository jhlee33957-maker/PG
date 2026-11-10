using UnityEngine;
using UnityEngine.InputSystem;
using CoffeeSpec.Rules.Entities;
using CoffeeSpec.UI;

namespace CoffeeSpec.Player
{
    /// <summary>
    /// 2D 플랫포머 플레이어. 좌우 이동(A/D, ←/→), 점프(Z 또는 ↑).
    /// 프리팹에 직접 붙이지 않는다. 문장으로 조종(Control)을 받은 RuleEntity가 붙이고, 조종이 빠지면 꺼 둔다.
    /// 인스펙터 기본값은 아래 코드 값이 그대로 쓰인다.
    /// 좌우 이동, 점프(뜬 동안) 중에는 "움직이는 상태"라서 규칙의 "움직이면" 조건에 걸린다.
    /// 예전의 X 공격과 C 대쉬는 퍼즐 조작을 단순하게 하려고 뺐다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(RuleEntity))]
    public class PlatformerPlayer : MonoBehaviour
    {
        [Header("이동")]
        [SerializeField] private float _moveSpeed = 6f;
        [SerializeField] private float _groundAcceleration = 80f;
        [SerializeField] private float _airAcceleration = 50f;

        [Header("점프 (Z / ↑)")]
        [Tooltip("점프 시작 속도. 높이는 속도의 제곱에 비례한다")]
        [SerializeField] private float _jumpSpeed = 10.4f;
        [Tooltip("발판에서 떨어진 직후에도 점프를 받아주는 시간(초)")]
        [SerializeField] private float _coyoteTime = 0.1f;
        [Tooltip("착지 직전에 누른 점프를 기억하는 시간(초)")]
        [SerializeField] private float _jumpBufferTime = 0.1f;
        [Tooltip("점프 키를 일찍 떼면 상승 속도에 곱하는 값. 짧게 누르면 낮게 뛴다")]
        [SerializeField, Range(0f, 1f)] private float _jumpCutMultiplier = 0.5f;
        [SerializeField] private float _groundCheckDistance = 0.05f;

        private Rigidbody2D _rigidbody;
        private Collider2D _collider;
        private RuleEntity _ruleEntity;
        private SpriteRenderer _renderer;
        private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];

        private float _inputX;
        private int _facing = 1;
        private bool _grounded;
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private bool _jumpCutRequested;
        private bool _jumpActive;           // 점프해서 뜬 뒤 다시 땅에 내려올 때까지

        public int Facing => _facing;
        public bool IsGrounded => _grounded;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();
            _ruleEntity = GetComponent<RuleEntity>();
            _renderer = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            // 기획서가 열려 있으면(미리보기로 시간이 흐르는 중 포함) 입력을 받지 않는다.
            // 이전 입력이 남아 계속 움직이지 않도록 입력 상태도 비운다
            if (SpecPanel.IsAnyOpen || Time.timeScale == 0f)
            {
                _inputX = 0f;
                _jumpBufferTimer = 0f;
                _jumpCutRequested = false;
                _ruleEntity.InputOverride = false;
                _ruleEntity.SetInputMoving(false);
                return;
            }

            float x = 0f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            _inputX = x;
            if (x != 0f) SetFacing((int)x);
            // 조종받지 않는 동안 Move가 그림을 뒤집었을 수 있다. 보이는 쪽과 바라보는 방향이 어긋나지 않게 그림 방향을 따른다
            else if (_renderer != null) _facing = _renderer.flipX ? -1 : 1;

            // Z와 ↑ 둘 다 점프. 둘 중 하나를 아직 누르고 있으면 낮은 점프로 끊지 않는다
            bool jumpPressed = kb.zKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
            bool jumpReleased = kb.zKey.wasReleasedThisFrame || kb.upArrowKey.wasReleasedThisFrame;
            bool jumpHeld = kb.zKey.isPressed || kb.upArrowKey.isPressed;
            if (jumpPressed) _jumpBufferTimer = _jumpBufferTime;
            if (jumpReleased && !jumpHeld) _jumpCutRequested = true;
            // 입력이 있을 때만 Move 이동을 눌러서, 손을 떼면 규칙 이동이 다시 이어지게 한다
            _ruleEntity.InputOverride = x != 0f;

            // 타이머는 입력 타이밍에 맞춰 Update에서 줄인다
            _jumpBufferTimer -= Time.deltaTime;

            // 상태가 바뀔 때만 RuleEntity가 규칙에 알린다 (매 프레임 다시 계산하지 않는다)
            _ruleEntity.SetInputMoving(IsActing);
        }

        // 좌우 이동, 점프 중 하나라도 하고 있으면 움직이는 상태
        private bool IsActing => _inputX != 0f || _jumpActive;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            // 직접 움직일 때만 가로 잠금을 푼다. 속도를 넣기 전에 알려야 이번 프레임부터 움직인다
            _ruleEntity.SetSelfDriven(_inputX != 0f);
            UpdateGrounded();

            if (_grounded) _coyoteTimer = _coyoteTime;
            else _coyoteTimer -= dt;
            // 점프 후 땅에 다시 내려오면 점프 상태가 끝난다 (뛰어오르는 첫 프레임은 아직 위로 가는 중이라 안 끝난다)
            if (_jumpActive && _grounded && _rigidbody.linearVelocity.y <= 0.01f) _jumpActive = false;

            // 규칙(Move)으로 이동 중이어도 입력이 있으면 플레이어가 우선한다. 입력이 없을 때만 RuleEntity에 맡긴다
            if (!_ruleEntity.IsRandomMoving || _inputX != 0f)
            {
                float target = _inputX * _moveSpeed;
                float accel = _grounded ? _groundAcceleration : _airAcceleration;
                var v = _rigidbody.linearVelocity;
                v.x = Mathf.MoveTowards(v.x, target, accel * dt);
                _rigidbody.linearVelocity = v;
            }

            HandleJump();
            _ruleEntity.BlockBySolids();
        }

        private void UpdateGrounded()
        {
            // 레이어 설정 없이도 동작하도록 내 콜라이더를 아래로 살짝 쏴서 바닥을 찾는다
            int count = _collider.Cast(Vector2.down, _groundHits, _groundCheckDistance);
            _grounded = false;
            for (int i = 0; i < count; i++)
            {
                // 쌓인 벽 블록의 이음매 윗면은 바닥이 아니다 (벽 타고 오르기 방지)
                if (RuleEntity.IsGroundHit(_groundHits[i], _collider)) { _grounded = true; break; }
            }
        }

        private void HandleJump()
        {
            // 규칙으로 나는 중에는 높이를 RuleEntity가 정하므로 점프하지 않는다
            if (_ruleEntity.IsFlying)
            {
                _jumpBufferTimer = 0f;
                _jumpCutRequested = false;
                return;
            }

            var v = _rigidbody.linearVelocity;
            if (_jumpBufferTimer > 0f && _coyoteTimer > 0f)
            {
                v.y = _jumpSpeed;
                _jumpActive = true;
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

        private void SetFacing(int dir)
        {
            _facing = dir > 0 ? 1 : -1;
            if (_renderer != null) _renderer.flipX = _facing < 0;
        }

        private void OnDisable()
        {
            _inputX = 0f;
            _jumpActive = false;
        }
    }
}
