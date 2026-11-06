using CoffeeSpec.Player;
using CoffeeSpec.UI;
using UnityEngine;

namespace CoffeeSpec.Cameras
{
    /// <summary>
    /// 메인 카메라에 붙인다. 플레이 중에는 플레이어를 부드럽게 따라가고,
    /// 기획서(SpecPanel)가 열려 있으면 씬에 배치된 원래 위치로 돌아가 맵 전체를 비춘다.
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

        private Vector3 _homePosition;
        private Vector3 _velocity;
        private Vector3 _smoothPosition; // 픽셀에 맞추기 전의 부드러운 위치
        private Transform _player;
        private SpecPanel _specPanel;

        private void Awake()
        {
            // 씬에 놓인 카메라 위치가 기획서 화면용 위치다
            _homePosition = transform.position;
            _smoothPosition = _homePosition;
        }

        private void LateUpdate()
        {
            if (_specPanel == null) _specPanel = FindAnyObjectByType<SpecPanel>();
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

        // 카메라가 픽셀 사이에 서면 타일마다 반올림이 달라져서 화면 전체가 일렁인다. 픽셀 격자에 맞춰 세운다
        private Vector3 SnapToPixel(Vector3 position)
        {
            if (_pixelsPerUnit <= 0) return position;
            float ppu = _pixelsPerUnit;
            return new Vector3(Mathf.Round(position.x * ppu) / ppu, Mathf.Round(position.y * ppu) / ppu, position.z);
        }
    }
}