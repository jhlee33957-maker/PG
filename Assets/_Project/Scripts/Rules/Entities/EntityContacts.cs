using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoffeeSpec.Rules.Entities
{
    /// <summary>닿음과 밟힘을 기억하고 상태가 바뀔 때만 규칙 관리자에 알린다.</summary>
    internal sealed class EntityContacts
    {
        private const float TreadNormalY = -0.5f;
        private bool _baseIsTrigger;
        private bool _treadable;
        private bool _touchable;
        private bool _hadTouch;
        private bool _hadTread;
        // 밟힘은 (내 콜라이더, 상대 콜라이더) 쌍마다 기억한다. 한 대상이 콜라이더를 여러 개 가질 수 있어서(고정 블록의 면 선)
        // 한 쌍이 떨어져도 다른 쌍으로 계속 밟고 있으면 밟힘이 유지되어야 한다
        private readonly Dictionary<(Collider2D mine, Collider2D other), RuleEntity> _treadPairs = new();
        private readonly HashSet<Collider2D> _touching = new();
        // 같은 상대 콜라이더와 내 콜라이더 여러 개가 동시에 닿아 있을 수 있다. 마지막 쌍이 떨어질 때만 떨어진 것으로 본다
        private readonly Dictionary<Collider2D, int> _pairCount = new();
        private readonly List<(Collider2D mine, Collider2D other)> _staleTread = new();
        // 벽·바닥처럼 RuleEntity가 없는 콜라이더도 null 값으로 캐시한다.
        private readonly Dictionary<Collider2D, RuleEntity> _owners = new();
        private readonly HashSet<RuleEntity> _peers = new();
        private readonly HashSet<RuleEntity> _unique = new();
        private readonly List<Collider2D> _stale = new();
        private readonly Action<RuleEntity> _onPeerInvalidated;
        private readonly RuleEntity _owner;
        private readonly Rigidbody2D _rigidbody;
        private readonly Collider2D _collider;

        internal EntityContacts(RuleEntity owner, Rigidbody2D rigidbody, Collider2D collider)
        {
            _owner = owner;
            _rigidbody = rigidbody;
            _collider = collider;
            _onPeerInvalidated = OnPeerInvalidated;
        }

        internal bool BaseIsTrigger { get => _baseIsTrigger; set => _baseIsTrigger = value; }

        internal bool IsTreaded
        {
            get
            {
                // 벽은 밟는 쪽으로 치지 않는다. 벽 위에 얹혀 있거나 벽이 붙어 있기만 해도 '밟히면' 조건이 켜지면 안 된다
                foreach (var t in _treadPairs.Values)
                    if (t != null && !t.IsRemoved && t.isActiveAndEnabled && t.Id != TouchIgnoredId) return true;
                return false;
            }
        }

        internal bool IsTouched
        {
            get
            {
                PruneInvalidContacts();
                return HasTouch();
            }
        }

        // 벽은 맵 어디에나 붙어 있어서 닿은 것으로 치면 걷기만 해도 Touch 조건이 켜진다. 벽만 닿아 있으면 안 닿은 것으로 본다.
        // 변신으로 Id가 바뀔 수 있어서 캐시하지 않고 매번 확인한다
        private static readonly WordId TouchIgnoredId = new("Wall");

        private bool HasTouch()
        {
            foreach (var c in _touching)
            {
                if (_owners.TryGetValue(c, out var e) && e != null && e.Id == TouchIgnoredId) continue;
                return true;
            }
            return false;
        }

        private RuleEntity ResolveCollider(Collider2D collider)
        {
            if (_owners.TryGetValue(collider, out var entity)) return entity;
            entity = collider.GetComponentInParent<RuleEntity>();
            _owners.Add(collider, entity);
            if (entity != null && entity != _owner && _peers.Add(entity))
                entity.ContactsInvalidated += _onPeerInvalidated;
            return entity;
        }

        private static bool IsValid(Collider2D collider, RuleEntity entity) =>
            collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
            (ReferenceEquals(entity, null) || (entity != null && !entity.IsRemoved && entity.isActiveAndEnabled));

        // Unity가 Exit를 보내지 않은 파괴·비활성 콜라이더는 기존 조회/접촉 이벤트에서만 정리한다.
        // 새 Update나 물리 쿼리는 추가하지 않는다.
        private void PruneInvalidContacts()
        {
            _stale.Clear();
            foreach (var c in _touching)
                if (!IsValid(c, _owners[c])) _stale.Add(c);
            bool changed = _stale.Count > 0;
            RuleEntity lost = null;
            foreach (var c in _stale)
            {
                if (lost == null) _owners.TryGetValue(c, out lost);
                _touching.Remove(c);
                _pairCount.Remove(c);
                ForgetCollider(c);
            }
            _stale.Clear();
            if (changed) NotifyTouchSetChanged(lost, ended: true);
        }

        private void ForgetCollider(Collider2D collider)
        {
            if (!_owners.TryGetValue(collider, out var entity)) return;
            _owners.Remove(collider);
            if (ReferenceEquals(entity, null)) return;
            foreach (var peer in _owners.Values)
                if (ReferenceEquals(peer, entity)) return;
            if (_peers.Remove(entity)) entity.ContactsInvalidated -= _onPeerInvalidated;
        }

        private void NotifyTouchSetChanged(RuleEntity other = null, bool ended = false)
        {
            bool touched = HasTouch();
            bool changed = _hadTouch != touched;
            _hadTouch = touched;
            _owner.Manager?.NotifyTouchChanged(_owner, changed, other, ended);
        }

        private void NotifyTreadStateChanged()
        {
            bool treaded = IsTreaded;
            if (_hadTread == treaded) return;
            _hadTread = treaded;
            _owner.Manager?.NotifyTreadChanged(_owner);
        }

        // 상대의 사망·비활성은 Exit가 생략돼도 즉시 접촉에서 제외한다.
        private void OnPeerInvalidated(RuleEntity entity)
        {
            _stale.Clear();
            foreach (var pair in _owners)
                if (ReferenceEquals(pair.Value, entity)) _stale.Add(pair.Key);
            bool changed = false;
            foreach (var c in _stale)
            {
                changed |= _touching.Remove(c);
                _pairCount.Remove(c);
                ForgetCollider(c);
            }
            _stale.Clear();
            RemoveTreader(entity);
            NotifyTreadStateChanged();
            if (changed) NotifyTouchSetChanged(entity, ended: true);
        }

        /// <summary>같은 대상의 여러 콜라이더를 한 번만 담는다. 현재 태그는 캐시하지 않는다.</summary>
        internal void GetTouchingEntities(List<RuleEntity> into)
        {
            PruneInvalidContacts();
            _unique.Clear();
            foreach (var e in into) _unique.Add(e);
            foreach (var c in _touching)
            {
                var entity = _owners[c];
                if (entity != null && entity != _owner && !entity.IsRemoved && _unique.Add(entity)) into.Add(entity);
            }
            _unique.Clear();
        }

        internal void UpdateTouch(Collider2D other, bool entering)
        {
            if (other == null || (_rigidbody != null && other.attachedRigidbody == _rigidbody)) return;
            PruneInvalidContacts();
            bool changed;
            RuleEntity entity = null;
            if (entering)
            {
                entity = ResolveCollider(other);
                if (IsValid(other, entity))
                {
                    _pairCount.TryGetValue(other, out int count);
                    _pairCount[other] = count + 1;
                    changed = _touching.Add(other);
                }
                else
                {
                    changed = _touching.Remove(other);
                    _pairCount.Remove(other);
                    ForgetCollider(other);
                }
            }
            else
            {
                // 내 다른 콜라이더로 아직 닿아 있으면 떨어진 것이 아니다
                if (_pairCount.TryGetValue(other, out int count) && count > 1)
                {
                    _pairCount[other] = count - 1;
                    return;
                }
                _pairCount.Remove(other);
                _owners.TryGetValue(other, out entity);
                changed = _touching.Remove(other);
                ForgetCollider(other);
            }
            if (!changed) return;
            // 같은 대상의 다른 콜라이더(예: 블록 모서리의 두 면)에 아직 닿아 있으면 그 대상과의 접촉은 끝나지 않았다
            bool ended = !entering && !IsTouchingEntity(entity);
            NotifyTouchSetChanged(entity, ended: ended);
        }

        private bool IsTouchingEntity(RuleEntity entity)
        {
            if (ReferenceEquals(entity, null)) return false;
            foreach (var c in _touching)
                if (_owners.TryGetValue(c, out var e) && ReferenceEquals(e, entity)) return true;
            return false;
        }

        private void RemoveTreader(RuleEntity entity)
        {
            _staleTread.Clear();
            foreach (var pair in _treadPairs)
                if (ReferenceEquals(pair.Value, entity)) _staleTread.Add(pair.Key);
            foreach (var key in _staleTread) _treadPairs.Remove(key);
            _staleTread.Clear();
        }

        internal void SetTouchable(bool on)
        {
            if (_touchable == on) return;
            _touchable = on;
            ApplyTrigger();
        }

        internal void UpdateTread(Collision2D collision, bool entering)
        {
            var other = collision.collider != null ? ResolveCollider(collision.collider) : null;
            if (other == null || other == _owner) return;
            var key = (collision.otherCollider, collision.collider);
            if (entering)
            {
                if (IsPressedFromAbove(collision)) _treadPairs[key] = other;
            }
            else _treadPairs.Remove(key);
            NotifyTreadStateChanged();
        }

        internal void SetTreadable(bool on)
        {
            if (_treadable == on) return;
            _treadable = on;
            ApplyTrigger();
        }

        internal void ApplyTrigger()
        {
            if (_collider == null) return;
            bool trigger = _baseIsTrigger && !_treadable && !_touchable;
            if (_collider.isTrigger == trigger) return;
            _collider.isTrigger = trigger;
            // 통과 판정이 바뀌면 고정 블록의 면 선도 다시 만든다 (통과 판정 몸은 면 선을 쓰지 않는다)
            SolidSurfaces.MarkDirty(_owner);
            bool wasTouched = _hadTouch;
            bool hadContacts = _touching.Count > 0;
            // 변신은 콜라이더를 교체하지 않고 크기만 바꾼다. 판정 종류가 바뀔 때만 기존처럼 비운다.
            ClearTouching();
            if (hadContacts) _owner.Manager?.NotifyTouchChanged(_owner, wasTouched);
            if (!trigger) return;
            _treadPairs.Clear();
            NotifyTreadStateChanged();
        }

        private static bool IsPressedFromAbove(Collision2D collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
                if (collision.GetContact(i).normal.y <= TreadNormalY) return true;
            return false;
        }

        internal void ReportContact(Collider2D other)
        {
            if (other == null || (_rigidbody != null && other.attachedRigidbody == _rigidbody)) return;
            var entity = ResolveCollider(other);
            if (entity != null && entity != _owner) _owner.Manager?.ReportContact(_owner, entity);
        }

        internal void ClearTreaders()
        {
            _treadPairs.Clear();
            _hadTread = false;
        }

        internal void ClearTouching()
        {
            foreach (var peer in _peers)
                if (!ReferenceEquals(peer, null)) peer.ContactsInvalidated -= _onPeerInvalidated;
            _peers.Clear();
            _touching.Clear();
            _pairCount.Clear();
            _owners.Clear();
            _hadTouch = false;
        }
    }
}
