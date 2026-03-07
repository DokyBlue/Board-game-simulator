using System.Collections.Generic;
using System.Linq;
using BoardGameSimulator.Core;
using TMPro;
using UnityEngine;

namespace BoardGameSimulator.Poker
{
    public class TexasHoldemGameManager : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private int initialChips = 1000;
        [SerializeField] private int smallBlind = 10;
        [SerializeField] private int bigBlind = 20;

        [Header("UI")]
        [SerializeField] private TMP_Text stateText;
        [SerializeField] private TMP_Text boardText;
        [SerializeField] private TMP_Text playerText;

        private readonly List<PokerPlayer> _players = new List<PokerPlayer>();
        private readonly List<PokerCard> _communityCards = new List<PokerCard>();

        private PokerDeck _deck;
        private int _pot;

        private void Start()
        {
            CreateDefaultTable();
            StartNewRound();
        }

        public void StartNewRound()
        {
            _pot = 0;
            _communityCards.Clear();
            _deck = new PokerDeck();
            _deck.Shuffle();

            foreach (var player in _players)
            {
                player.ResetForRound();
                player.HoleCards.Add(_deck.Draw());
                player.HoleCards.Add(_deck.Draw());
            }

            PostBlinds();
            DealFlopTurnRiver();
            ResolveShowdown();
            RenderUI();
        }

        private void PostBlinds()
        {
            _pot += _players[0].Bet(smallBlind);
            _pot += _players[1].Bet(bigBlind);
        }

        private void DealFlopTurnRiver()
        {
            _communityCards.Add(_deck.Draw());
            _communityCards.Add(_deck.Draw());
            _communityCards.Add(_deck.Draw());
            _communityCards.Add(_deck.Draw());
            _communityCards.Add(_deck.Draw());
        }

        private void ResolveShowdown()
        {
            var best = _players
                .Select(p => new
                {
                    Player = p,
                    Hand = PokerHandEvaluator.Evaluate(p.HoleCards.Concat(_communityCards).ToList())
                })
                .OrderByDescending(x => x.Hand)
                .First();

            best.Player.Chips += _pot;
            stateText.text = $"赢家：{best.Player.Name}，牌型：{best.Hand.Rank}，底池：{_pot}";
        }

        private void RenderUI()
        {
            boardText.text = "公共牌：" + string.Join(" | ", _communityCards.Select(c => c.ToString()));
            playerText.text = string.Join("\n", _players.Select(p =>
                $"{p.Name} 手牌:{p.HoleCards[0]},{p.HoleCards[1]} 筹码:{p.Chips}"));
        }

        private void CreateDefaultTable()
        {
            _players.Clear();
            _players.Add(new PokerPlayer(SessionContext.CurrentUser, initialChips));
            _players.Add(new PokerPlayer("Bot-A", initialChips));
            _players.Add(new PokerPlayer("Bot-B", initialChips));
            _players.Add(new PokerPlayer("Bot-C", initialChips));
        }
    }
}
