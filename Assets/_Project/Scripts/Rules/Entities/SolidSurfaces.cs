using System.Collections.Generic;
using UnityEngine;

namespace CoffeeSpec.Rules.Entities
{
    /// <summary>
    /// 고정 블록(Static 바디 + 박스 콜라이더)의 충돌 모양을 바깥으로 드러난 면만으로 만든다.
    /// 블록을 칸마다 따로 두면 붙어 있는 두 블록 사이의 안쪽 면(이음매)도 물리 엔진에 그대로 남는다.
    /// 벽에 붙어 오르내리거나 바닥을 걸을 때 몸이 그 안쪽 면 끝을 스치면, 엔진은 그 면을 막는 면(천장·벽)으로 보고
    /// 속도를 지운다(유령 충돌). 그래서 벽 이음매나 벽 위 모서리에서 점프가 끊긴다.
    /// 여기서는 각 블록에 드러난 면만 선(EdgeCollider2D)으로 붙이고, 선 끝에 이어지는 면의 방향(인접점)을 넣어
    /// 여러 블록이 한 덩어리의 매끈한 면처럼 충돌하게 한다. 선은 블록 자기 오브젝트에 붙으므로 닿음·밟힘 판정은 그 블록 것으로 그대로 난다.
    /// 원래 박스는 크기·조회(Cast, Overlap)용으로 남기고 다른 콜라이더와 접촉만 하지 않게 한다.
    /// 대상 종류(Id)는 보지 않는다. 고정(Static)이고 통과 판정이 아닌 박스면 무엇이든 같은 규칙을 따른다.
    /// </summary>
    internal static class SolidSurfaces
    {
        // 면 바로 바깥(이웃 블록이 있는지)과 안쪽을 볼 거리
        private const float Probe = 0.02f;
        // 면을 이 간격으로 나눠서 어느 부분이 이웃에 덮였는지 본다
        private const float SampleStep = 0.125f;
        // 선 끝에 이어 붙이는 인접점까지의 거리
        private const float AdjacentLength = 0.5f;

        private sealed class Entry
        {
            public BoxCollider2D Box;
            public LayerMask OriginalExclude;
            public Bounds LastBounds;
            public readonly List<EdgeCollider2D> Edges = new();
        }

        private static readonly Dictionary<RuleEntity, Entry> _entries = new();
        private static readonly HashSet<RuleEntity> _dirty = new();
        private static readonly HashSet<RuleEntity> _rebuild = new();
        private static readonly List<RuleEntity> _scratch = new();
        private static readonly List<Collider2D> _hits = new();
        private static readonly List<Vector2> _points = new(2);
        private static readonly bool[] _covered = new bool[1024];

        /// <summary>모양·바디 종류·통과 판정·켜짐이 바뀌었을 때 부른다. 다음 물리 프레임 전에 자기와 이웃 블록의 면을 다시 만든다.</summary>
        internal static void MarkDirty(RuleEntity entity)
        {
            if (!ReferenceEquals(entity, null)) _dirty.Add(entity);
        }

        /// <summary>오브젝트가 꺼지거나 지워질 때 부른다. 붙여 둔 선을 거두고 이웃 블록이 다시 면을 만들게 한다.</summary>
        internal static void Forget(RuleEntity entity)
        {
            if (ReferenceEquals(entity, null)) return;
            _dirty.Remove(entity);
            if (!_entries.TryGetValue(entity, out var entry)) return;
            MarkNeighborsDirty(entry.LastBounds, entity);
            Release(entity, entry);
        }

