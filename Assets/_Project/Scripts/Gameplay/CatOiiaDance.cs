using System.Collections;
using UnityEngine;

namespace CoffeeSpec.Gameplay
{
    /// <summary>
    /// 오이아이오 밈 춤: 식빵 고양이가 천천히 한 바퀴 돈 뒤 빠르게 빙글빙글 돌고 잠깐 쉰다.
    /// 도는 동안에는 몸통만 형광색으로 계속 바뀌고, 쉴 때는 원래 색으로 돌아온다.
    /// 빠르게 도는 동안에는 사방으로 날아다니며 벽(움직이지 않는 콜라이더)에 튕기고, 끝나면 제자리로 돌아온다.
    /// _frames에는 CatOiia_0 ~ CatOiia_7을 순서대로 넣는다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class CatOiiaDance : MonoBehaviour
    {
        [SerializeField] private Sprite[] _frames;
        [SerializeField] private bool _playOnStart = true;
        [SerializeField] private bool _loop = true;
        [SerializeField] private float _slowFrameTime = 0.11f;
        [SerializeField] private float _fastFrameTime = 0.035f;
        [SerializeField] private int _fastSpins = 6;
        [SerializeField] private float _restTime = 1f;
        [SerializeField] private float _hueStep = 0.13f;
        [Header("빠르게 돌 때 날아다니기")]
        [SerializeField] private bool _flyWhileFast = true;
        [SerializeField] private float _flySpeed = 12f;
        [SerializeField] private float _returnTime = 0.4f;
        [SerializeField] private LayerMask _bounceLayers = ~0;

        private static readonly Color32 BodyColor = new Color32(153, 186, 196, 255);

        private SpriteRenderer _renderer;
        private Sprite[] _masks;
        private Vector3 _baseLocalPos;
        private float _hue;
        private Coroutine _routine;
        private Vector2 _velocity;
        private bool _flying;
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[8];

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _baseLocalPos = transform.localPosition;
            _masks = BuildMasks(_frames);
            if (_frames != null && _frames.Length > 0) _renderer.sprite = _frames[0];
        }

        private void Start()
        {
            if (_playOnStart) Play();
        }

        public void Play()
        {
            if (_frames == null || _frames.Length == 0) return;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Dance());
        }

        public void Stop()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            ShowNormal(0);
        }

        private IEnumerator Dance()
        {
            do
            {
                ShowNormal(0);
                yield return new WaitForSeconds(_restTime * 0.5f);

                yield return Spin(_slowFrameTime);

                if (_flyWhileFast) StartFlying();
                for (int i = 0; i < _fastSpins; i++) yield return Spin(_fastFrameTime);
                _flying = false;
                if (_flyWhileFast) yield return ReturnHome();

                ShowNormal(0);
                yield return new WaitForSeconds(_restTime * 0.5f);
            } while (_loop);
            _routine = null;
        }

        private IEnumerator Spin(float frameTime)
        {
            for (int i = 0; i < _frames.Length; i++)
            {
                _hue = Mathf.Repeat(_hue + _hueStep, 1f);
                ShowNeon(i);
                yield return new WaitForSeconds(frameTime);
            }
        }

        private void StartFlying()
        {
            // 수평·수직에 너무 붙지 않는 위쪽 대각선 방향으로 출발한다.
            float deg = Random.Range(25f, 65f) + (Random.value < 0.5f ? 0f : 90f);
            _velocity = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * _flySpeed;
            _flying = true;
        }

        private void Update()
        {
            if (!_flying) return;
            float remaining = _velocity.magnitude * Time.deltaTime;
            for (int iter = 0; iter < 4 && remaining > 0f; iter++)
            {
                Vector2 dir = _velocity.normalized;
                if (TryFindWall(dir, remaining, out var hit))
                {
                    float move = Mathf.Max(0f, hit.distance - Skin);
                    transform.position += (Vector3)(dir * move);
                    remaining -= move;
                    _velocity = Vector2.Reflect(_velocity, hit.normal);
                    if (move <= 0f) transform.position += (Vector3)(hit.normal * Skin);
                }
                else
                {
                    transform.position += (Vector3)(dir * remaining);
                    remaining = 0f;
                }
            }
        }

        private const float Skin = 0.02f;

        // 고양이 몸 크기의 상자를 진행 방향으로 쏴서, 움직이지 않는 콜라이더(벽, 바닥)만 벽으로 본다.
        private bool TryFindWall(Vector2 dir, float distance, out RaycastHit2D wall)
        {
            var b = _renderer.bounds;
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = _bounceLayers };
            int count = Physics2D.BoxCast(b.center, (Vector2)b.size * 0.9f, 0f, dir, filter, _hits, distance + Skin);
            wall = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = _hits[i];
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.rigidbody != null && h.rigidbody.bodyType != RigidbodyType2D.Static) continue;
                if (Vector2.Dot(h.normal, dir) >= 0f) continue; // 이미 빠져나가는 중인 면은 무시
                if (h.distance < best) { best = h.distance; wall = h; }
            }
            return wall.collider != null;
        }

        private IEnumerator ReturnHome()
        {
            Vector3 from = transform.localPosition;
            for (float t = 0f; t < _returnTime; t += Time.deltaTime)
            {
                _hue = Mathf.Repeat(_hue + _hueStep * 0.5f, 1f);
                ShowNeon(0);
                transform.localPosition = Vector3.Lerp(from, _baseLocalPos, Mathf.SmoothStep(0f, 1f, t / _returnTime));
                yield return null;
            }
            transform.localPosition = _baseLocalPos;
        }

        private void ShowNormal(int i)
        {
            _flying = false;
            _renderer.sprite = _frames[i];
            _renderer.color = Color.white;
            transform.localPosition = _baseLocalPos;
        }

        private void ShowNeon(int i)
        {
            bool hasMask = _masks != null && i < _masks.Length && _masks[i] != null;
            _renderer.sprite = hasMask ? _masks[i] : _frames[i];
            _renderer.color = hasMask ? Color.HSVToRGB(_hue, 1f, 1f) : Color.white;
        }

        // 몸통 색 픽셀만 흰색으로 바꾼 복사본을 만든다. 흰색 위에 색을 곱하면 쨍한 형광색이 된다.
        private static Sprite[] BuildMasks(Sprite[] frames)
        {
            if (frames == null || frames.Length == 0 || frames[0] == null) return null;
            var source = frames[0].texture;
            if (!source.isReadable) return null;

            var pixels = source.GetPixels32();
            for (int p = 0; p < pixels.Length; p++)
            {
                var c = pixels[p];
                if (c.a > 0 && c.r == BodyColor.r && c.g == BodyColor.g && c.b == BodyColor.b)
                    pixels[p] = new Color32(255, 255, 255, c.a);
            }
            var mask = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = source.name + "_NeonMask"
            };
            mask.SetPixels32(pixels);
            mask.Apply(false, true);

            var result = new Sprite[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                var f = frames[i];
                if (f == null) continue;
                var pivot = new Vector2(f.pivot.x / f.rect.width, f.pivot.y / f.rect.height);
                result[i] = Sprite.Create(mask, f.rect, pivot, f.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
            return result;
        }
    }
}
