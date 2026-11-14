using UnityEngine;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// 스테이지 선택 섬네일(Sprite/UI/Thumb_StageN) 목록. 타이틀은 씬에 이미지가 직접 놓여 있지만,
    /// 코드로 그리는 ESC 메뉴는 참조가 없어서 빌드에서 그림을 찾지 못했다(타이틀 씬이 내려가면 메모리에서도 빠진다).
    /// 그래서 Resources에 이 에셋을 두고 직접 참조로 들고 있게 한다. 순서대로 1스테이지, 2스테이지 ...
    /// </summary>
    [CreateAssetMenu(menuName = "CoffeeSpec/Stage Thumbnails", fileName = "StageThumbnails")]
    public sealed class StageThumbnails : ScriptableObject
    {
        public const string ResourcePath = "StageThumbnails";

        [SerializeField] private Sprite[] _thumbnails = new Sprite[0];

        private static StageThumbnails _instance;
        private static bool _loaded;

        /// <summary>stageNumber는 1부터. 없으면 null.</summary>
        public static Sprite Get(int stageNumber)
        {
            if (!_loaded || _instance == null)
            {
                _loaded = true;
                _instance = Resources.Load<StageThumbnails>(ResourcePath);
            }
            if (_instance == null || _instance._thumbnails == null) return null;
            int i = stageNumber - 1;
            return i >= 0 && i < _instance._thumbnails.Length ? _instance._thumbnails[i] : null;
        }
    }
}