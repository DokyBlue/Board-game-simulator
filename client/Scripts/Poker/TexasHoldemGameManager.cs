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
using BoardGameSimulator.Poker;

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

    [Serializable]
    public class ServerGameState
    {
        public int pot;
        public string stage;
        public long currentTurnUserId;
        public ServerCard[] communityCards;
        public ServerPlayer[] players;
    }

    [Serializable]
    public class ServerPlayer
    {
        public long userId;
        public string username;
        public int chips;
        public int currentBet;
        public bool isFolded;
        public bool isAllIn;
        public string lastAction;
        public bool isBot;
        public ServerCard[] holeCards;
    }

    [Serializable]
    public class ServerCard
    {
        public string suit;
        public string rank;
    }

    [Serializable]
    public class OwnerChangedEvent
    {
        public long roomId;
        public long newOwnerUserId;
        public string @event;
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
        [SerializeField] private Button checkButton;
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
        private int _pot = 0;
        private int _dealerIndex;
        private bool _waitingForRoundChoice;
        private int _roundNumber = 1;
        private PlayerActionType _playerAction = PlayerActionType.None;
        private readonly Dictionary<string, string> _lastActions = new Dictionary<string, string>();
        private readonly Dictionary<string, BotStyle> _botStyles = new Dictionary<string, BotStyle>();
        private bool _isShowdownRevealed;
        private readonly List<string> _actionHistory = new List<string>();
        private const int MaxHistoryEntries = 160;
        private const int MaxRoomPlayers = 6;
        private bool _isWaitingForHostStart;

        private enum PlayerActionType
        {
            None,
            Check,
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

        private async void Start()
        {
            HookButtons();
            RenderRoomInfo();
            RefreshStrategyPanel();
            QueryHistory();

            // 如果 CurrentRoomId > 0，说明是联机模式
            if (SessionContext.CurrentRoomId > 0)
            {
                if (TcpNetworkManager.Instance != null)
                {
                    // 1. 订阅 TCP 收包事件
                    TcpNetworkManager.Instance.OnPacketReceived += OnNetworkPacket;

                    // 2. 在这里发起真正的 TCP 长连接拨号
                    bool isConnected = await TcpNetworkManager.Instance.ConnectAsync();

                    if (isConnected)
                    {
                        Debug.Log("[TCP] 成功连入 C++ 游戏服务器！");

                        // 3. 连上后的第一件事：向 C++ 发送 1001 指令，告诉它我是谁，进哪个房间
                        // 注意：这里的字段名需要和你 C++ 里解析的字段对应
                        string joinJson = $"{{\"roomId\": {SessionContext.CurrentRoomId}, \"userId\": {SessionContext.UserId}, \"username\": \"{SessionContext.CurrentUser}\"}}";
                        TcpNetworkManager.Instance.SendMessage(1001, joinJson);
                    }
                    else
                    {
                        Debug.LogError("[TCP] 连接 C++ 服务器失败！请检查 IP 和安全组端口。");
                        if (stateText != null) stateText.text = "连接游戏服务器失败，请重试";
                        return; // 没连上就直接终止后续逻辑
                    }

                    // 4. 界面初始化
                    _isWaitingForHostStart = true;
                    ToggleRoundChoiceButtons(SessionContext.IsRoomOwner);
                    if (stateText != null) stateText.text = SessionContext.IsRoomOwner ? "你是房主，点击 Next Round 开始对局" : "等待房主开始游戏";
                    if (promptView != null) promptView.Show("已连接 TCP，等待游戏开始...", Color.green);
                }
                return; // 联机模式直接 return，不再启动本地发牌协程
            }

            // 单机模式原逻辑
            CreateTable();
            StartCoroutine(RoundLoop());
        }

        private void OnDestroy()
        {
            // 退出场景时，务必注销事件，防止报错
            if (TcpNetworkManager.Instance != null)
            {
                TcpNetworkManager.Instance.OnPacketReceived -= OnNetworkPacket;
            }
        }

        private void OnNetworkPacket(uint msgCode, string body)
        {
            // 新增日志，拦截所有来自 C++ 的数据
            Debug.Log($"<color=#00FFFF>[下行 <- C++]</color> MsgCode: {msgCode}, JSON: {body}");

            if (msgCode == 1001)
            {
                ServerGameState user = JsonUtility.FromJson<ServerGameState>(body);
                SessionContext.UserId = user.players[0].userId;
            }

            if (msgCode != 3001)
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(body))
                {
                    Debug.LogWarning("[TCP] 收到空的 3001 包体，忽略。");
                    return;
                }

                ServerGameState serverState = JsonUtility.FromJson<ServerGameState>(body);
                _isWaitingForHostStart = false;

                ToggleRoundChoiceButtons(false);
                if (stateText != null)
                {
                    stateText.text = "游戏进行中...";
                }

                RenderServerState(serverState);
            }
            catch (Exception e)
            {
                Debug.LogError($"JSON 解析异常: {e.Message}\n收到数据: {body}");
            }
        }

        private void RenderServerState(ServerGameState state)
        {
            SessionContext.UserId = state.currentTurnUserId;
            // 测试打印读取到的数据
            //if (state != null)
            //{
            //    Debug.Log($"当前阶段: {state.stage}, 奖池: {state.pot}");
            //    if (state.players != null && state.players.Length > 0)
            //    {
            //        Debug.Log($"玩家1名字: {state.players[0].username}");
            //    }
            //}

            if (state == null)
            {
                return;
            }

            // 1. 更新底池 + 动画
            if (_pot != state.pot)
            {
                if (chipAnimator != null)
                {
                    chipAnimator.AnimatePot(_pot, state.pot);
                }

                _pot = state.pot;
            }

            if (boardText != null)
            {
                boardText.text = $"{state.stage} | 底池: {_pot}";
            }

            // 2. 更新公共牌
            if (boardCardViews != null)
            {
                for (int i = 0; i < boardCardViews.Count; i++)
                {
                    if (boardCardViews[i] == null)
                    {
                        continue;
                    }

                    if (state.communityCards != null && i < state.communityCards.Length && state.communityCards[i] != null)
                    {
                        var sc = state.communityCards[i];
                        var card = new PokerCard { Suit = ParseSuit(sc.suit), Rank = ParseRank(sc.rank) };
                        boardCardViews[i].SetCard(card, true);
                    }
                    else
                    {
                        boardCardViews[i].HideCard();
                    }
                }
            }

            // 安全兜底：等人阶段可能还没有 players 数组
            if (state.players == null)
            {
                SetActionButtons(false);
                if (playersText != null)
                {
                    playersText.text = "等待玩家加入...";
                }
                return;
            }

            // 3. 先计算公共回合信息（是否轮到我、是否可以 check）
            int tableMaxBet = 0;
            for (int i = 0; i < state.players.Length; i++)
            {
                if (state.players[i] != null)
                {
                    tableMaxBet = Mathf.Max(tableMaxBet, state.players[i].currentBet);
                }
            }

            bool foundLocalPlayer = false;
            bool myTurnAndCanAct = false;
            bool canCheck = false;
            List<string> playerSummary = new List<string>();

            // 4. 渲染玩家座位、动作、本人手牌
            for (int i = 0; i < state.players.Length; i++)
            {
                ServerPlayer sp = state.players[i];
                if (sp == null)
                {
                    continue;
                }

                bool isLocalPlayer = sp.userId == SessionContext.UserId;
                //Debug.Log("SessionContext.UserId: " + SessionContext.UserId + ", PlayerUserId: " + sp.userId);
                bool isMyTurn = state.currentTurnUserId == sp.userId;

                if (isLocalPlayer)
                {
                    foundLocalPlayer = true;
                    myTurnAndCanAct = isMyTurn && !sp.isFolded && !sp.isAllIn;
                    canCheck = sp.currentBet >= tableMaxBet;

                    if (myTurnAndCanAct && promptView != null)
                    {
                        promptView.Show("轮到你了！", Color.yellow);
                    }

                    if (playerCardViews != null && playerCardViews.Count >= 2)
                    {
                        if (sp.holeCards != null && sp.holeCards.Length >= 2 && sp.holeCards[0] != null && sp.holeCards[1] != null)
                        {
                            var c1 = new PokerCard { Suit = ParseSuit(sp.holeCards[0].suit), Rank = ParseRank(sp.holeCards[0].rank) };
                            var c2 = new PokerCard { Suit = ParseSuit(sp.holeCards[1].suit), Rank = ParseRank(sp.holeCards[1].rank) };
                            if (playerCardViews[0] != null) playerCardViews[0].SetCard(c1, true);
                            if (playerCardViews[1] != null) playerCardViews[1].SetCard(c2, true);
                        }
                        else
                        {
                            if (playerCardViews[0] != null) playerCardViews[0].HideCard();
                            if (playerCardViews[1] != null) playerCardViews[1].HideCard();
                        }
                    }
                }

                if (playerSeatViews != null && i < playerSeatViews.Count && playerSeatViews[i] != null)
                {
                    playerSeatViews[i].SetHighlight(isMyTurn);
                    var vp = new PokerPlayer(sp.username, sp.chips, sp.isBot || !isLocalPlayer)
                    {
                        CurrentBet = sp.currentBet,
                        IsFolded = sp.isFolded,
                        IsAllIn = sp.isAllIn
                    };

                    string actionText = sp.lastAction ?? "-";
                    playerSeatViews[i].UpdateSeat(vp, actionText);
                }

                playerSummary.Add($"{sp.username} Bet:{sp.currentBet} 筹码:{sp.chips} 动作:{(sp.lastAction ?? "-")}");
            }

            // 清空多余座位，避免脏 UI
            for (int i = state.players.Length; playerSeatViews != null && i < playerSeatViews.Count; i++)
            {
                if (playerSeatViews[i] != null)
                {
                    playerSeatViews[i].SetHighlight(false);
                    playerSeatViews[i].UpdateSeat(null, "-");
                }
            }

            if (playersText != null)
            {
                playersText.text = playerSummary.Count > 0 ? string.Join("\n", playerSummary) : "等待玩家加入...";
            }

            SetActionButtons(foundLocalPlayer && myTurnAndCanAct, canCheck);
        }

        private Suit ParseSuit(string s) => Enum.TryParse(s, out Suit res) ? res : Suit.Spades;
        private Rank ParseRank(string r) => Enum.TryParse(r, out Rank res) ? res : Rank.Two;

        public void StartNewRoundManual()
        {
            StartNextRound();
        }

        private IEnumerator RoundLoop()
        {
            _waitingForRoundChoice = false;
            _isShowdownRevealed = false;
            ToggleRoundChoiceButtons(false);
            SetActionButtons(false);
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
            HideCommunityCards();
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
            var canKeepChecking = true;
            var checkCount = 0;
            var checkablePlayerCount = ActivePlayers().Count(p => !p.IsAllIn && p.Chips > 0);

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
                    HandleBotDecision(player, decision, callTarget, ref maxBet, ref pending, ref canKeepChecking, ref checkCount, checkablePlayerCount);
                    RenderStaticUI(street);
                    continue;
                }

                _playerAction = PlayerActionType.None;
                var toCall = Mathf.Max(0, callTarget - player.CurrentBet);
                var canCheck = toCall == 0 && canKeepChecking;
                SetActionButtons(true, canCheck);
                promptView.Show(canCheck
                    ? $"{street} 轮到你：可 Check"
                    : $"{street} 轮到你：To Call {toCall}", Color.green);
                while (_playerAction == PlayerActionType.None)
                {
                    yield return null;
                }

                SetActionButtons(false);
                HandleHumanAction(player, _playerAction, callTarget, ref maxBet, ref pending, ref canKeepChecking, ref checkCount, checkablePlayerCount);
                RenderStaticUI(street);
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

        private void HandleBotDecision(
            PokerPlayer player,
            BotDecision decision,
            int callTarget,
            ref int maxBet,
            ref HashSet<string> pending,
            ref bool canKeepChecking,
            ref int checkCount,
            int checkablePlayerCount)
        {
            if (callTarget == player.CurrentBet && canKeepChecking && decision.Action == PlayerActionType.Call)
            {
                decision.Action = PlayerActionType.Check;
            }

            switch (decision.Action)
            {
                case PlayerActionType.Fold:
                    canKeepChecking = false;
                    player.IsFolded = true;
                    pending.Remove(player.Name);
                    RecordAction(player.Name, "Fold");
                    promptView.Show($"{player.Name} Fold", Color.red);
                    return;
                case PlayerActionType.AllIn:
                {
                    canKeepChecking = false;
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
                    canKeepChecking = false;
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
                case PlayerActionType.Check:
                {
                    if (callTarget != player.CurrentBet || !canKeepChecking)
                    {
                        goto default;
                    }

                    checkCount += 1;
                    RecordAction(player.Name, "Check");
                    promptView.Show($"{player.Name} Check", Color.yellow);
                    pending.Remove(player.Name);
                    if (checkCount >= checkablePlayerCount)
                    {
                        promptView.Show("所有玩家均 Check，进入下一阶段", Color.cyan);
                    }

                    return;
                }
                default:
                {
                    canKeepChecking = false;
                    var paid = player.BetTo(callTarget);
                    AddToPot(paid);
                    RecordAction(player.Name, "Call");
                    promptView.Show($"{player.Name} Call", Color.yellow);
                    pending.Remove(player.Name);
                    return;
                }
            }
        }

        private void HandleHumanAction(
            PokerPlayer player,
            PlayerActionType action,
            int callTarget,
            ref int maxBet,
            ref HashSet<string> pending,
            ref bool canKeepChecking,
            ref int checkCount,
            int checkablePlayerCount)
        {
            if (action == PlayerActionType.Check)
            {
                if (callTarget != player.CurrentBet || !canKeepChecking)
                {
                    action = PlayerActionType.Call;
                }
                else
                {
                    checkCount += 1;
                    RecordAction(player.Name, "Check");
                    promptView.Show($"{player.Name} Check", Color.yellow);
                    pending.Remove(player.Name);
                    if (checkCount >= checkablePlayerCount)
                    {
                        promptView.Show("所有玩家均 Check，进入下一阶段", Color.cyan);
                    }

                    return;
                }
            }

            if (action == PlayerActionType.Fold)
            {
                canKeepChecking = false;
                player.IsFolded = true;
                RecordAction(player.Name, "Fold");
                promptView.Show($"{player.Name} Fold", Color.red);
                pending.Remove(player.Name);
                return;
            }

            if (action == PlayerActionType.AllIn)
            {
                canKeepChecking = false;
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
                canKeepChecking = false;
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

            canKeepChecking = false;
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
                _isShowdownRevealed = false;
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

            _isShowdownRevealed = true;
            result.Player.Chips += _pot;
            RecordAction(result.Player.Name, "Winner");
            var winnerHoleCards = string.Join(" ", result.Player.HoleCards.Select(card => card.ToString()));
            stateText.text = $"赢家：{result.Player.Name}，手牌：{winnerHoleCards}，牌型：{result.Hand.Rank}，底池：{_pot}";
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

        private void SetActionButtons(bool enabled, bool canCheck = false)
        {
            callButton.interactable = enabled;
            if (checkButton != null)
            {
                checkButton.interactable = enabled && canCheck;
            }
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
                if (playerSeatViews != null && i < playerSeatViews.Count)
                {
                    string action = _lastActions.ContainsKey(_players[i].Name) ? _lastActions[_players[i].Name] : "-";
                    playerSeatViews[i].UpdateSeat(_players[i], action);
                }
            }

            if (playerSeatViews != null)
            {
                for (int i = _players.Count; i < playerSeatViews.Count; i++)
                {
                    playerSeatViews[i].UpdateSeat(null, "-");
                }
            }
        }


        private void HideCommunityCards()
        {
            if (boardCardViews == null)
            {
                return;
            }

            foreach (var cardView in boardCardViews)
            {
                if (cardView != null)
                {
                    cardView.HideCard();
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
                    var revealCard = _isShowdownRevealed || i < _communityCards.Count;
                    boardCardViews[i].SetCard(_communityCards[i], revealCard);
                }
                else
                {
                    boardCardViews[i].HideCard();
                }
            }
        }

        private void CreateTable()
        {
            _players.Clear();
            _botStyles.Clear();
            _lastActions.Clear();
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

        private void EnterRoundChoiceState()
        {
            _waitingForRoundChoice = true;
            var isOwnerOrOffline = SessionContext.IsRoomOwner || SessionContext.CurrentRoomId == 0;
            ToggleRoundChoiceButtons(isOwnerOrOffline);
            SetActionButtons(false);
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

        public void ResetChipsAndRestart()
        {
            if (!_waitingForRoundChoice)
            {
                return;
            }

            if (SessionContext.CurrentRoomId > 0 && !SessionContext.IsRoomOwner)
            {
                return;
            }

            if (SessionContext.CurrentRoomId > 0)
            {
                // 联机模式：由 C++ 服务端执行重置并广播最新状态 (MsgCode: 2004)
                if (TcpNetworkManager.Instance != null && TcpNetworkManager.Instance.IsConnected)
                {
                    TcpNetworkManager.Instance.SendMessage(2004, "{}");
                }
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

        //public void LeaveRoomAndBackLobby()
        //{
        //    if (SessionContext.CurrentRoomId <= 0 || lobbyApiClient == null)
        //    {
        //        SessionContext.ClearRoom();
        //        UnityEngine.SceneManagement.SceneManager.LoadScene(gameSelectionScene);
        //        return;
        //    }

        //    StartCoroutine(LeaveRoomCoroutine());
        //}

        private void HookButtons()
        {
            // 核心改变：所有游戏动作统一交由 SendNetworkAction 处理
            callButton.onClick.AddListener(() => SendNetworkAction(PlayerActionType.Call, 0));
            if (checkButton != null) checkButton.onClick.AddListener(() => SendNetworkAction(PlayerActionType.Check, 0));
            raiseButton.onClick.AddListener(() => SendNetworkAction(PlayerActionType.Raise, ParseRaiseAmount()));
            if (allInButton != null) allInButton.onClick.AddListener(() => SendNetworkAction(PlayerActionType.AllIn, 0));
            foldButton.onClick.AddListener(() => SendNetworkAction(PlayerActionType.Fold, 0));

            if (nextRoundButton != null)
            {
                nextRoundButton.onClick.AddListener(StartNextRound);
                nextRoundButton.gameObject.SetActive(SessionContext.IsRoomOwner || SessionContext.CurrentRoomId == 0);
                nextRoundButton.interactable = false;
            }

            if (resetChipsButton != null) { resetChipsButton.onClick.AddListener(ResetChipsAndRestart); resetChipsButton.gameObject.SetActive(SessionContext.IsRoomOwner || SessionContext.CurrentRoomId == 0); resetChipsButton.interactable = false; }
            if (leaveRoomButton != null) leaveRoomButton.onClick.AddListener(LeaveRoomAndBackLobby);
            if (historyQueryButton != null) historyQueryButton.onClick.AddListener(QueryHistory);
            if (historyClearButton != null) historyClearButton.onClick.AddListener(() => { if (historyQueryInput != null) historyQueryInput.text = string.Empty; QueryHistory(); });

            SetActionButtons(false);
        }

        private void SendNetworkAction(PlayerActionType action, int amount)
        {
            if (SessionContext.CurrentRoomId == 0)
            {
                // 单机模式：继续走本地协程赋值
                _playerAction = action;
                return;
            }

            // 联机模式：向 C++ 发送动作指令 (MsgCode: 2001)
            SetActionButtons(false); // 点击后立刻禁用按钮，防抖
            string json = $"{{\"action\":\"{action}\", \"amount\":{amount}}}";
            TcpNetworkManager.Instance.SendMessage(2001, json);
        }

        public void LeaveRoomAndBackLobby()
        {
            if (SessionContext.CurrentRoomId > 0 && TcpNetworkManager.Instance != null && TcpNetworkManager.Instance.IsConnected)
            {
                // 发送退出指令给 C++ (MsgCode: 2003)
                TcpNetworkManager.Instance.SendMessage(2003, "{}");
            }

            if (SessionContext.CurrentRoomId > 0 && lobbyApiClient != null)
            {
                StartCoroutine(LeaveRoomCoroutine());
                return;
            }

            SessionContext.ClearRoom();
            UnityEngine.SceneManagement.SceneManager.LoadScene(gameSelectionScene);
        }

        public void StartNextRound()
        {
            if (SessionContext.CurrentRoomId > 0)
            {
                // 房主发送开局指令 (MsgCode: 2002)
                if (SessionContext.IsRoomOwner && TcpNetworkManager.Instance != null)
                {
                    TcpNetworkManager.Instance.SendMessage(2002, "{}");
                }
                return;
            }

            if (!_waitingForRoundChoice) return;
            _roundNumber += 1;
            StopAllCoroutines();
            StartCoroutine(RoundLoop());
        }

        private IEnumerator LeaveRoomCoroutine()
        {
            var done = false;
            long roomId = SessionContext.CurrentRoomId;
            string token = SessionContext.AccessToken;

            StartCoroutine(lobbyApiClient.LeaveRoom(roomId, token, result =>
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
