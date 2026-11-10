using System;
using UnityEngine;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// Sprite/Object에 있는 애니메이션 시트(PlayerCat, Pigeon, CatEnter, DoorFlap)의 잘린 컷 목록.
    /// Resources 밖의 그림은 실행 중에 이름으로 찾을 수 없어서, 에디터(EntitySpriteSheetPostprocessor)가 컷을 이 에셋에 담아 두고
    /// Player Settings의 Preloaded Assets에 넣어 빌드 시작 때 불러와지게 한다. 에디터에서는 경로로 바로 읽는다.
    /// </summary>
    public sealed class SpriteSheetLibrary : ScriptableObject
    {
        public const string AssetPath = "Assets/_Project/Data/SpriteSheetLibrary.asset";

        [Serializable]
        public sealed class Sheet
        {
            public string Name;
            public Sprite[] Frames;
        }

        [SerializeField] private Sheet[] _sheets = Array.Empty<Sheet>();

        private static SpriteSheetLibrary _instance;

        private void OnEnable()
        {
            _instance = this;
        }

        private static SpriteSheetLibrary Instance
        {
            get
            {
#if UNITY_EDITOR
                if (_instance == null) _instance = UnityEditor.AssetDatabase.LoadAssetAtPath<SpriteSheetLibrary>(AssetPath);
#endif
                return _instance;
            }
        }

        /// <summary>시트 이름(PlayerCat 등)의 컷을 번호 순서로 돌려준다. 없으면 null.</summary>
        public static Sprite[] GetFrames(string name)
        {
            var library = Instance;
            if (library == null || library._sheets == null || string.IsNullOrEmpty(name)) return null;
            foreach (var sheet in library._sheets)
            {
                if (sheet != null && sheet.Name == name && sheet.Frames != null && sheet.Frames.Length > 0)
                    return (Sprite[])sheet.Frames.Clone();
            }
            return null;
        }

#if UNITY_EDITOR
        /// <summary>에디터 전용. 내용이 달라졌을 때만 바꾸고 true를 돌려준다.</summary>
        public bool SetSheets(Sheet[] sheets)
        {
            if (Same(_sheets, sheets)) return false;
            _sheets = sheets;
            return true;
        }

        private static bool Same(Sheet[] a, Sheet[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null || b[i] == null || a[i].Name != b[i].Name) return false;
                var fa = a[i].Frames;
                var fb = b[i].Frames;
                if (fa == null || fb == null || fa.Length != fb.Length) return false;
                for (int j = 0; j < fa.Length; j++)
                    if (fa[j] != fb[j]) return false;
            }
            return true;
        }
#endif
    }
}
