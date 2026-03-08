using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BoardGameSimulator.Core;
using BoardGameSimulator.Networking;
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
        [SerializeField] private int defaultRaiseStep = 20;
        [SerializeField] private float botActionDelay = 0.5f;

        [Header("Text UI")]
        [SerializeField] private TMP_Text stateText;
        [SerializeField] private TMP_Text boardText;
        [SerializeField] private TMP_Text playersText;
        [SerializeField] private TMP_Text roomText;

        [Header("Seat UI")]
        [SerializeField] private List<PlayerSeatView> playerSeatViews;

        [Header("Action UI")]
        [SerializeField] private Button callButton;
        [SerializeField] private Button raiseButton;
        [SerializeField] private Button allInButton;
        [SerializeField] private Button foldButton;
        [SerializeField] private Button nextRoundButton;
        [SerializeField] private Button resetChipsButton;
        [SerializeField] private Button leaveRoomButton;
        [SerializeField] private Button historyQueryButton;
        [SerializeField] private Button historyClearButton;
        [SerializeField] private TMP_InputField raiseAmountInput;
        [SerializeField] private TMP_InputField historyQueryInput;
        [SerializeField] private TMP_Text historyText;
        [SerializeField] private ActionPromptView promptView;
        [SerializeField] private ChipAnimator chipAnimator;
        [SerializeField] private LobbyApiClient lobbyApiClient;

        [Header("Scene")]
        [SerializeField] private string gameSelectionScene = "GameSelection";

        [Header("Card UI")]
        [SerializeField] private List<CardView> playerCardViews;
        [SerializeField] private List<CardView> boardCardViews;

        private readonly List<PokerPlayer> _players = new List<PokerPlayer>();
        private readonly List<PokerCard> _communityCards = new List<PokerCard>();

        private PokerDeck _deck;
        private int _pot;
        private int _dealerIndex;
        private bool _waitingForRoundChoice;
        private int _roundNumber = 1;
        private PlayerActionType _playerAction = PlayerActionType.None;
        private readonly Dictionary<string, string> _lastActions = new Dictionary<string, string>();
        private readonly Dictionary<string, BotStyle> _botStyles = new Dictionary<string, BotStyle>();
        private readonly List<string> _actionHistory = new List<string>();
        private const int MaxHistoryEntries = 160;

        private enum PlayerActionType
        {
            None,
            Call,
            Raise,
            AllIn,
            Fold
        }

        private enum BotStyle
        {
            Tight,
            Balanced,
            Aggressive,
            Chaotic
        }

        private struct BotDecision
        {
            public PlayerActionType Action;
            public int RaiseAmount;
        }

        private void Start()
        {
            CreateTable();
            HookButtons();
            RenderRoomInfo();
            RefreshStrategyPanel();
            QueryHistory();
            StartCoroutine(RoundLoop());
        }

        private void HookButtons()
        {
            callButton.onClick.AddListener(() => _playerAction = PlayerActionType.Call);
            raiseButton.onClick.AddListener(() => _playerAction = PlayerActionType.Raise);
            if (allInButton != null)
            {
                allInButton.onClick.AddListener(() => _playerAction = PlayerActionType.AllIn);
            }
            foldButton.onClick.AddListener(() => _playerAction = PlayerActionType.Fold);
            if (nextRoundButton != null)
            {
                nextRoundButton.onClick.AddListener(StartNextRound);
                nextRoundButton.gameObject.SetActive(SessionContext.IsRoomOwner || SessionContext.CurrentRoomId == 0);
                nextRoundButton.interactable = false;
            }

            if (resetChipsButton != null)
            {
                resetChipsButton.onClick.AddListener(ResetChipsAndRestart);
                resetChipsButton.gameObject.SetActive(SessionContext.IsRoomOwner || SessionContext.CurrentRoomId == 0);
                resetChipsButton.interactable = false;
            }

            if (leaveRoomButton != null)
            {
                leaveRoomButton.onClick.AddListener(LeaveRoomAndBackLobby);
            }

            if (historyQueryButton != null)
            {
                historyQueryButton.onClick.AddListener(QueryHistory);
            }

            if (historyClearButton != null)
            {
                historyClearButton.onClick.AddListener(() =>
                {
                    if (historyQueryInput != null)
                    {
                        historyQueryInput.text = string.Empty;
                    }

                    QueryHistory();
                });
            }

            SetActionButtons(false);
        }

        public void StartNewRoundManual()
        {
            StartNextRound();
        }

        private IEnumerator RoundLoop()
        {
            _waitingForRoundChoice = false;
            ToggleRoundChoiceButtons(false);
            PrepareRound();

            if (PlayersWithChips().Count <= 1)
            {
                ResolveShowdown();
                RenderStaticUI(BettingStreet.Showdown);
                EnterRoundChoiceState();
                yield break;
            }

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
            EnterRoundChoiceState();
        }

        private void PrepareRound()
        {
            ClearAllSeatHighlights();
            _pot = 0;
            _communityCards.Clear();
            _deck = new PokerDeck();
            _deck.Shuffle();
            _lastActions.Clear();
            AppendHistory($"---- Round {_roundNumber} ----");

            foreach (var player in _players)
            {
                player.ResetForRound();
                if (player.Chips <= 0)
                {
                    player.IsFolded = true;
                    _lastActions[player.Name] = "Out";
                    continue;
                }

                player.HoleCards.Add(_deck.Draw());
                player.HoleCards.Add(_deck.Draw());
                _lastActions[player.Name] = "Waiting";
            }

            var blindSeats = PlayersWithChips().Select(p => _players.IndexOf(p)).ToList();
            if (blindSeats.Count >= 2)
            {
                CollectBlind(NextSeatWithChips(_dealerIndex), smallBlind);
                CollectBlind(NextSeatWithChips(_dealerIndex, 2), bigBlind);
            }

            RenderCards();
            RefreshStrategyPanel();
            RenderStaticUI(BettingStreet.Preflop);
        }

        private IEnumerator Preflop()
        {
            promptView.Show("Preflop 开始", Color.white);
            yield return BettingRound(BettingStreet.Preflop, NextSeatWithChips(_dealerIndex, 3));
        }

        private IEnumerator Flop()
        {
            DrawCommunity(3);
            ResetBets();
            RenderCards();
            RenderStaticUI(BettingStreet.Flop);
            promptView.Show("Flop", Color.cyan);
            yield return BettingRound(BettingStreet.Flop, NextSeatWithChips(_dealerIndex, 1));
        }

        private IEnumerator Turn()
        {
            DrawCommunity(1);
            ResetBets();
            RenderCards();
            RenderStaticUI(BettingStreet.Turn);
            promptView.Show("Turn", Color.cyan);
            yield return BettingRound(BettingStreet.Turn, NextSeatWithChips(_dealerIndex, 1));
        }

        private IEnumerator River()
        {
            DrawCommunity(1);
            ResetBets();
            RenderCards();
            RenderStaticUI(BettingStreet.River);
            promptView.Show("River", Color.cyan);
            yield return BettingRound(BettingStreet.River, NextSeatWithChips(_dealerIndex, 1));
        }

        private IEnumerator BettingRound(BettingStreet street, int startIndex)
        {
            var maxBet = _players.Max(p => p.CurrentBet);
            var pending = ActivePlayers().Where(p => !p.IsAllIn).Select(p => p.Name).ToHashSet();
            var index = startIndex;

            while (pending.Count > 0 && ActivePlayers().Count > 1)
            {
                var player = _players[index];

                // 清除旧高亮并设置当前行动玩家的高亮
                ClearAllSeatHighlights();
                if (index < playerSeatViews.Count)
                {
                    playerSeatViews[index].SetHighlight(true);
                }

                index = NextSeat(index);

                if (player.IsFolded || player.IsAllIn || player.Chips <= 0)
                {
                    continue;
                }

                var callTarget = maxBet;
                if (player.IsBot)
                {
                    yield return new WaitForSeconds(botActionDelay);
                    var decision = BuildBotDecision(player, street, callTarget);
                    HandleBotDecision(player, decision, ref maxBet, ref pending);
                    RenderStaticUI(street);
                    continue;
                }

                _playerAction = PlayerActionType.None;
                SetActionButtons(true);
                var toCall = Mathf.Max(0, callTarget - player.CurrentBet);
                promptView.Show($"{street} 轮到你：To Call {toCall}", Color.green);
                while (_playerAction == PlayerActionType.None)
                {
                    yield return null;
                }

                SetActionButtons(false);
                HandleHumanAction(player, _playerAction, callTarget, ref maxBet, ref pending);
                RenderStaticUI(street);
                index = NextSeat(index);
            }
            ClearAllSeatHighlights(); 
        }

        private BotDecision BuildBotDecision(PokerPlayer player, BettingStreet street, int callTarget)
        {
            var toCall = Mathf.Max(0, callTarget - player.CurrentBet);
            var board = player.HoleCards.Concat(_communityCards).ToList();
            var handScore = PokerHandEvaluator.Evaluate(board);
            var strength = RankScore(handScore.Rank);
            var potPressure = _pot <= 0 ? 0f : (float)toCall / _pot;
            var style = GetBotStyle(player.Name);
            var roll = UnityEngine.Random.value;

            if (player.Chips <= toCall)
            {
                if (strength >= 5 || roll > 0.7f)
                {
                    return new BotDecision { Action = PlayerActionType.AllIn };
                }

                return new BotDecision { Action = PlayerActionType.Fold };
            }

            var raiseBase = Mathf.Max(defaultRaiseStep, bigBlind);
            var stageFactor = street switch
            {
                BettingStreet.Preflop => 1,
                BettingStreet.Flop => 2,
                BettingStreet.Turn => 3,
                _ => 4
            };

            switch (style)
            {
                case BotStyle.Tight:
                    if (strength <= 1 && potPressure > 0.25f)
                    {
                        return new BotDecision { Action = PlayerActionType.Fold };
                    }

                    if (strength >= 6 && roll > 0.4f)
                    {
                        return new BotDecision { Action = PlayerActionType.Raise, RaiseAmount = raiseBase * stageFactor };
                    }

                    return new BotDecision { Action = PlayerActionType.Call };

                case BotStyle.Aggressive:
                    if (strength >= 4 && roll > 0.2f)
                    {
                        var raise = raiseBase * (stageFactor + 1);
                        if (raise >= player.Chips - toCall)
                        {
                            return new BotDecision { Action = PlayerActionType.AllIn };
                        }

                        return new BotDecision { Action = PlayerActionType.Raise, RaiseAmount = raise };
                    }

                    if (strength <= 1 && potPressure > 0.4f)
                    {
                        return new BotDecision { Action = PlayerActionType.Fold };
                    }

                    return new BotDecision { Action = PlayerActionType.Call };

                case BotStyle.Chaotic:
                    if (roll < 0.18f)
                    {
                        return new BotDecision { Action = PlayerActionType.Fold };
                    }

                    if (roll > 0.88f)
                    {
                        return new BotDecision { Action = PlayerActionType.AllIn };
                    }

                    if (roll > 0.48f)
                    {
                        return new BotDecision { Action = PlayerActionType.Raise, RaiseAmount = raiseBase * UnityEngine.Random.Range(1, 5) };
                    }

                    return new BotDecision { Action = PlayerActionType.Call };

                default:
                    if (strength <= 1 && potPressure > 0.35f)
                    {
                        return new BotDecision { Action = PlayerActionType.Fold };
                    }

                    if (strength >= 5 && roll > 0.5f)
                    {
                        return new BotDecision { Action = PlayerActionType.Raise, RaiseAmount = raiseBase * stageFactor };
                    }

                    if (strength >= 7 && roll > 0.25f)
                    {
                        return new BotDecision { Action = PlayerActionType.AllIn };
                    }

                    return new BotDecision { Action = PlayerActionType.Call };
            }
        }

        private void HandleBotDecision(PokerPlayer player, BotDecision decision, ref int maxBet, ref HashSet<string> pending)
        {
            var callTarget = maxBet;
            switch (decision.Action)
            {
                case PlayerActionType.Fold:
                    player.IsFolded = true;
                    pending.Remove(player.Name);
                    RecordAction(player.Name, "Fold");
                    promptView.Show($"{player.Name} Fold", Color.red);
                    return;
                case PlayerActionType.AllIn:
                {
                    var paidToCall = player.BetTo(callTarget);
                    var extra = player.AllIn();
                    AddToPot(paidToCall + extra);
                    if (player.CurrentBet > maxBet)
                    {
                        maxBet = player.CurrentBet;
                        pending = ActivePlayers().Where(p => !p.IsAllIn && p.Name != player.Name).Select(p => p.Name).ToHashSet();
                    }
                    else
                    {
                        pending.Remove(player.Name);
                    }

                    RecordAction(player.Name, "AllIn");
                    promptView.Show($"{player.Name} All in", new Color(1f, 0.5f, 0f));
                    return;
                }
                case PlayerActionType.Raise:
                {
                    var paidToCall = player.BetTo(callTarget);
                    var raiseAmount = Mathf.Max(defaultRaiseStep, decision.RaiseAmount);
                    var paidRaise = player.RaiseBy(raiseAmount);
                    if (paidRaise <= 0)
                    {
                        pending.Remove(player.Name);
                        return;
                    }

                    AddToPot(paidToCall + paidRaise);
                    maxBet = player.CurrentBet;
                    pending = ActivePlayers().Where(p => !p.IsAllIn && p.Name != player.Name).Select(p => p.Name).ToHashSet();
                    RecordAction(player.Name, $"Raise +{paidRaise}");
                    promptView.Show($"{player.Name} Raise +{paidRaise}", Color.magenta);
                    return;
                }
                default:
                {
                    var paid = player.BetTo(callTarget);
                    AddToPot(paid);
                    RecordAction(player.Name, "Call");
                    promptView.Show($"{player.Name} Call", Color.yellow);
                    pending.Remove(player.Name);
                    return;
                }
            }
        }

        private void HandleHumanAction(PokerPlayer player, PlayerActionType action, int callTarget, ref int maxBet, ref HashSet<string> pending)
        {
            if (action == PlayerActionType.Fold)
            {
                player.IsFolded = true;
                RecordAction(player.Name, "Fold");
                promptView.Show($"{player.Name} Fold", Color.red);
                pending.Remove(player.Name);
                return;
            }

            if (action == PlayerActionType.AllIn)
            {
                var paidToCall = player.BetTo(callTarget);
                var extra = player.AllIn();
                AddToPot(paidToCall + extra);
                if (player.CurrentBet > maxBet)
                {
                    maxBet = player.CurrentBet;
                    pending = ActivePlayers().Where(p => !p.IsAllIn && p.Name != player.Name).Select(p => p.Name).ToHashSet();
                }
                else
                {
                    pending.Remove(player.Name);
                }

                RecordAction(player.Name, "AllIn");
                promptView.Show($"{player.Name} All in", new Color(1f, 0.5f, 0f));
                return;
            }

            if (action == PlayerActionType.Raise)
            {
                var paidToCall = player.BetTo(callTarget);
                var raiseAmount = ParseRaiseAmount();
                var paidRaise = player.RaiseBy(raiseAmount);
                AddToPot(paidToCall + paidRaise);
                maxBet = player.CurrentBet;
                pending = ActivePlayers().Where(p => !p.IsAllIn && p.Name != player.Name).Select(p => p.Name).ToHashSet();
                RecordAction(player.Name, $"Raise +{paidRaise}");
                promptView.Show($"{player.Name} Raise +{paidRaise}", Color.magenta);
                return;
            }

            var paidCall = player.BetTo(callTarget);
            AddToPot(paidCall);
            RecordAction(player.Name, "Call");
            promptView.Show($"{player.Name} Call", Color.yellow);
            pending.Remove(player.Name);
        }

        private int ParseRaiseAmount()
        {
            if (raiseAmountInput == null)
            {
                return defaultRaiseStep;
            }

            if (!int.TryParse(raiseAmountInput.text, out var raiseAmount) || raiseAmount <= 0)
            {
                return defaultRaiseStep;
            }

            return raiseAmount;
        }

        private BotStyle GetBotStyle(string playerName)
        {
            if (_botStyles.TryGetValue(playerName, out var style))
            {
                return style;
            }

            var fallback = (Math.Abs(playerName.GetHashCode()) % 4) switch
            {
                0 => BotStyle.Tight,
                1 => BotStyle.Balanced,
                2 => BotStyle.Aggressive,
                _ => BotStyle.Chaotic
            };
            _botStyles[playerName] = fallback;
            return fallback;
        }

        private static int RankScore(HandRank rank)
        {
            return rank switch
            {
                HandRank.HighCard => 1,
                HandRank.OnePair => 2,
                HandRank.TwoPairs => 3,
                HandRank.ThreeOfAKind => 4,
                HandRank.Straight => 5,
                HandRank.Flush => 6,
                HandRank.FullHouse => 7,
                HandRank.FourOfAKind => 8,
                HandRank.StraightFlush => 9,
                HandRank.RoyalFlush => 10,
                _ => 0
            };
        }

        private void ShowRemainingChips()
        {
            
        }

        private void ResolveShowdown()
        {
            var survivors = ActivePlayers();
            if (survivors.Count == 0)
            {
                stateText.text = "没有可参与玩家，请房主重置筹码后再开始";
                return;
            }

            if (survivors.Count == 1)
            {
                survivors[0].Chips += _pot;
                RecordAction(survivors[0].Name, "Winner");
                stateText.text = $"{survivors[0].Name} 因其他玩家弃牌获胜，赢得底池 {_pot}";
                _dealerIndex = NextSeatWithChips(_dealerIndex);
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
            RecordAction(result.Player.Name, "Winner");
            stateText.text = $"赢家：{result.Player.Name}，牌型：{result.Hand.Rank}，底池：{_pot}";
            _dealerIndex = NextSeatWithChips(_dealerIndex);
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

        private int NextSeatWithChips(int from, int step = 1)
        {
            var seat = from;
            var moved = 0;
            while (moved < step)
            {
                seat = NextSeat(seat);
                if (_players[seat].Chips > 0)
                {
                    moved += 1;
                }
            }

            return seat;
        }

        private List<PokerPlayer> ActivePlayers()
        {
            return _players.Where(p => !p.IsFolded).ToList();
        }

        private List<PokerPlayer> PlayersWithChips()
        {
            return _players.Where(p => p.Chips > 0).ToList();
        }

        private void SetActionButtons(bool enabled)
        {
            callButton.interactable = enabled;
            raiseButton.interactable = enabled;
            foldButton.interactable = enabled;
            if (allInButton != null)
            {
                allInButton.interactable = enabled;
            }

            if (raiseAmountInput != null)
            {
                raiseAmountInput.interactable = enabled;
            }
        }

        private void RenderStaticUI(BettingStreet street)
        {
            // 更新公共牌信息
            boardText.text = $"{street} | 公共牌：" + string.Join(" | ", _communityCards.Select(c => c.ToString()));

            if (playersText != null)
            {
                playersText.text = string.Join("\n", _players.Select(p =>
                    $"{p.Name} {(p.IsFolded ? "[Fold]" : "")} {(p.IsAllIn ? "[AllIn]" : "")} Bet:{p.CurrentBet} 筹码:{p.Chips} 动作:{(_lastActions.ContainsKey(p.Name) ? _lastActions[p.Name] : "-")}"));
            }

            // 将数据分发给每个独立的座位 UI
            for (int i = 0; i < _players.Count; i++)
            {
                // 确保场景中绑定的座位 UI 数量足够
                if (playerSeatViews != null && i < playerSeatViews.Count)
                {
                    string action = _lastActions.ContainsKey(_players[i].Name) ? _lastActions[_players[i].Name] : "-";
                    playerSeatViews[i].UpdateSeat(_players[i], action);
                }
            }
        }

        private void RenderCards()
        {
            if (playerCardViews.Count >= 2 && _players[0].HoleCards.Count >= 2)
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
            _botStyles.Clear();
            var userName = string.IsNullOrWhiteSpace(SessionContext.CurrentUser) ? "Player" : SessionContext.CurrentUser;
            _players.Add(new PokerPlayer(userName, initialChips, false));
            for (var i = 1; i <= 5; i++)
            {
                var botName = $"Bot-{i}";
                _players.Add(new PokerPlayer(botName, initialChips, true));
                _botStyles[botName] = (Math.Abs(botName.GetHashCode()) % 4) switch
                {
                    0 => BotStyle.Tight,
                    1 => BotStyle.Balanced,
                    2 => BotStyle.Aggressive,
                    _ => BotStyle.Chaotic
                };
            }
        }

        private void RecordAction(string playerName, string action)
        {
            _lastActions[playerName] = action;
            AppendHistory($"R{_roundNumber} | {playerName}: {action}");
            RefreshStrategyPanel();
        }

        private void AppendHistory(string entry)
        {
            _actionHistory.Add(entry);
            if (_actionHistory.Count > MaxHistoryEntries)
            {
                _actionHistory.RemoveAt(0);
            }

            QueryHistory();
        }

        private void QueryHistory()
        {
            if (historyText == null)
            {
                return;
            }

            var keyword = historyQueryInput == null ? string.Empty : historyQueryInput.text.Trim();
            var rows = _actionHistory
                .Where(item => string.IsNullOrWhiteSpace(keyword) || item.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .TakeLast(20)
                .ToList();

            historyText.text = rows.Count == 0
                ? "暂无匹配历史记录"
                : string.Join("\n", rows);
        }

        private void RefreshStrategyPanel()
        {
            if (promptView == null)
            {
                return;
            }

            var lines = _players
                .Where(p => p.IsBot)
                .Select(p =>
                {
                    var action = _lastActions.TryGetValue(p.Name, out var lastAction) ? lastAction : "Waiting";
                    //return $"{p.Name} | {BotStyleDescription(GetBotStyle(p.Name))} | 最近动作: {action}";
                    return $"{p.Name} | 最近动作: {action}";
                });
            promptView.SetStrategySnapshot(string.Join("\n", lines));
        }

        //private static string BotStyleDescription(BotStyle style)
        //{
        //    return style switch
        //    {
        //        BotStyle.Tight => "Tight：保守，仅强牌重注",
        //        BotStyle.Balanced => "Balanced：中庸，按压力调整",
        //        BotStyle.Aggressive => "Aggressive：激进，偏好加注",
        //        _ => "Chaotic：随机，波动较大"
        //    };
        //}

        private void EnterRoundChoiceState()
        {
            _waitingForRoundChoice = true;
            var isOwnerOrOffline = SessionContext.IsRoomOwner || SessionContext.CurrentRoomId == 0;
            ToggleRoundChoiceButtons(isOwnerOrOffline);
            if (isOwnerOrOffline)
            {
                promptView.Show("本轮结束：房主可选择下一轮或重置筹码", Color.white);
            }
            else
            {
                promptView.Show("本轮结束，等待房主开始下一轮", Color.white);
            }
        }

        private void ToggleRoundChoiceButtons(bool enabled)
        {
            if (nextRoundButton != null)
            {
                nextRoundButton.interactable = enabled;
            }

            if (resetChipsButton != null)
            {
                resetChipsButton.interactable = enabled;
            }
        }

        public void StartNextRound()
        {
            if (!_waitingForRoundChoice)
            {
                return;
            }

            _roundNumber += 1;
            StopAllCoroutines();
            StartCoroutine(RoundLoop());
        }

        public void ResetChipsAndRestart()
        {
            if (!_waitingForRoundChoice)
            {
                return;
            }

            foreach (var player in _players)
            {
                player.Chips = initialChips;
            }

            _roundNumber = 1;
            _actionHistory.Clear();
            QueryHistory();
            StopAllCoroutines();
            StartCoroutine(RoundLoop());
        }
        private void ClearAllSeatHighlights()
        {
            if (playerSeatViews == null) return;
            foreach (var seat in playerSeatViews)
            {
                if (seat != null) seat.SetHighlight(false);
            }
        }

        public void LeaveRoomAndBackLobby()
        {
            if (SessionContext.CurrentRoomId <= 0 || lobbyApiClient == null)
            {
                SessionContext.ClearRoom();
                UnityEngine.SceneManagement.SceneManager.LoadScene(gameSelectionScene);
                return;
            }

            StartCoroutine(LeaveRoomCoroutine());
        }

        private IEnumerator LeaveRoomCoroutine()
        {
            var done = false;
            StartCoroutine(lobbyApiClient.LeaveRoom(SessionContext.CurrentRoomId, SessionContext.AccessToken, result =>
            {
                done = true;
                if (!result.Success)
                {
                    Debug.LogWarning($"退出房间失败：{result.Message}");
                }
            }));

            while (!done)
            {
                yield return null;
            }

            SessionContext.ClearRoom();
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameSelectionScene);
        }

        private void RenderRoomInfo()
        {
            if (roomText == null)
            {
                return;
            }

            if (SessionContext.CurrentRoomId > 0)
            {
                roomText.text = $"大厅代码：{SessionContext.CurrentRoomCode}";
                return;
            }

            roomText.text = "当前模式：单机";
        }
    }
}
