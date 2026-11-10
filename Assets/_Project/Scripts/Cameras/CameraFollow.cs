using CoffeeSpec.Rules.Entities;
using CoffeeSpec.Rules;
using CoffeeSpec.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace CoffeeSpec.Cameras
{
    /// <summary>
    /// 메인 카메라에 붙인다. 플레이 중에는 조종 대상(Control 문장을 받은 대상)들의 한가운데를 부드럽게 따라가고,
    /// 조종 대상이 여럿이면 모두 화면에 들어오도록 카메라를 넓힌다(줌아웃). 다 모이면 원래 크기로 돌아온다.
    /// 기획서(SpecPanel)가 열려 있으면 씬에 배치된 원래 위치로 돌아가 맵 전체를 비춘다.
    /// 기획서를 BTN_Close로 접으면 마우스 드래그나 WASD/방향키로 맵을 둘러볼 수 있다.
    /// 플레이어와 기획서는 씬에서 알아서 찾는다 (리트로 플레이어가 새로 생겨도 다시 찾는다).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        [Tooltip("플레이어 위치에서 얼마나 떨어진 곳을 볼지. 위쪽을 더 보이게 하려면 y를 올린다")]
        [SerializeField] private Vector2 _offset = new(0f, 1f);
        [Tooltip("따라가는 데 걸리는 시간(초). 작을수록 바짝 따라간다")]
        [SerializeField, Min(0f)] private float _followSmoothTime = 0.15f;
        [Tooltip("기획서가 열릴 때 원래 위치로 돌아가는 데 걸리는 시간(초)")]
        [SerializeField, Min(0f)] private float _returnSmoothTime = 0.3f;
        [Tooltip("스프라이트의 Pixels Per Unit. 카메라를 이 픽셀 격자에 맞춰 세워서 화면이 일렁이지 않게 한다. 0이면 맞추지 않는다")]
        [SerializeField, Min(0)] private int _pixelsPerUnit = 32;

        [Header("여러 조종 대상 (줌)")]
        [Tooltip("조종 대상들을 감싼 범위 바깥에 남길 여백(칸). x는 좌우, y는 위아래")]
        [SerializeField] private Vector2 _groupPadding = new(3f, 2f);
        [Tooltip("넓혀도 이 크기(orthographicSize)보다 커지지 않는다. 0이면 제한 없음")]
        [SerializeField, Min(0f)] private float _maxOrthographicSize = 20f;
        [Tooltip("카메라 크기가 바뀌는 데 걸리는 시간(초)")]
        [SerializeField, Min(0f)] private float _zoomSmoothTime = 0.3f;

        [Header("스테이지 보기 (드래그 / 키보드)")]
        [Tooltip("WASD/방향키로 움직이는 속도 (초당 월드 유닛)")]
        [SerializeField, Min(0f)] private float _keyboardPanSpeed = 10f;
        [Tooltip("Shift를 누르고 있을 때 속도 배수")]
        [SerializeField, Min(1f)] private float _keyboardFastMultiplier = 2f;
        [Tooltip("켜면 카메라(따라가기·드래그·둘러보기 모두)가 레벨 가장자리 프리팹이 화면 끝에 닿는 곳까지만 움직인다")]
        [SerializeField] private bool _clampToLevel = true;
        [Tooltip("드래그 범위를 계산할 레벨 루트 오브젝트 이름")]
        [SerializeField] private string _levelRootName = "Level";

        private Camera _camera;
        private Vector3 _homePosition;
        private Vector3 _velocity;
        private Vector3 _smoothPosition; // 픽셀에 맞추기 전의 부드러운 위치
        private RuleManager _ruleManager;
        private float _homeSize;       // 씬에 놓인 카메라 크기. 조종 대상이 하나일 때와 기획서 화면에서 쓴다
        private float _sizeVelocity;
        private SpecPanel _specPanel;
        private bool _searchedSpecPanel;

        // 드래그 상태
        private bool _wasViewingStage;
        private bool _panning;
        private Vector2 _lastMousePosition;
        private bool _hasPanBounds;
        private Bounds _panBounds;
        private GameObject _boundsLevel;   // 범위를 잰 레벨 루트. 리트로 레벨이 새로 만들어지면 null이 되어 다시 잰다
        private float _nextLevelSearchTime; // 레벨 루트가 없는 씬에서 매 프레임 Find하지 않게 한다

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            // 씬에 놓인 카메라 위치가 기획서 화면용 위치다
            _homePosition = transform.position;
            _smoothPosition = _homePosition;
            _homeSize = _camera.orthographicSize;
        }

        private void LateUpdate()
        {
            if (!_searchedSpecPanel)
            {
                _searchedSpecPanel = true;
                _specPanel = FindAnyObjectByType<SpecPanel>();
            }

            bool viewingStage = _specPanel != null && _specPanel.IsViewingStage;
            if (viewingStage && !_wasViewingStage)
            {
                // 리트로 레벨이 새로 만들어질 수 있어서 들어갈 때마다 범위를 다시 잰다
                RecalculatePanBounds();
            }
            _wasViewingStage = viewingStage;

            if (viewingStage)
            {
                // 둘러보기 범위는 원래 화면 크기 기준이라 크기부터 되돌린다
                _camera.orthographicSize = _homeSize;
                _sizeVelocity = 0f;
                UpdatePan();
                return;
            }
            _panning = false;

            bool showHome = _specPanel != null && _specPanel.IsOpen;

            Vector3 target;
            float smoothTime;
            float targetSize = _homeSize;
            if (showHome)
            {
                // 기획서 화면도 벽 바깥을 비추지 않게 배치한 자리를 레벨 범위 안으로 가둔다
                EnsurePanBounds();
                target = ClampToPanBounds(_homePosition);
                smoothTime = _returnSmoothTime;
            }
            else
            {
                if (!TryGetControlledBounds(out Bounds group))
                {
                    UpdateFreeLook();
                    return;
                }
                _freeLooking = false;
                target = new Vector3(group.center.x + _offset.x, group.center.y + _offset.y, _homePosition.z);
                // 따라가기도 레벨 밖을 비추지 않게 목표 자리부터 가둔다
                EnsurePanBounds();
                targetSize = SizeToFit(group);
                target = ClampToPanBounds(target);
                // 조종 대상이 사라지면 이 자리에서 둘러보기를 시작한다
                _lastFollowTarget = target;
                _hasLastFollowTarget = true;
                smoothTime = _followSmoothTime;
            }

            _camera.orthographicSize = Mathf.SmoothDamp(_camera.orthographicSize, targetSize, ref _sizeVelocity,
                _zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

            // 기획서가 열려 있으면 timeScale이 0이라 실제 시간으로 움직인다
            _smoothPosition = Vector3.SmoothDamp(_smoothPosition, target, ref _velocity,
                smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            // 줌 중에는 화면 크기가 바뀌므로 지금 크기 기준으로 한 번 더 가둔다. 기획서 화면으로 돌아가는 중에도 벽 바깥이 보이지 않게 한다
            _smoothPosition = ClampToPanBounds(_smoothPosition);
            transform.position = SnapToPixel(_smoothPosition);
        }

        private void EnsurePanBounds()
        {
            if (_hasPanBounds && _boundsLevel != null) return;
            if (Time.unscaledTime < _nextLevelSearchTime) return;
            _nextLevelSearchTime = Time.unscaledTime + 1f;
            RecalculatePanBounds();
        }

        // 조종할 수 있는 대상이 없으면(모두 죽었을 때 등) 방향키로 카메라를 움직여 맵을 둘러본다
        private bool _freeLooking;
        private bool _freeLookMoved;
        private Vector3 _lastFollowTarget;
        private bool _hasLastFollowTarget;
        private void UpdateFreeLook()
        {
            if (!_freeLooking)
            {
                _freeLooking = true;
                _freeLookMoved = false;
                RecalculatePanBounds();
                // 카메라는 살짝 늦게 따라가므로, 죽는 순간 멈추면 캐릭터가 화면 중앙이 아니다.
                // 마지막으로 비추려던 자리까지는 이어서 간다. 그 자리는 따라가기 때 이미 범위 안으로 가둬 둔 곳이다
            }

            Vector3 pan = ReadKeyboardPan();
            if (pan != Vector3.zero) _freeLookMoved = true;

            if (!_freeLookMoved && _hasLastFollowTarget)
            {
                // 키를 누르기 전까지는 원래 따라가던 방식 그대로 마지막 자리로 다가간다
                _smoothPosition = Vector3.SmoothDamp(_smoothPosition, _lastFollowTarget, ref _velocity,
                    _followSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
                _smoothPosition = ClampToPanBounds(_smoothPosition);
            }
            else
            {
                _velocity = Vector3.zero;
                _smoothPosition += pan * Time.unscaledDeltaTime;
                _smoothPosition = ClampToPanBounds(_smoothPosition);
            }
            transform.position = SnapToPixel(_smoothPosition);
        }

        // 조종 대상 전부의 위치를 감싸는 범위. 하나도 없으면 false (카메라를 그대로 둔다)
        private bool TryGetControlledBounds(out Bounds bounds)
        {
            bounds = default;
            if (_ruleManager == null) _ruleManager = RuleManager.Instance;
            if (_ruleManager == null) return false;

            bool any = false;
            foreach (var e in _ruleManager.GetControlled())
            {
                Vector3 p = e.transform.position;
                if (!any) { bounds = new Bounds(new Vector3(p.x, p.y, 0f), Vector3.zero); any = true; }
                else bounds.Encapsulate(new Vector3(p.x, p.y, 0f));
            }
            return any;
        }

        // 범위와 여백이 화면 안에 다 들어오는 카메라 크기. 원래 크기보다 작아지지는 않는다
        private float SizeToFit(Bounds group)
        {
            float halfHeight = group.extents.y + _groupPadding.y + Mathf.Abs(_offset.y);
            float halfWidth = group.extents.x + _groupPadding.x + Mathf.Abs(_offset.x);
            float size = Mathf.Max(_homeSize, halfHeight, halfWidth / Mathf.Max(0.01f, _camera.aspect));
            // 레벨 전체가 다 보이는 크기보다 더 넓히면 레벨 밖만 더 보이므로 거기서 멈춘다 (원래 크기보다는 줄이지 않는다)
            if (_hasPanBounds)
            {
                float fitLevel = Mathf.Max(_panBounds.extents.y, _panBounds.extents.x / Mathf.Max(0.01f, _camera.aspect));
                size = Mathf.Min(size, Mathf.Max(_homeSize, fitLevel));
            }
            return _maxOrthographicSize > 0f ? Mathf.Min(size, Mathf.Max(_homeSize, _maxOrthographicSize)) : size;
        }

        // ---------------- 스테이지 보기 드래그 ----------------

        private void UpdatePan()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 mousePosition = mouse.position.ReadValue();

            // 버튼 같은 UI 위에서 누른 건 드래그로 치지 않는다
            if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverUI())
            {
                _panning = true;
                _lastMousePosition = mousePosition;
            }
            if (!mouse.leftButton.isPressed)
            {
                _panning = false;
            }

            if (_panning)
            {
                // 잡은 지점이 마우스를 따라가도록 카메라는 반대로 움직인다
                Vector3 from = _camera.ScreenToWorldPoint(_lastMousePosition);
                Vector3 to = _camera.ScreenToWorldPoint(mousePosition);
                Vector3 delta = from - to;
                _smoothPosition += new Vector3(delta.x, delta.y, 0f);
                _lastMousePosition = mousePosition;
            }

            // 마우스로 끄는 중이 아니면 키보드로 움직인다 (미리보기 중에는 플레이어 입력이 막혀 있어 겹치지 않는다)
            if (!_panning)
            {
                _smoothPosition += ReadKeyboardPan() * Time.unscaledDeltaTime;
            }

            _smoothPosition = ClampToPanBounds(_smoothPosition);
            _velocity = Vector3.zero;
            transform.position = SnapToPixel(_smoothPosition);
        }

        // WASD/방향키 입력을 이번 프레임의 이동 속도로 바꾼다. 대각선도 같은 속도가 되게 정규화한다
        private Vector3 ReadKeyboardPan()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return Vector3.zero;

            Vector2 dir = Vector2.zero;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dir.x -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dir.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dir.y -= 1f;
            if (dir == Vector2.zero) return Vector3.zero;

            float speed = _keyboardPanSpeed;
            if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) speed *= _keyboardFastMultiplier;
            dir = dir.normalized * speed;
            return new Vector3(dir.x, dir.y, 0f);
        }

        private static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        /// <summary>레벨 아래 켜져 있는 모든 렌더러를 감싸는 범위. 네 방향 가장 바깥 프리팹의 끝이 곧 카메라가 비출 수 있는 끝이다.</summary>
        private void RecalculatePanBounds()
        {
            _hasPanBounds = false;
            if (!_clampToLevel) return;

            GameObject level = GameObject.Find(_levelRootName);
            if (level == null) return;

            Renderer[] renderers = level.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds bounds = default;
            foreach (var r in renderers)
            {
                // 죽어서 꺼진 대상 등 안 보이는 것은 범위에 넣지 않는다
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) return;

            // 예전에는 원래 카메라 화면까지 범위에 넣었지만, 그러면 레벨 밖이 보여서 뺐다.
            // 레벨이 화면보다 좁은 축은 ClampToPanBounds가 레벨 가운데에 세운다
            _panBounds = bounds;
            _hasPanBounds = true;
            _boundsLevel = level;
        }

        // 화면 가장자리가 범위를 넘지 않도록 카메라 중심을 가둔다. 범위가 화면보다 좁은 축은 가운데에 둔다
        private Vector3 ClampToPanBounds(Vector3 position)
        {
            if (!_hasPanBounds) return position;

            // 픽셀 격자에 맞출 때 반 픽셀까지 바깥으로 밀릴 수 있어서 그만큼 안쪽으로 더 가둔다
            Vector2 half = GetHalfViewSize();
            if (_pixelsPerUnit > 0) half += Vector2.one * (0.5f / _pixelsPerUnit);
            Vector3 min = _panBounds.min;
            Vector3 max = _panBounds.max;

            float x = max.x - min.x <= half.x * 2f
                ? _panBounds.center.x
                : Mathf.Clamp(position.x, min.x + half.x, max.x - half.x);
            float y = max.y - min.y <= half.y * 2f
                ? _panBounds.center.y
                : Mathf.Clamp(position.y, min.y + half.y, max.y - half.y);
            return new Vector3(x, y, position.z);
        }

        private Vector2 GetHalfViewSize()
        {
            // Pixel Perfect Camera가 있으면 렌더링 직전에 orthographicSize를 자기 계산값으로 덮어쓴다.
            // 그래서 우리가 넣은 크기(_homeSize)로 가두면 실제 보이는 화면이 더 넓어 벽 바깥이 살짝 보였다. 실제 보이는 크기로 잰다
            if (_pixelPerfect == null && !_searchedPixelPerfect)
            {
                _searchedPixelPerfect = true;
                _pixelPerfect = GetComponent<PixelPerfectCamera>();
            }
            if (_pixelPerfect != null && _pixelPerfect.isActiveAndEnabled && _pixelPerfect.pixelRatio > 0)
            {
                float ppu = _pixelPerfect.assetsPPU;
                float ratio = _pixelPerfect.pixelRatio;
                float width = _pixelPerfect.cropFrameX ? _pixelPerfect.refResolutionX : _camera.pixelWidth / ratio;
                float height = _pixelPerfect.cropFrameY ? _pixelPerfect.refResolutionY : _camera.pixelHeight / ratio;
                return new Vector2(width / ppu * 0.5f, height / ppu * 0.5f);
            }
            float halfHeight = _camera.orthographicSize;
            return new Vector2(halfHeight * _camera.aspect, halfHeight);
        }

        private PixelPerfectCamera _pixelPerfect;
        private bool _searchedPixelPerfect;

        // 카메라가 픽셀 사이에 서면 타일마다 반올림이 달라져서 화면 전체가 일렁인다. 픽셀 격자에 맞춰 세운다
        private Vector3 SnapToPixel(Vector3 position)
        {
            if (_pixelsPerUnit <= 0) return position;
            float ppu = _pixelsPerUnit;
            return new Vector3(Mathf.Round(position.x * ppu) / ppu, Mathf.Round(position.y * ppu) / ppu, position.z);
        }
    }
}
