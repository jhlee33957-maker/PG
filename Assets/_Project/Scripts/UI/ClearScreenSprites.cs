using UnityEngine;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 클리어 네온 간판 그림. Resources 밖이라 실행 중에 경로로 찾을 수 없어서,
    /// Player Settings의 Preloaded Assets에 넣어 빌드 시작 때 불러와지게 한다. 에디터에서는 경로로 바로 읽는다.
    /// </summary>
    public sealed class ClearScreenSprites : ScriptableObject
    {
        public const string AssetPath = "Assets/_Project/Data/ClearScreenSprites.asset";

        [SerializeField] private Sprite _lit;
        [SerializeField] private Sprite _dark;
        [SerializeField] private Sprite _glow;

        public Sprite Lit => _lit;
        public Sprite Dark => _dark;
        public Sprite Glow => _glow;

        private static ClearScreenSprites _instance;

        private void OnEnable()
        {
            _instance = this;
        }

        public static ClearScreenSprites Instance
        {
            get
            {
#if UNITY_EDITOR
                if (_instance == null)
                    _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<ClearScreenSprites>(AssetPath);
#endif
                return _instance;
            }
        }

#if UNITY_EDITOR
        public void SetSprites(Sprite lit, Sprite dark, Sprite glow)
        {
            _lit = lit;
            _dark = dark;
            _glow = glow;
        }
#endif
    }
}
