using System.Collections.Generic;
using UnityEngine;

namespace CoffeeSpec.Player
{
    /// <summary>
    /// 보이지 않는 공격 판정. 잠깐 존재하면서 닿은 대상을 한 번씩 기록하고 사라진다.
    /// 아직 피해 시스템이 없어서 맞은 대상은 로그로만 남긴다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class AttackHitbox : MonoBehaviour
    {
        private GameObject _owner;
        private float _lifetime;
        private readonly HashSet<GameObject> _hit = new();

        public void Init(GameObject owner, float lifetime)
        {
            _owner = owner;
            _lifetime = lifetime;
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