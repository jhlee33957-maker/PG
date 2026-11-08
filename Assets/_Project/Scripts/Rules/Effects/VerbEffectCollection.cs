using System;
using System.Collections.Generic;
using CoffeeSpec.Rules.Entities;

namespace CoffeeSpec.Rules.Effects
{
    /// <summary>동사 등록과 이번 계산의 효과 집합을 관리한다. 등록 순서가 적용 순서다.</summary>
    public sealed class VerbEffectCollection
    {
        private readonly Dictionary<WordId, IVerbEffect> _effects = new();
        private readonly Dictionary<WordId, HashSet<RuleEntity>> _assigned = new();
        private readonly List<IVerbEffect> _ordered = new();
        private readonly Action<RuleEntity> _beforeDeath;

        public VerbEffectCollection(Action<RuleEntity> beforeDeath)
        {
            _beforeDeath = beforeDeath;
            // 충돌 설정 → 사망 → 이동 → 날기 → 조종 순서는 기존 동작을 유지한다.
            Register(new TouchEffect());
            Register(new DieEffect());
            Register(new MoveEffect());
            Register(new FlyEffect());
            Register(new ControlEffect());
            Register(new WinEffect());
            Register(new TreadEffect());
        }

        public void Register(IVerbEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            if (_effects.ContainsKey(effect.Id)) throw new ArgumentException($"이미 등록된 동사: {effect.Id}");
            _effects.Add(effect.Id, effect);
            _assigned.Add(effect.Id, new HashSet<RuleEntity>());
            _ordered.Add(effect);
        }

        public void Clear()
        {
            foreach (var set in _assigned.Values) set.Clear();
        }

        public bool Add(RuleEntity entity, WordId word) =>
            _effects.TryGetValue(word, out var effect) && effect.AcceptsResult && _assigned[word].Add(entity);

        public bool IsAssigned(RuleEntity entity, WordId word) =>
            _assigned.TryGetValue(word, out var set) && set.Contains(entity);

        public bool IsActive(RuleEntity entity, WordId word) =>
            _effects.TryGetValue(word, out var effect) && effect.IsActive(entity, IsAssigned(entity, word));

        public bool AllowsRemovedCondition(WordId word) =>
            _effects.TryGetValue(word, out var effect) && effect.AllowsRemovedCondition;

        public bool RequiresSolidSubject(WordId word) =>
            _effects.TryGetValue(word, out var effect) && effect.RequiresSolidSubject;

        public bool ObservesTouch(WordId word) =>
            _effects.TryGetValue(word, out var effect) && effect.ObservesTouch;

        public bool WillControl(RuleEntity entity)
        {
            foreach (var effect in _ordered)
                if (effect.ControlsEntity && IsAssigned(entity, effect.Id)) return true;
            return false;
        }

        public bool IsGoal(RuleEntity entity)
        {
            foreach (var effect in _ordered)
                if (effect.IsGoal && IsAssigned(entity, effect.Id)) return true;
            return false;
        }

        public void BeforeDeath(RuleEntity entity) => _beforeDeath?.Invoke(entity);

        public void Apply(RuleEntity entity)
        {
            foreach (var effect in _ordered)
            {
                if (entity.IsRemoved) break;
                effect.Apply(entity, IsAssigned(entity, effect.Id), this);
            }
        }
    }
}
