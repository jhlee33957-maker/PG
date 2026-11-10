using System.Collections;
using CoffeeSpec.Rules;
using CoffeeSpec.Rules.Entities;
using UnityEngine;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// 클리어 순간 고양이가 출구 문 앞까지 걸어가 캣도어로 쏙 들어가고, 문 플랩이 흔들리다 닫히는 연출.
    /// 고양이 그림(Resources/CatEnter)과 문 그림(Resources/DoorFlap)은 따로 된 시트다.
    /// 조종 대상이 고양이(PlayerCat)이고 근처에 출구 문(Proto_Exit)이 있을 때만 재생하고,
    /// 변신 등으로 다른 모습이면 아무것도 하지 않아 기존 클리어 흐름이 그대로 간다.
    /// 기획서가 열려 timeScale이 0이어도 멈추지 않게 실제 시간으로 돈다.
    /// </summary>
    public sealed class ExitEnterAnimation : MonoBehaviour
    {
        private const float PixelsPerUnit = 32f;
        private const string CatSheet = "CatEnter";
        private const string DoorSheet = "DoorFlap";
        private const string PlayerSheet = "PlayerCat";
        private const string DoorTexture = "Proto_Exit";
        // 고양이 컷(28x20) 가운데가 문(20x40) 가운데에서 떨어진 거리. 컷 안의 구멍 모서리(26열)가 문 구멍 모서리(7열)에 맞는 자리
        private const float CatFrameOffsetX = 15f / PixelsPerUnit;
        // 첫 컷(멈춤)에서 고양이 몸 가운데가 컷 가운데에서 떨어진 거리. 걸어온 고양이가 이 자리에 서야 끊기지 않는다
        private const float CatBodyOffsetX = 4.5f / PixelsPerUnit;
        // 고양이를 찾은 뒤 이 거리 안의 문만 출구로 본다 (유닛)
        private const float DoorSearchRadius = 3f;
        private const float WalkSpeed = 2.5f;
        private const float MaxWalkTime = 0.5f;
        private const float WalkFrameTime = 0.1f;

        // 멈춤, 웅크림, 납작, 미끄러짐 3단계, 꼬리 끝
        private static readonly float[] CatTimes = { 0.18f, 0.15f, 0.12f, 0.09f, 0.09f, 0.09f, 0.11f };
        // DoorFlap 컷: 0 열린 구멍 ~ 5 다 닫힘. 흔들림 폭이 줄다가 닫힌 채로 멈춘다
        private static readonly int[] FlapFrames = { 1, 4, 2, 5, 3, 5, 4, 5 };
        private static readonly float[] FlapTimes = { 0.07f, 0.07f, 0.08f, 0.08f, 0.09f, 0.10f, 0.12f, 0f };

        private static ExitEnterAnimation _current;

        private SpriteRenderer _playerRenderer;
        private SpriteRenderer _doorRenderer;
        private Sprite _doorOriginal;
        private Rigidbody2D _body;
        private bool _bodyWasSimulated;
        private SpriteRenderer _view;

        /// <summary>연출을 시작한다. 재생하지 못하면 false. duration은 플랩이 닫힐 때까지 걸리는 시간(초).</summary>
        public static bool TryPlay(out float duration)
        {
            duration = 0f;
            Cancel();

            Sprite[] cat = LoadFrames(CatSheet, CatTimes.Length);
            Sprite[] flap = LoadFrames(DoorSheet, 6);
            Sprite[] walk = LoadFrames(PlayerSheet, 5);
            if (cat == null || flap == null || walk == null) return false;

            if (!FindCatAndDoor(out var player, out var playerRenderer, out var doorRenderer)) return false;

            var go = new GameObject("ExitEnterAnimation");
            var anim = go.AddComponent<ExitEnterAnimation>();
            _current = anim;
            duration = anim.Begin(player, playerRenderer, doorRenderer, cat, flap, walk);
            return true;
        }

        /// <summary>리트 등으로 되돌릴 때 고양이와 문을 원래대로 돌려놓는다.</summary>
        public static void Cancel()
        {
            if (_current == null) return;
            _current.Restore();
            Destroy(_current.gameObject);
            _current = null;
        }

        private static bool FindCatAndDoor(out RuleEntity player, out SpriteRenderer playerRenderer, out SpriteRenderer doorRenderer)
        {
            player = null; playerRenderer = null; doorRenderer = null;
            var entities = FindObjectsByType<RuleEntity>(FindObjectsSortMode.None);
            foreach (var e in entities)
            {
                if (e == null || e.IsRemoved || !e.IsControlled) continue;
                var r = RendererOf(e);
                if (!HasTexture(r, PlayerSheet)) continue;
                player = e; playerRenderer = r;
                break;
            }
            if (player == null) return false;

            float best = DoorSearchRadius;
            Vector2 at = playerRenderer.bounds.center;
            foreach (var e in entities)
            {
                if (e == null || e.IsRemoved || e == player) continue;
                var r = RendererOf(e);
                if (!HasTexture(r, DoorTexture)) continue;
                float d = Vector2.Distance(at, r.bounds.center);
                if (d < best) { best = d; doorRenderer = r; }
            }
            return doorRenderer != null;
        }

        private static SpriteRenderer RendererOf(RuleEntity e)
        {
            var r = e.GetComponent<SpriteRenderer>();
            return r != null ? r : e.GetComponentInChildren<SpriteRenderer>();
        }

        private static bool HasTexture(SpriteRenderer r, string name) =>
            r != null && r.enabled && r.sprite != null && r.sprite.texture != null && r.sprite.texture.name == name;

        private float Begin(RuleEntity player, SpriteRenderer playerRenderer, SpriteRenderer doorRenderer, Sprite[] cat, Sprite[] flap, Sprite[] walk)
        {
            _playerRenderer = playerRenderer;
            _doorRenderer = doorRenderer;
            _doorOriginal = doorRenderer.sprite;

            Bounds door = doorRenderer.bounds;
            Vector3 start = playerRenderer.bounds.center;
            // 문 오른쪽에서 왔으면 좌우를 뒤집는다. 구멍이 문 가운데라 뒤집어도 모서리가 맞는다
            bool fromRight = start.x > door.center.x;
            float side = fromRight ? 1f : -1f;
            var frameCenter = new Vector3(door.center.x + side * CatFrameOffsetX, door.min.y + 10f / PixelsPerUnit, start.z);
            var walkTarget = frameCenter - new Vector3(side * CatBodyOffsetX, 0f, 0f);
            float walkTime = Mathf.Min(MaxWalkTime, Vector2.Distance(start, walkTarget) / WalkSpeed);

            _view = gameObject.AddComponent<SpriteRenderer>();
            _view.sortingLayerID = playerRenderer.sortingLayerID;
            _view.sortingOrder = Mathf.Max(playerRenderer.sortingOrder, doorRenderer.sortingOrder + 1);
            _view.flipX = fromRight;
            transform.position = start;

            // 진짜 고양이는 숨기고 멈춰 둔다. 클리어 뒤에 굴러가거나 떨어지지 않게
            _playerRenderer.enabled = false;
            _body = player.GetComponent<Rigidbody2D>();
            if (_body != null)
            {
                _bodyWasSimulated = _body.simulated;
                _body.linearVelocity = Vector2.zero;
                _body.simulated = false;
            }

            CatSfx.PlayClearAll();
            StartCoroutine(Run(start, walkTarget, frameCenter, walkTime, cat, flap, walk));

            float total = walkTime;
            foreach (float t in CatTimes) total += t;
            foreach (float t in FlapTimes) total += t;
            return total;
        }

        private IEnumerator Run(Vector3 start, Vector3 walkTarget, Vector3 frameCenter, float walkTime, Sprite[] cat, Sprite[] flap, Sprite[] walk)
        {
            // 1) 문 앞까지 걷기 (PlayerCat 걷기 컷 2,3,4,3)
            int[] walkOrder = { 2, 3, 4, 3 };
            for (float t = 0f; t < walkTime; t += Time.unscaledDeltaTime)
            {
                _view.sprite = walk[walkOrder[(int)(t / WalkFrameTime) % walkOrder.Length]];
                transform.position = Vector3.Lerp(start, walkTarget, t / walkTime);
                yield return null;
            }

            // 2) 캣도어로 들어가기. 구멍 안쪽은 그림에서 미리 잘라 두었다
            transform.position = frameCenter;
            for (int i = 0; i < cat.Length; i++)
            {
                _view.sprite = cat[i];
                yield return new WaitForSecondsRealtime(CatTimes[i]);
            }
            _view.enabled = false;

            // 3) 플랩이 흔들리다 닫힌 채로 멈춘다
            for (int i = 0; i < FlapFrames.Length; i++)
            {
                if (_doorRenderer == null) yield break;
                _doorRenderer.sprite = flap[FlapFrames[i]];
                if (FlapTimes[i] > 0f) yield return new WaitForSecondsRealtime(FlapTimes[i]);
            }
        }

        private void Restore()
        {
            if (_playerRenderer != null) _playerRenderer.enabled = true;
            if (_body != null) _body.simulated = _bodyWasSimulated;
            if (_doorRenderer != null && _doorOriginal != null) _doorRenderer.sprite = _doorOriginal;
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
        }

        // 잘라 둔 컷을 이름 끝 번호 순서로 가져온다
        private static Sprite[] LoadFrames(string key, int count)
        {
            var sprites = Resources.LoadAll<Sprite>(key);
            if (sprites == null || sprites.Length < count)
            {
                Debug.LogWarning($"[ExitEnterAnimation] Resources/{key} 컷이 {count}개보다 적어 연출을 건너뜀");
                return null;
            }
            System.Array.Sort(sprites, (a, b) => Index(a.name).CompareTo(Index(b.name)));
            var result = new Sprite[count];
            System.Array.Copy(sprites, result, count);
            return result;
        }

        private static int Index(string name)
        {
            int u = name.LastIndexOf('_');
            return u >= 0 && int.TryParse(name.Substring(u + 1), out int n) ? n : int.MaxValue;
        }
    }
}
