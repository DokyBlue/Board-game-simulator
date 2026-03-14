using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardGameSimulator.Poker
{
    public enum HandRank
    {
        HighCard = 1,
        OnePair = 2,
        TwoPairs = 3,
        ThreeOfAKind = 4,
        Straight = 5,
        Flush = 6,
        FullHouse = 7,
        FourOfAKind = 8,
        StraightFlush = 9,
        RoyalFlush = 10
    }

    public class EvaluatedHand : IComparable<EvaluatedHand>
    {
        public HandRank Rank;
        public List<int> Kickers;

        public EvaluatedHand(HandRank rank, IEnumerable<int> kickers)
        {
            Rank = rank;
            Kickers = kickers.ToList();
        }

        public int CompareTo(EvaluatedHand other)
        {
            if (Rank != other.Rank)
            {
                return Rank.CompareTo(other.Rank);
            }

            for (var i = 0; i < Math.Min(Kickers.Count, other.Kickers.Count); i++)
            {
                if (Kickers[i] != other.Kickers[i])
                {
                    return Kickers[i].CompareTo(other.Kickers[i]);
                }
            }

            return 0;
        }
    }

    public static class PokerHandEvaluator
    {
        public static EvaluatedHand Evaluate(List<PokerCard> sevenCards)
        {
            var byRank = sevenCards.GroupBy(c => (int)c.Rank)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .ToList();

            var flushGroup = sevenCards.GroupBy(c => c.Suit).FirstOrDefault(g => g.Count() >= 5);
            var isFlush = flushGroup != null;

            var straightHigh = FindStraightHigh(sevenCards.Select(c => (int)c.Rank));
            var flushStraightHigh = isFlush ? FindStraightHigh(flushGroup.Select(c => (int)c.Rank)) : -1;

            if (flushStraightHigh == 14)
            {
                return new EvaluatedHand(HandRank.RoyalFlush, new[] { 14 });
            }

            if (flushStraightHigh > 0)
            {
                return new EvaluatedHand(HandRank.StraightFlush, new[] { flushStraightHigh });
            }

            if (byRank[0].Count() == 4)
            {
                return new EvaluatedHand(HandRank.FourOfAKind, new[] { byRank[0].Key, byRank[1].Key });
            }

            if (byRank[0].Count() == 3 && byRank.Any(g => g.Count() >= 2 && g.Key != byRank[0].Key))
            {
                var pair = byRank.Where(g => g.Count() >= 2 && g.Key != byRank[0].Key).Max(g => g.Key);
                return new EvaluatedHand(HandRank.FullHouse, new[] { byRank[0].Key, pair });
            }

            if (isFlush)
            {
                var topFlush = flushGroup.Select(c => (int)c.Rank).OrderByDescending(v => v).Take(5);
                return new EvaluatedHand(HandRank.Flush, topFlush);
            }

            if (straightHigh > 0)
            {
                return new EvaluatedHand(HandRank.Straight, new[] { straightHigh });
            }

            if (byRank[0].Count() == 3)
            {
                var kickers = byRank.Where(g => g.Count() == 1).Select(g => g.Key).Take(2);
                return new EvaluatedHand(HandRank.ThreeOfAKind, new[] { byRank[0].Key }.Concat(kickers));
            }

            if (byRank.Count(g => g.Count() == 2) >= 2)
            {
                var pairs = byRank.Where(g => g.Count() == 2).Select(g => g.Key).Take(2).ToList();
                var kicker = byRank.Where(g => g.Count() == 1).Select(g => g.Key).FirstOrDefault();
                return new EvaluatedHand(HandRank.TwoPairs, pairs.Concat(new[] { kicker }));
            }

            if (byRank[0].Count() == 2)
            {
                var kickers = byRank.Where(g => g.Count() == 1).Select(g => g.Key).Take(3);
                return new EvaluatedHand(HandRank.OnePair, new[] { byRank[0].Key }.Concat(kickers));
            }

            var highCards = sevenCards.Select(c => (int)c.Rank).OrderByDescending(v => v).Take(5);
            return new EvaluatedHand(HandRank.HighCard, highCards);
        }

        private static int FindStraightHigh(IEnumerable<int> ranks)
        {
            var distinct = ranks.Distinct().OrderBy(v => v).ToList();
            if (distinct.Contains(14))
            {
                distinct.Insert(0, 1);
            }

            var count = 1;
            var high = -1;
            for (var i = 1; i < distinct.Count; i++)
            {
                if (distinct[i] == distinct[i - 1] + 1)
                {
                    count++;
                    if (count >= 5)
                    {
                        high = distinct[i];
                    }
                }
                else
                {
                    count = 1;
                }
            }

            return high;
        }
    }
}