        /// <summary>물리 프레임 전에 부른다(RuleEntity.FixedUpdate). 바뀐 것이 없으면 바로 끝난다.</summary>
        internal static void Flush()
        {
            if (_dirty.Count == 0) return;
            Physics2D.SyncTransforms();

            _scratch.Clear();
            _scratch.AddRange(_dirty);
            _dirty.Clear();
            _rebuild.Clear();
            foreach (var e in _scratch)
            {
                if (e == null) continue;
                _rebuild.Add(e);
                // 이 블록이 생기거나 사라지면 이웃 블록의 드러난 면도 바뀐다
                if (_entries.TryGetValue(e, out var old)) CollectNeighbors(old.LastBounds, _rebuild);
                if (e.MainCollider != null && e.MainCollider.enabled) CollectNeighbors(e.MainCollider.bounds, _rebuild);
            }
            foreach (var e in _rebuild) Rebuild(e);
            _rebuild.Clear();
            _scratch.Clear();
            // 지워진 대상의 기록 정리
            _scratch.AddRange(_entries.Keys);
            foreach (var e in _scratch)
                if (e == null) _entries.Remove(e);
            _scratch.Clear();
        }

        // 고정되어 있고(Static), 통과 판정이 아니고, 켜진 박스를 가진 살아 있는 대상만 면을 나눈다
        private static bool Qualifies(RuleEntity e, out BoxCollider2D box)
        {
            box = null;
            if (e == null || !e.isActiveAndEnabled || e.IsRemoved) return false;
            var body = e.Body;
            if (body == null || !body.simulated || body.bodyType != RigidbodyType2D.Static) return false;
            box = e.MainCollider as BoxCollider2D;
            return box != null && box.enabled && !box.isTrigger;
        }

        private static void Rebuild(RuleEntity e)
        {
            _entries.TryGetValue(e, out var entry);
            if (!Qualifies(e, out var box))
            {
                if (entry != null) Release(e, entry);
                return;
            }
            if (entry == null)
            {
                entry = new Entry { Box = box, OriginalExclude = box.excludeLayers };
                _entries.Add(e, entry);
            }

            Bounds b = box.bounds;
            entry.LastBounds = b;
            Vector2 min = b.min, max = b.max;
            int used = 0;
            // 반시계 방향으로 네 면: 아래(왼→오), 오른쪽(아래→위), 위(오→왼), 왼쪽(위→아래). 바깥 법선은 진행 방향의 오른쪽
            used = BuildFace(e, entry, used, new Vector2(min.x, min.y), new Vector2(max.x, min.y), Vector2.down);
            used = BuildFace(e, entry, used, new Vector2(max.x, min.y), new Vector2(max.x, max.y), Vector2.right);
            used = BuildFace(e, entry, used, new Vector2(max.x, max.y), new Vector2(min.x, max.y), Vector2.up);
            used = BuildFace(e, entry, used, new Vector2(min.x, max.y), new Vector2(min.x, min.y), Vector2.left);
            for (int i = used; i < entry.Edges.Count; i++)
                if (entry.Edges[i] != null) entry.Edges[i].enabled = false;

            // 박스는 조회용으로만 남긴다. 박스까지 부딪히면 안쪽 면이 다시 생긴다
            box.excludeLayers = ~0;
        }

        // 한 면을 나눠 보고, 이웃 고정 블록에 덮이지 않은 구간마다 선을 하나씩 만든다
        private static int BuildFace(RuleEntity e, Entry entry, int used, Vector2 a, Vector2 b, Vector2 normal)
        {
            float length = Vector2.Distance(a, b);
            if (length < 0.0001f) return used;
            int count = Mathf.Clamp(Mathf.CeilToInt(length / SampleStep), 1, _covered.Length);
            Vector2 dir = (b - a) / length;
            for (int i = 0; i < count; i++)
            {
                Vector2 mid = a + dir * (length * (i + 0.5f) / count);
                _covered[i] = IsSolidAt(mid + normal * Probe, e);
            }
            int start = -1;
            for (int i = 0; i <= count; i++)
            {
                bool open = i < count && !_covered[i];
                if (open && start < 0) start = i;
                if (!open && start >= 0)
                {
                    Vector2 v1 = a + dir * (length * start / count);
                    Vector2 v2 = a + dir * (length * i / count);
                    used = SetEdge(e, entry, used, v1, v2, dir, normal);
                    start = -1;
                }
            }
            return used;
        }

