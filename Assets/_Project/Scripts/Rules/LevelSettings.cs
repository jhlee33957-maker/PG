using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoffeeSpec.Rules
{
    /// <summary>
    /// 맵마다 다른 설정. 레벨 에디터가 맵 데이터(JSON)대로 Level 루트에 붙여 둔다.
    /// 오브젝트 프리팹 값을 바꾸면 모든 맵이 바뀌므로, 이 맵에서만 달라야 하는 값은 여기에 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelSettings : MonoBehaviour
    {
        [Serializable]
        public struct FlyHeight
        {
            [Tooltip("이 단어 모습일 때 적용된다. 다른 것에서 변신한 경우도 포함")]
            public WordId word;
            [Tooltip("바닥에서 떠오를 높이(유닛)")]
            [Min(0f)] public float height;
        }

        [Tooltip("이 맵에서만 쓰는 단어별 날기 높이. 없는 단어는 각 오브젝트의 Fly Height Range를 쓴다")]
        [SerializeField] private List<FlyHeight> _flyHeights = new();

        private static LevelSettings _active;

        // 레벨 에디터(에디터 어셈블리)가 맵 데이터를 읽고 쓰므로 public이다
        public List<FlyHeight> FlyHeights => _flyHeights;

        private void Awake() => _active = this;
        private void OnEnable() => _active = this;

        private void OnDisable()
        {
            if (_active == this) _active = null;
        }

        /// <summary>지금 맵에 이 단어의 날기 높이가 정해져 있으면 true. 날기 시작할 때 현재 모습으로 찾는다.</summary>
        public static bool TryGetFlyHeight(WordId id, out float height)
        {
            height = 0f;
            // 씬 시작 때 RuleManager가 이 컴포넌트보다 먼저 규칙을 적용하면 아직 등록 전이라 직접 찾는다
            if (_active == null) _active = FindAnyObjectByType<LevelSettings>();
            if (_active == null) return false;
            foreach (var f in _active._flyHeights)
            {
                if (f.word != id) continue;
                height = f.height;
                return true;
            }
            return false;
        }
    }
}