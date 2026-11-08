using System.Collections.Generic;
using UnityEngine;
using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Player
{
    /// <summary>
    /// 공격 판정. 잠깐 존재하면서 닿은 대상을 한 번씩 기록하고 사라진다.
    /// 맞은 대상이 '공격으로 사라짐'이 켜진 RuleEntity면 숨긴다. 나머지는 로그로만 남긴다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class AttackHitbox : MonoBehaviour
    {
        private GameObject _owner;
        private float _lifetime;
        private readonly HashSet<GameObject> _hit = new();
        private static Sprite _whiteSprite; // 판정 표시용 1x1 흰색 스프라이트. 한 번만 만들어 같이 쓴다

        public void Init(GameObject owner, float lifetime)
        {
            _owner = owner;
            _lifetime = lifetime;
        }

        /// <summary>콜라이더와 같은 크기의 반투명 사각형을 게임 화면에 그린다. 판정이 사라지면 같이 사라진다.</summary>
        public void ShowVisual(Color color)
        {
            if (_whiteSprite == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            }

            var box = GetComponent<BoxCollider2D>();
            var visual = new GameObject("HitboxVisual");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = box.offset;
            visual.transform.localScale = new Vector3(box.size.x, box.size.y, 1f);

            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = _whiteSprite;
            sr.color = color;
            sr.sortingOrder = 100; // 캐릭터와 지형 위에 보이도록
        }

        private void Update()
        {
            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f) Destroy(gameObject);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var target = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
            if (target == _owner || !_hit.Add(target)) return;
            var entity = target.GetComponentInParent<RuleEntity>();
            if (entity != null && entity.TryRemoveByAttack())
            {
                Debug.Log($"[Attack] {target.name} 명중, 사라짐");
                return;
            }
            Debug.Log($"[Attack] {target.name} 명중");
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider2D>();
            Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
            Gizmos.DrawCube(transform.position + (Vector3)box.offset, box.size);
        }
    }
}