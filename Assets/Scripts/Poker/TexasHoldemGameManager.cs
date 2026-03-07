using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BoardGameSimulator.Core;
using BoardGameSimulator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoardGameSimulator.Poker
{
    public enum BettingStreet
    {
        Preflop,
        Flop,
        Turn,
        River,
        Showdown
    }

    public class TexasHoldemGameManager : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private int initialChips = 2000;
        [SerializeField] private int smallBlind = 10;
        [SerializeField] private int bigBlind = 20;
        [SerializeField] private int raiseStep = 20;
        [SerializeField] private float botActionDelay = 0.5f;

        [Header("Text UI")]
        [SerializeField] private TMP_Text stateText;
        [SerializeField] private TMP_Text boardText;
        [SerializeField] private TMP_Text playersText;

        [Header("Action UI")]
        [SerializeField] private Button callButton;
        [SerializeField] private Button raiseButton;
        [SerializeField] private Button foldButton;
        [SerializeField] private ActionPromptView promptView;
        [SerializeField] private ChipAnimator chipAnimator;

        [Header("Card UI")]
        [SerializeField] private List<CardView> playerCardViews;
        [SerializeField] private List<CardView> boardCardViews;

        private readonly List<PokerPlayer> _players = new List<PokerPlayer>();
        private readonly List<PokerCard> _communityCards = new List<PokerCard>();

        private PokerDeck _deck;
        private int _pot;
        private int _dealerIndex;
        private int _playerAction = -1; // 0 call,1 raise,2 fold

        private void Start()
        {
            CreateTable();
            HookButtons();
            StartCoroutine(RoundLoop());
        }

        private void HookButtons()
        {
            callButton.onClick.AddListener(() => _playerAction = 0);
            raiseButton.onClick.AddListener(() => _playerAction = 1);
            foldButton.onClick.AddListener(() => _playerAction = 2);
            SetActionButtons(false);
        }

        public void StartNewRoundManual()
        {
            StopAllCoroutines();
            StartCoroutine(RoundLoop());
        }

        private IEnumerator RoundLoop()
        {
            PrepareRound();
            yield return Preflop();
            if (ActivePlayers().Count > 1)
            {
                yield return Flop();
            }

            if (ActivePlayers().Count > 1)
            {
                yield return Turn();
            }

            if (ActivePlayers().Count > 1)
            {
                yield return River();
            }

            ResolveShowdown();
            RenderStaticUI(BettingStreet.Showdown);
        }

        private void PrepareRound()
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

            CollectBlind(NextSeat(_dealerIndex), smallBlind);
            CollectBlind(NextSeat(_dealerIndex, 2), bigBlind);
            RenderCards();
            RenderStaticUI(BettingStreet.Preflop);
        }

        private IEnumerator Preflop()
        {
            promptView.Show("Preflop 开始", Color.white);
            yield return BettingRound(BettingStreet.Preflop, NextSeat(_dealerIndex, 3));
        }

        private IEnumerator Flop()
        {
            DrawCommunity(3);
            ResetBets();
            RenderCards();
            RenderStaticUI(BettingStreet.Flop);
            promptView.Show("Flop", Color.cyan);
            yield return BettingRound(BettingStreet.Flop, NextSeat(_dealerIndex, 1));
        }

        private IEnumerator Turn()
        {
            DrawCommunity(1);
            ResetBets();
            RenderCards();
            RenderStaticUI(BettingStreet.Turn);
            promptView.Show("Turn", Color.cyan);
            yield return BettingRound(BettingStreet.Turn, NextSeat(_dealerIndex, 1));
        }

        private IEnumerator River()
        {
            DrawCommunity(1);
            ResetBets();
            RenderCards();
            RenderStaticUI(BettingStreet.River);
            promptView.Show("River", Color.cyan);
            yield return BettingRound(BettingStreet.River, NextSeat(_dealerIndex, 1));
        }

        private IEnumerator BettingRound(BettingStreet street, int startIndex)
        {
            var maxBet = _players.Max(p => p.CurrentBet);
            var pending = ActivePlayers().Where(p => !p.IsAllIn).Select(p => p.Name).ToHashSet();
            var index = startIndex;

            while (pending.Count > 0 && ActivePlayers().Count > 1)
            {
                var player = _players[index];
                index = NextSeat(index);

                if (player.IsFolded || player.IsAllIn)
                {
                    continue;
                }

                var callTarget = maxBet;
                if (player.IsBot)
                {
                    yield return new WaitForSeconds(botActionDelay);
                    var paid = player.BetTo(callTarget);
                    AddToPot(paid);
                    promptView.Show($"{player.Name} 选择 Call", Color.yellow);
                    pending.Remove(player.Name);
                    continue;
                }

                _playerAction = -1;
                SetActionButtons(true);
                promptView.Show($"{street} 轮到你：Call / Raise / Fold", Color.green);
                while (_playerAction < 0)
                {
                    yield return null;
                }

                SetActionButtons(false);
                if (_playerAction == 2)
                {
                    player.IsFolded = true;
                    promptView.Show($"{player.Name} Fold", Color.red);
                    pending.Remove(player.Name);
                    continue;
                }

                if (_playerAction == 1)
                {
                    var toCall = player.BetTo(callTarget);
                    var raise = player.RaiseBy(raiseStep);
                    AddToPot(toCall + raise);
                    maxBet = player.CurrentBet;
                    pending = ActivePlayers().Where(p => !p.IsAllIn && p.Name != player.Name).Select(p => p.Name).ToHashSet();
                    promptView.Show($"{player.Name} Raise +{raiseStep}", Color.magenta);
                }
                else
                {
                    var paid = player.BetTo(callTarget);
                    AddToPot(paid);
                    promptView.Show($"{player.Name} Call", Color.yellow);
                    pending.Remove(player.Name);
                }

                RenderStaticUI(street);
            }
        }

        private void ResolveShowdown()
        {
            var survivors = ActivePlayers();
            if (survivors.Count == 1)
            {
                survivors[0].Chips += _pot;
                stateText.text = $"{survivors[0].Name} 因其他玩家弃牌获胜，赢得底池 {_pot}";
                _dealerIndex = NextSeat(_dealerIndex);
                return;
            }

            var result = survivors
                .Select(p => new
                {
                    Player = p,
                    Hand = PokerHandEvaluator.Evaluate(p.HoleCards.Concat(_communityCards).ToList())
                })
                .OrderByDescending(x => x.Hand)
                .First();

            result.Player.Chips += _pot;
            stateText.text = $"赢家：{result.Player.Name}，牌型：{result.Hand.Rank}，底池：{_pot}";
            _dealerIndex = NextSeat(_dealerIndex);
        }

        private void AddToPot(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            var old = _pot;
            _pot += amount;
            chipAnimator.AnimatePot(old, _pot);
        }

        private void CollectBlind(int seat, int blind)
        {
            var paid = _players[seat].RaiseBy(blind);
            AddToPot(paid);
        }

        private void DrawCommunity(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _communityCards.Add(_deck.Draw());
            }
        }

        private void ResetBets()
        {
            foreach (var p in _players)
            {
                p.CurrentBet = 0;
            }
        }

        private int NextSeat(int from, int step = 1)
        {
            return (from + step) % _players.Count;
        }

        private List<PokerPlayer> ActivePlayers()
        {
            return _players.Where(p => !p.IsFolded).ToList();
        }

        private void SetActionButtons(bool enabled)
        {
            callButton.interactable = enabled;
            raiseButton.interactable = enabled;
            foldButton.interactable = enabled;
        }

        private void RenderStaticUI(BettingStreet street)
        {
            boardText.text = $"{street} | 公共牌：" + string.Join(" | ", _communityCards.Select(c => c.ToString()));
            playersText.text = string.Join("\n", _players.Select(p =>
                $"{p.Name} {(p.IsFolded ? "[Fold]" : "")} Bet:{p.CurrentBet} 筹码:{p.Chips}"));
        }

        private void RenderCards()
        {
            if (playerCardViews.Count >= 2)
            {
                playerCardViews[0].SetCard(_players[0].HoleCards[0], true);
                playerCardViews[1].SetCard(_players[0].HoleCards[1], true);
            }

            for (var i = 0; i < boardCardViews.Count; i++)
            {
                if (i < _communityCards.Count)
                {
                    boardCardViews[i].SetCard(_communityCards[i], true);
                }
            }
        }

        private void CreateTable()
        {
            _players.Clear();
            var userName = string.IsNullOrWhiteSpace(SessionContext.CurrentUser) ? "Player" : SessionContext.CurrentUser;
            _players.Add(new PokerPlayer(userName, initialChips, false));
            for (var i = 1; i <= 5; i++)
            {
                _players.Add(new PokerPlayer($"Bot-{i}", initialChips, true));
            }
        }
    }
}
