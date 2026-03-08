using System.Collections.Generic;

namespace BoardGameSimulator.Poker
{
    public class PokerPlayer
    {
        public string Name;
        public int Chips;
        public bool IsFolded;
        public bool IsAllIn;
        public bool IsBot;
        public int CurrentBet;
        public List<PokerCard> HoleCards = new List<PokerCard>();

        public PokerPlayer(string name, int initialChips, bool isBot)
        {
            Name = name;
            Chips = initialChips;
            IsBot = isBot;
        }

        public int BetTo(int targetBet)
        {
            var need = targetBet - CurrentBet;
            if (need <= 0)
            {
                return 0;
            }

            var paid = need > Chips ? Chips : need;
            Chips -= paid;
            CurrentBet += paid;
            IsAllIn = Chips == 0;
            return paid;
        }

        public int RaiseBy(int raiseAmount)
        {
            var paid = raiseAmount > Chips ? Chips : raiseAmount;
            Chips -= paid;
            CurrentBet += paid;
            IsAllIn = Chips == 0;
            return paid;
        }

        public int AllIn()
        {
            if (Chips <= 0)
            {
                return 0;
            }

            var paid = Chips;
            Chips = 0;
            CurrentBet += paid;
            IsAllIn = true;
            return paid;
        }

        public void ResetForRound()
        {
            IsFolded = false;
            IsAllIn = false;
            CurrentBet = 0;
            HoleCards.Clear();
        }
    }
}
