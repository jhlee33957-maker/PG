using System.Collections.Generic;
using CoffeeSpec.Core;
using UnityEngine;

namespace CoffeeSpec.Rules.Entities
{
    /// <summary>
    /// 지금 그려지는 그림이 어떤 시트(Sprite/Object의 PlayerCat, Pigeon)에서 왔는지 보고
    /// 땅에 서 있는지, 걷는지, 공중에 있는지에 맞춰 컷을 바꾼다.
    /// 변신하면 그림이 다른 시트로 바뀌므로 그 시트의 애니메이션을 따라가고, 시트가 없는 모습이면 손대지 않는다.
    /// 그림만 바꾸고 콜라이더 크기는 건드리지 않는다 (컷마다 크기가 같다).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntitySpriteAnimator : MonoBehaviour
    {
        private sealed class AnimSet
        {
            internal Sprite[] Idle, Walk, Rise, Fall, Fly;
            internal float IdleFrameTime, WalkFrameTime, FlyFrameTime;
            internal readonly HashSet<Sprite> All = new();
        }

        private enum Pose { Idle, Walk, Rise, Fall, Fly }

        private const int PixelsPerUnit = 32;
        // 이 속도보다 빠르게 옆으로 가면 걷는 것으로 본다 (유닛/초)
        private const float WalkSpeed = 0.15f;
        // 이 속도보다 빠르게 오르면 점프로 본다
        private const float RiseSpeed = 0.05f;
        // 바닥에서 잠깐 떨어진 것(턱, 착지 순간)은 공중으로 치지 않는다
        private const float AirGrace = 0.06f;
        private const float GroundCheckDistance = 0.06f;

        private static readonly Dictionary<string, AnimSet> Cache = new();
        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[8];

        private SpriteRenderer _renderer;
        private Rigidbody2D _rigidbody;
        private Collider2D _collider;
        private RuleEntity _entity;

        private AnimSet _set;
        private Pose _pose;
        private float _frameTime;
        private int _frame;
        private float _airTime;
        private Vector2 _lastPosition;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _rigidbody = GetComponent<Rigidbody2D>();
            _collider = GetComponent<Collider2D>();
            _entity = GetComponent<RuleEntity>();
            _lastPosition = transform.position;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _renderer == null || !_renderer.enabled) return;
            if (!ResolveSet()) return;

            Vector2 position = transform.position;
            Vector2 velocity = (position - _lastPosition) / dt;
            _lastPosition = position;
            if (_rigidbody != null && _rigidbody.bodyType == RigidbodyType2D.Dynamic) velocity.y = _rigidbody.linearVelocity.y;

            bool grounded = IsGrounded();
            _airTime = grounded ? 0f : _airTime + dt;
            bool flying = _entity != null && _entity.IsFlying;
            bool inAir = flying || _airTime > AirGrace;

            Pose pose;
            if (inAir)
            {
                if (_set.Fly != null) pose = Pose.Fly;
                else pose = velocity.y > RiseSpeed ? Pose.Rise : Pose.Fall;
            }
            else pose = Mathf.Abs(velocity.x) > WalkSpeed ? Pose.Walk : Pose.Idle;

            if (pose != _pose)
            {
                _pose = pose;
                _frame = 0;
                _frameTime = 0f;
            }

