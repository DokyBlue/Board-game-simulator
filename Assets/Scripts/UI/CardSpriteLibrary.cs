using System.Collections.Generic;
using BoardGameSimulator.Poker;
using UnityEngine;

namespace BoardGameSimulator.UI
{
    public class CardSpriteLibrary : MonoBehaviour
    {
        [SerializeField] private List<Sprite> cardSprites;
        [SerializeField] private Sprite backSprite;

        private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        private void Awake()
        {
            _cache.Clear();
            foreach (var sprite in cardSprites)
            {
                _cache[sprite.name] = sprite;
            }
        }

        public Sprite GetFront(PokerCard card)
        {
            var key = $"{(int)card.Rank}_{card.Suit}";
            return _cache.TryGetValue(key, out var sprite) ? sprite : backSprite;
        }

        public Sprite GetBack() => backSprite;
    }
}
