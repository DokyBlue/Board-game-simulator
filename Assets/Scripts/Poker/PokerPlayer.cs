using System.Collections.Generic;

namespace BoardGameSimulator.Poker
{
    public class PokerPlayer
    {
        public string Name;
        public int Chips;
        public bool IsFolded;
        public bool IsAllIn;
        public List<PokerCard> HoleCards = new List<PokerCard>();

        public PokerPlayer(string name, int initialChips)
        {
            Name = name;
            Chips = initialChips;
        }

        public int Bet(int amount)
        {
            var paid = amount > Chips ? Chips : amount;
            Chips -= paid;
            IsAllIn = Chips == 0;
            return paid;
        }

        public void ResetForRound()
        {
            IsFolded = false;
            IsAllIn = false;
            HoleCards.Clear();
        }
    }
}
