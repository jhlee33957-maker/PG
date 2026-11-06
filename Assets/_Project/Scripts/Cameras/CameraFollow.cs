using CoffeeSpec.Player;
using CoffeeSpec.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CoffeeSpec.Cameras
{
    /// <summary>
    /// 메인 카메라에 붙인다. 플레이 중에는 플레이어를 부드럽게 따라가고,
    /// 기획서(SpecPanel)가 열려 있으면 씬에 배치된 원래 위치로 돌아가 맵 전체를 비춘다.
    /// 기획서를 BTN_Close로 접으면 마우스 드래그로 맵을 둘러볼 수 있다.
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

        [Header("스테이지 보기 (드래그)")]
        [Tooltip("켜면 드래그로 레벨 밖까지 나가지 못하게 막는다")]
        [SerializeField] private bool _clampToLevel = true;
        [Tooltip("드래그 범위를 계산할 레벨 루트 오브젝트 이름")]
        [SerializeField] private string _levelRootName = "Level";

        private Camera _camera;
        private Vector3 _homePosition;
        private Vector3 _velocity;
        private Vector3 _smoothPosition; // 픽셀에 맞추기 전의 부드러운 위치
        private Transform _player;
        private SpecPanel _specPanel;

        // 드래그 상태
        private bool _wasViewingStage;
        private bool _panning;
        private Vector2 _lastMousePosition;
        private bool _hasPanBounds;
        private Bounds _panBounds;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            // 씬에 놓인 카메라 위치가 기획서 화면용 위치다
            _homePosition = transform.position;
            _smoothPosition = _homePosition;
        }

        private void LateUpdate()
        {
            if (_specPanel == null) _specPanel = FindAnyObjectByType<SpecPanel>();

            bool viewingStage = _specPanel != null && _specPanel.IsViewingStage;
            if (viewingStage && !_wasViewingStage)
            {
                // 리트로 레벨이 새로 만들어질 수 있어서 들어갈 때마다 범위를 다시 잰다
                RecalculatePanBounds();
            }
            _wasViewingStage = viewingStage;

            if (viewingStage)
            {
                UpdatePan();
                return;
            }
            _panning = false;

            bool showHome = _specPanel != null && _specPanel.IsOpen;

            Vector3 target;
            float smoothTime;
            if (showHome)
            {
                target = _homePosition;
                smoothTime = _returnSmoothTime;
            }
            else
            {
                if (_player == null)
                {
                    var player = FindAnyObjectByType<PlatformerPlayer>();
                    if (player == null) return;
                    _player = player.transform;
                }
                target = new Vector3(_player.position.x + _offset.x, _player.position.y + _offset.y, _homePosition.z);
                smoothTime = _followSmoothTime;
            }

            // 기획서가 열려 있으면 timeScale이 0이라 실제 시간으로 움직인다
            _smoothPosition = Vector3.SmoothDamp(_smoothPosition, target, ref _velocity,
                smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            transform.position = SnapToPixel(_smoothPosition);
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

            _smoothPosition = ClampToPanBounds(_smoothPosition);
            _velocity = Vector3.zero;
            transform.position = SnapToPixel(_smoothPosition);
        }

        private static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        /// <summary>레벨 아래 모든 렌더러를 감싸는 범위. 원래 화면이 레벨보다 넓으면 그 화면까지 포함한다.</summary>
        private void RecalculatePanBounds()
        {
            _hasPanBounds = false;
            if (!_clampToLevel) return;

            GameObject level = GameObject.Find(_levelRootName);
            if (level == null) return;

            Renderer[] renderers = level.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            // 원래 위치에서 보이던 화면은 항상 갈 수 있게 해서, 드래그를 시작하자마자 화면이 튀지 않게 한다
            Vector2 half = GetHalfViewSize();
            bounds.Encapsulate(new Vector3(_homePosition.x - half.x, _homePosition.y - half.y, 0f));
            bounds.Encapsulate(new Vector3(_homePosition.x + half.x, _homePosition.y + half.y, 0f));

            _panBounds = bounds;
            _hasPanBounds = true;
        }

        // 화면 가장자리가 범위를 넘지 않도록 카메라 중심을 가둔다. 범위가 화면보다 좁은 축은 가운데에 둔다
        private Vector3 ClampToPanBounds(Vector3 position)
        {
            if (!_hasPanBounds) return position;

            Vector2 half = GetHalfViewSize();
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
            float halfHeight = _camera.orthographicSize;
            return new Vector2(halfHeight * _camera.aspect, halfHeight);
        }

        // 카메라가 픽셀 사이에 서면 타일마다 반올림이 달라져서 화면 전체가 일렁인다. 픽셀 격자에 맞춰 세운다
        private Vector3 SnapToPixel(Vector3 position)
        {
            if (_pixelsPerUnit <= 0) return position;
            float ppu = _pixelsPerUnit;
            return new Vector3(Mathf.Round(position.x * ppu) / ppu, Mathf.Round(position.y * ppu) / ppu, position.z);
        }
    }
}