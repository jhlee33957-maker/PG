using UnityEngine;

namespace CoffeeSpec.Rules
{
    /// <summary>품사. 문장이 이 단어를 어느 자리에 받을 수 있는지 판단할 때 쓴다.</summary>
    public enum WordType
    {
        Noun,       // 명사
        Adjective,  // 형용사
        Verb,       // 동사
        Object      // 목적어
    }

    /// <summary>단어의 의미. 새 단어가 생기면 여기에 추가한다.</summary>
    public enum WordId
    {
        Person,  // 사람
        Pigeon,  // 비둘기
        Move     // 움직인다
    }

    /// <summary>태그만으로 품사를 알 수 있게 하는 조회표. 새 WordId를 추가하면 여기에도 품사를 적는다.</summary>
    public static class WordTags
    {
        public static WordType TypeOf(WordId id) => id switch
        {
            WordId.Person => WordType.Noun,
            WordId.Pigeon => WordType.Noun,
            WordId.Move => WordType.Verb,
            _ => WordType.Noun
        };

        public static bool Is(WordId id, WordType type) => TypeOf(id) == type;

        // 플레이어에게 보일 단어. 문장 틀의 빈칸에 들어간다
        public static string DisplayName(WordId id) => id switch
        {
            WordId.Person => "사람",
            WordId.Pigeon => "비둘기",
            WordId.Move => "움직인다",
            _ => id.ToString()
        };
    }

    /// <summary>
    /// 모든 단어를 하나의 클래스로 표현하고, 품사와 의미 태그로만 구분한다.
    /// 사람, 비둘기, 움직인다 같은 단어는 이 클래스의 에셋으로 만든다.
    /// </summary>
    [CreateAssetMenu(menuName = "CoffeeSpec/Word", fileName = "Word_New")]
    public class Word : ScriptableObject
    {
        [SerializeField] private string _displayName;
        [SerializeField] private WordType _type;
        [SerializeField] private WordId _id;
        [Tooltip("명사일 때 대상의 모습. 'X는 Y다' 문장에서 X가 이 모습으로 바뀐다")]
        [SerializeField] private Sprite _sprite;

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public WordType Type => _type;
        public WordId Id => _id;
        public Sprite Sprite => _sprite;

        public bool Is(WordType type) => _type == type;
        public bool Is(WordId id) => _id == id;
    }
}