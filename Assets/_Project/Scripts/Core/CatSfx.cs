using CoffeeSpec.Rules;
using CoffeeSpec.Rules.Entities;
using UnityEngine;

namespace CoffeeSpec.Core
{
    /// <summary>
    /// 고양이 야옹 소리(Resources/Sfx/meow). 점프할 때와 클리어(캣도어 연출)할 때 쓴다.
    /// 고양이마다 자기 AudioSource를 따로 달아서, 여러 마리가 같이 뛰거나 클리어하면 동시에 운다.
    /// 한 마리가 연달아 뛸 때는 자기 이전 소리만 끊고 다시 틀어 겹쳐 쌓이지 않게 한다.
    /// </summary>
    public static class CatSfx
    {
        private const string ClipPath = "Sfx/meow";
        /// <summary>고양이 밑에 다는 소리 전용 자식 이름. 설정의 효과음 배율을 두 번 곱하지 않게 GameSettings가 이 이름으로 건너뛴다.</summary>
        public const string SourceName = "CatSfx";
        private const string CatTexture = "PlayerCat";
        private const float JumpVolume = 0.6f;
        private const float ClearVolume = 0.9f;
        // 똑같이 들리면 금방 질리고, 여러 마리가 같이 울 때 한 소리로 뭉개지지 않게 음높이를 조금씩 흔든다
        private const float PitchMin = 0.95f;
        private const float PitchMax = 1.12f;

        private static AudioClip _clip;
        private static bool _warned;

        /// <summary>점프한 대상이 고양이 모습일 때만 짧게 운다. 비둘기 등으로 변신했으면 울지 않는다.</summary>
        public static void PlayJump(SpriteRenderer renderer)
        {
            if (!LooksLikeCat(renderer)) return;
            Play(renderer.gameObject, JumpVolume);
        }

        /// <summary>클리어 순간 조종 중인 고양이 모두가 같이 운다.</summary>
        public static void PlayClearAll()
        {
            foreach (var e in Object.FindObjectsByType<RuleEntity>(FindObjectsSortMode.None))
            {
                if (e == null || e.IsRemoved || !e.IsControlled) continue;
                var r = e.GetComponent<SpriteRenderer>();
                if (r == null) r = e.GetComponentInChildren<SpriteRenderer>();
                if (LooksLikeCat(r)) Play(e.gameObject, ClearVolume);
            }
        }

        // 꺼 둔(숨긴) 그림이어도 모습은 고양이로 본다. 캣도어 연출 중 진짜 고양이는 숨겨져 있다
        private static bool LooksLikeCat(SpriteRenderer r) =>
            r != null && r.sprite != null && r.sprite.texture != null && r.sprite.texture.name == CatTexture;

        private static void Play(GameObject cat, float volume)
        {
            if (!LoadClip()) return;
            var source = SourceOf(cat);
            source.Stop();
            source.clip = _clip;
            source.pitch = Random.Range(PitchMin, PitchMax);
            source.volume = volume * GameSettings.SfxVolume;
            source.Play();
        }

        // 고양이 밑에 소리 전용 자식을 하나 둔다. 엔티티 본체의 다른 AudioSource와 섞이지 않게
        private static AudioSource SourceOf(GameObject cat)
        {
            var child = cat.transform.Find(SourceName);
            if (child != null && child.TryGetComponent(out AudioSource existing)) return existing;
            var go = new GameObject(SourceName);
            go.transform.SetParent(cat.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        private static bool LoadClip()
        {
            if (_clip == null) _clip = Resources.Load<AudioClip>(ClipPath);
            if (_clip != null) return true;
            if (!_warned) Debug.LogWarning($"[CatSfx] Resources/{ClipPath} 소리를 찾지 못했다");
            _warned = true;
            return false;
        }
    }
}
