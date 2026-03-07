using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoardGameSimulator.Poker
{
    public class PokerDeck
    {
        private readonly List<PokerCard> _cards = new List<PokerCard>();

        public PokerDeck()
        {
            foreach (Suit suit in Enum.GetValues(typeof(Suit)))
            {
                foreach (Rank rank in Enum.GetValues(typeof(Rank)))
                {
                    _cards.Add(new PokerCard(suit, rank));
                }
            }
        }

        public void Shuffle()
        {
            for (var i = _cards.Count - 1; i > 0; i--)
            {
                var swap = UnityEngine.Random.Range(0, i + 1);
                (_cards[i], _cards[swap]) = (_cards[swap], _cards[i]);
            }
        }

        public PokerCard Draw()
        {
            var card = _cards[0];
            _cards.RemoveAt(0);
            return card;
        }
    }
}
