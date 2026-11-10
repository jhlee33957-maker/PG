using UnityEngine;
using UnityEngine.InputSystem;

namespace CoffeeSpec.UI
{
    /// <summary>
    /// 가이드용 키캡 그림. 지정한 키 중 하나라도 누르고 있으면 눌린 그림으로 바꾼다.
    /// SpriteRenderer가 붙은 오브젝트에 붙이고, 안 눌린/눌린 스프라이트와 키를 지정한다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class GuideKeyPress : MonoBehaviour
    {
        [SerializeField] private Key[] _keys = { Key.LeftArrow };
        [SerializeField] private Sprite _upSprite;
        [SerializeField] private Sprite _downSprite;

        private SpriteRenderer _renderer;
        private bool _pressed;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (_upSprite == null) _upSprite = _renderer.sprite;
            Apply(false);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            bool pressed = false;
            if (kb != null)
            {
                foreach (var key in _keys)
                {
                    if (key != Key.None && kb[key].isPressed) { pressed = true; break; }
                }
            }
            if (pressed != _pressed) Apply(pressed);
        }

        private void Apply(bool pressed)
        {
            _pressed = pressed;
            var sprite = pressed ? _downSprite : _upSprite;
            if (sprite != null) _renderer.sprite = sprite;
        }
    }
}