            Sprite[] frames = FramesFor(pose, out float frameTime);
            if (frames == null || frames.Length == 0) return;
            if (frames.Length > 1 && frameTime > 0f)
            {
                _frameTime += dt;
                while (_frameTime >= frameTime)
                {
                    _frameTime -= frameTime;
                    _frame = (_frame + 1) % frames.Length;
                }
            }
            if (_frame >= frames.Length) _frame = 0;
            if (_renderer.sprite != frames[_frame]) _renderer.sprite = frames[_frame];
        }

        // 지금 그림이 들어 있는 시트를 찾는다. 이미 쓰던 시트의 컷이면 그대로 간다
        private bool ResolveSet()
        {
            Sprite current = _renderer.sprite;
            if (current == null) { _set = null; return false; }
            if (_set != null && _set.All.Contains(current)) return true;

            string key = current.texture != null ? current.texture.name : null;
            _set = Load(key);
            _pose = Pose.Idle;
            _frame = 0;
            _frameTime = 0f;
            return _set != null;
        }

        private Sprite[] FramesFor(Pose pose, out float frameTime)
        {
            switch (pose)
            {
                case Pose.Walk: frameTime = _set.WalkFrameTime; return _set.Walk ?? _set.Idle;
                case Pose.Rise: frameTime = 0f; return _set.Rise ?? _set.Fall ?? _set.Idle;
                case Pose.Fall: frameTime = 0f; return _set.Fall ?? _set.Rise ?? _set.Idle;
                case Pose.Fly: frameTime = _set.FlyFrameTime; return _set.Fly ?? _set.Idle;
                default: frameTime = _set.IdleFrameTime; return _set.Idle;
            }
        }

        private bool IsGrounded()
        {
            if (_collider == null || !_collider.enabled) return true;
            int count = _collider.Cast(Vector2.down, Hits, GroundCheckDistance);
            for (int i = 0; i < count; i++)
                if (RuleEntity.IsGroundHit(Hits[i], _collider)) return true;
            return false;
        }

        // 시트마다 컷 순서. 그림 파일은 Assets/_Project/Sprite/Object 에 있고, 에디터에서 컷을 잘라 SpriteSheetLibrary에 담아 둔다
        private static AnimSet Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            // 플레이 모드를 다시 들어가면 직접 만든 컷은 지워져 있을 수 있어 그때는 새로 읽는다
            if (Cache.TryGetValue(key, out var cached) && (cached == null || cached.Idle[0] != null)) return cached;

            AnimSet set = null;
            switch (key)
            {
                case "PlayerCat":
                {
                    // 대기 2, 걷기 3, 점프, 낙하
                    var s = LoadSprites(key, 7);
                    if (s != null)
                        set = new AnimSet
                        {
                            Idle = new[] { s[0], s[1] }, IdleFrameTime = 0.4f,
                            Walk = new[] { s[2], s[3], s[4], s[3] }, WalkFrameTime = 0.1f,
                            Rise = new[] { s[5] },
                            Fall = new[] { s[6] },
                        };
                    break;
                }
                case "Pigeon":
                {
                    // 대기 1, 걷기 3, 날기 3 (공중이면 날든 떨어지든 날갯짓)
                    var s = LoadSprites(key, 7);
                    if (s != null)
                        set = new AnimSet
                        {
                            Idle = new[] { s[0] }, IdleFrameTime = 0f,
                            Walk = new[] { s[1], s[2], s[3], s[2] }, WalkFrameTime = 0.12f,
                            Fly = new[] { s[4], s[5], s[6], s[5] }, FlyFrameTime = 0.08f,
                        };
                    break;
                }
            }

            if (set != null)
            {
                foreach (var a in new[] { set.Idle, set.Walk, set.Rise, set.Fall, set.Fly })
                    if (a != null) foreach (var sp in a) set.All.Add(sp);
            }
            Cache[key] = set;
            return set;
        }

        // 잘라 둔 컷을 이름 순서로 읽는다. 아직 안 잘렸으면 텍스처를 같은 너비로 나눠 만든다
        private static Sprite[] LoadSprites(string key, int count)
        {
            var sliced = SpriteSheetLibrary.GetFrames(key);
            if (sliced != null && sliced.Length >= count)
            {
                System.Array.Sort(sliced, (a, b) => FrameIndex(a.name).CompareTo(FrameIndex(b.name)));
                var result = new Sprite[count];
                System.Array.Copy(sliced, result, count);
                return result;
            }

            var texture = sliced != null && sliced.Length > 0 && sliced[0] != null ? sliced[0].texture : null;
            if (texture == null)
            {
                Debug.LogWarning($"[EntitySpriteAnimator] SpriteSheetLibrary에서 {key} 시트를 찾지 못했다.");
                return null;
            }
            texture.filterMode = FilterMode.Point;
            int width = texture.width / count;
            var made = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                made[i] = Sprite.Create(texture, new Rect(i * width, 0f, width, texture.height), new Vector2(0.5f, 0.5f), PixelsPerUnit);
                made[i].name = $"{key}_{i}";
            }
            return made;
        }

        private static int FrameIndex(string name)
        {
            int underscore = name.LastIndexOf('_');
            return underscore >= 0 && int.TryParse(name.Substring(underscore + 1), out int n) ? n : int.MaxValue;
        }
    }
}
