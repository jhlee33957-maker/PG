using UnityEngine;

namespace CoffeeSpec.Gameplay
{
    /// <summary>
    /// 스프라이트의 투명도를 천천히 오르내려 은은하게 빛나는 것처럼 보이게 한다.
    /// 배경 위에 겹친 빛 레이어(별, 창문 불빛, 달 후광)에 붙여 쓴다. 레이어마다 주기·박자를 다르게 두면 따로따로 반짝인다.
    /// 기획서를 열면 timeScale이 0이 되는데, 그동안에도 배경이 멈춰 보이지 않도록 기본은 실제 시간(unscaled)을 쓴다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteGlowPulse : MonoBehaviour
    {
        [Tooltip("가장 어두울 때 투명도")]
        [SerializeField, Range(0f, 1f)] private float _minAlpha = 0.15f;
        [Tooltip("가장 밝을 때 투명도")]
        [SerializeField, Range(0f, 1f)] private float _maxAlpha = 1f;
        [Tooltip("한 번 밝아졌다 어두워지는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.05f)] private float _period = 3f;
        [Tooltip("0~1. 같은 주기의 레이어끼리 박자를 엇갈리게 한다")]
        [SerializeField, Range(0f, 1f)] private float _phase;
        [Tooltip("켜면 시작할 때 박자를 무작위로 정한다")]
        [SerializeField] private bool _randomPhase;
        [Tooltip("0~0.9. 한 주기 중 다 켜진 채·다 꺼진 채로 머무는 비율. 높이면 창문 불처럼 한동안 켜져 있다가 천천히 꺼지고, 한동안 꺼져 있다가 천천히 켜진다")]
        [SerializeField, Range(0f, 0.9f)] private float _holdRatio;
        [Tooltip("1초마다 잠깐 꺼졌다 켜질 확률. 창문 불빛처럼 가끔 깜빡이게 할 때 쓴다 (0이면 안 깜빡임)")]
        [SerializeField, Range(0f, 1f)] private float _flickerChance;
        [Tooltip("깜빡일 때 꺼져 있는 시간(초)")]
        [SerializeField, Min(0.01f)] private float _flickerDuration = 0.08f;
        [Tooltip("켜면 기획서가 열려 시간이 멈춰도 계속 움직인다")]
        [SerializeField] private bool _useUnscaledTime = true;

        private SpriteRenderer _renderer;
        private float _offset;
        private float _flickerUntil = -1f;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _offset = _randomPhase ? Random.value : _phase;
        }

        private void Update()
        {
            float time = _useUnscaledTime ? Time.unscaledTime : Time.time;
            float delta = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            // 한 주기 = 서서히 켜짐 → 켜진 채 머묾 → 서서히 꺼짐 → 꺼진 채 머묾. 머무는 비율이 0이면 쉬지 않고 오르내린다
            float u = Mathf.Repeat(time / _period + _offset, 1f);
            float fade = (1f - _holdRatio) * 0.5f;
            float hold = _holdRatio * 0.5f;
            float wave;
            if (u < fade) wave = u / fade;
            else if (u < fade + hold) wave = 1f;
            else if (u < fade * 2f + hold) wave = 1f - (u - fade - hold) / fade;
            else wave = 0f;
            // smoothstep으로 켜지기 시작·끝날 때를 부드럽게 해 딱딱 끊겨 보이지 않게 한다
            wave = wave * wave * (3f - 2f * wave);
            float alpha = Mathf.Lerp(_minAlpha, _maxAlpha, wave);

            if (_flickerChance > 0f && time >= _flickerUntil && Random.value < _flickerChance * delta)
            {
                _flickerUntil = time + _flickerDuration;
            }
            if (time < _flickerUntil)
            {
                alpha = _minAlpha;
            }

            Color color = _renderer.color;
            color.a = alpha;
            _renderer.color = color;
        }
    }
}