        private static int SetEdge(RuleEntity e, Entry entry, int used, Vector2 v1, Vector2 v2, Vector2 dir, Vector2 normal)
        {
            EdgeCollider2D edge;
            if (used < entry.Edges.Count && entry.Edges[used] != null) edge = entry.Edges[used];
            else
            {
                edge = e.gameObject.AddComponent<EdgeCollider2D>();
                if (used < entry.Edges.Count) entry.Edges[used] = edge;
                else entry.Edges.Add(edge);
            }
            var t = e.transform;
            _points.Clear();
            _points.Add(t.InverseTransformPoint(v1));
            _points.Add(t.InverseTransformPoint(v2));
            edge.offset = Vector2.zero;
            edge.edgeRadius = 0f;
            edge.isTrigger = false;
            edge.SetPoints(_points);
            // 선 끝에서 면이 어떻게 이어지는지(곧게, 바깥으로 꺾임, 안으로 꺾임)를 넣어 이음매를 매끈하게 한다
            edge.useAdjacentStartPoint = true;
            edge.adjacentStartPoint = t.InverseTransformPoint(Adjacent(e, v1, -dir, normal));
            edge.useAdjacentEndPoint = true;
            edge.adjacentEndPoint = t.InverseTransformPoint(Adjacent(e, v2, dir, normal));
            edge.enabled = true;
            return used + 1;
        }

        // end에서 along 쪽으로 면이 이어지는 모양에 맞는 인접점
        private static Vector2 Adjacent(RuleEntity e, Vector2 end, Vector2 along, Vector2 normal)
        {
            Vector2 beyond = end + along * Probe * 2f;
            bool solidInside = IsSolidAt(beyond - normal * Probe, null);
            if (!solidInside) return end - normal * AdjacentLength;          // 바깥으로 꺾이는 모서리 (볼록)
            bool solidOutside = IsSolidAt(beyond + normal * Probe, null);
            if (solidOutside) return end + normal * AdjacentLength;          // 안으로 꺾이는 모서리 (오목)
            return end + along * AdjacentLength;                              // 같은 면이 이웃 블록으로 곧게 이어짐
        }

        // 그 점이 고정 블록(면을 나누는 대상의 박스) 안인지. except는 빼고 본다
        private static bool IsSolidAt(Vector2 point, RuleEntity except)
        {
            var filter = ContactFilter2D.noFilter;
            filter.useTriggers = false;
            int n = Physics2D.OverlapPoint(point, filter, _hits);
            for (int i = 0; i < n; i++)
            {
                if (!(_hits[i] is BoxCollider2D box)) continue;
                var owner = box.GetComponent<RuleEntity>();
                if (owner == null || owner == except) continue;
                if (Qualifies(owner, out var ownerBox) && ownerBox == box) return true;
            }
            return false;
        }

        private static void CollectNeighbors(Bounds bounds, HashSet<RuleEntity> into)
        {
            var filter = ContactFilter2D.noFilter;
            filter.useTriggers = false;
            Vector2 size = (Vector2)bounds.size + Vector2.one * (Probe * 4f);
            int n = Physics2D.OverlapBox(bounds.center, size, 0f, filter, _hits);
            for (int i = 0; i < n; i++)
            {
                var owner = _hits[i] != null ? _hits[i].GetComponent<RuleEntity>() : null;
                if (owner != null) into.Add(owner);
            }
        }

        private static void MarkNeighborsDirty(Bounds bounds, RuleEntity except)
        {
            _rebuild.Clear();
            CollectNeighbors(bounds, _rebuild);
            foreach (var e in _rebuild)
                if (e != except) _dirty.Add(e);
            _rebuild.Clear();
        }

        // 선을 거두고 박스의 접촉을 원래대로 돌린다
        private static void Release(RuleEntity e, Entry entry)
        {
            foreach (var edge in entry.Edges)
                if (edge != null) edge.enabled = false;
            if (entry.Box != null) entry.Box.excludeLayers = entry.OriginalExclude;
            _entries.Remove(e);
            if (e != null)
                foreach (var edge in entry.Edges)
                    if (edge != null) Object.Destroy(edge);
        }
    }
}
