using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System;

using Unknown6656.Common;
using System.Data.Common;

namespace SKHEIJO
{
    public readonly struct Card
        : IComparable<Card>
        , IEquatable<Card>
    {
        public const int MinValue = -2;
        public const int MaxValue = 12;

        public int Value { get; init; }


        public Card(int value)
            : this() => Value = value >= MinValue && value <= MaxValue ? value : throw new ArgumentOutOfRangeException(nameof(value));

        public readonly override bool Equals(object? obj) => obj is Card c && Equals(c);

        public readonly override int GetHashCode() => Value;

        public readonly override string ToString() => Value.ToString().PadLeft(2);

        public readonly int CompareTo(Card other) => Value.CompareTo(other.Value);

        public readonly bool Equals(Card other) => Value == other.Value;

        public static bool operator ==(Card left, Card right) => left.Equals(right);

        public static bool operator !=(Card left, Card right) => !(left == right);

        public static bool operator <(Card left, Card right) => left.CompareTo(right) < 0;

        public static bool operator <=(Card left, Card right) => left.CompareTo(right) <= 0;

        public static bool operator >(Card left, Card right) => left.CompareTo(right) > 0;

        public static bool operator >=(Card left, Card right) => left.CompareTo(right) >= 0;
    }

    public sealed class PlayerState
    {
        public static (int rows, int columns) InitialDimensions = (2, 2);// (3, 4);

        public Game Game { get; }
        public Player Player { get; }
        public (int rows, int columns) Dimensions;
        public (Card card, bool visible)[,] GameField;
        public Card? CurrentlyDrawnCard = null;

        public int VisiblePoints => GameField.Cast<(Card c, bool v)>().Aggregate(0, (s, t) => s + (t.v ? t.c.Value : 0));

        public int VisibleCount => GameField.Cast<(Card c, bool v)>().Count(t => t.v);

        public bool IsFull => VisibleCount >= Dimensions.rows * Dimensions.columns;


        internal PlayerState(Game game, Player player)
        {
            Game = game;
            Player = player;
            Dimensions = InitialDimensions;
            GameField = new (Card, bool)[Dimensions.rows, Dimensions.columns];
        }

        public Card[] RemoveColumn(int column_index)
        {
            column_index = Math.Max(0, Math.Min(column_index, Dimensions.columns - 1));
            (Card card, bool visible)[,] @new = new (Card, bool)[Dimensions.rows, Dimensions.columns - 1];
            Card[] removed = Enumerable.Range(0, Dimensions.rows).ToArray(row => GameField[row, column_index].card);

            for (int row = 0; row < Dimensions.rows; ++row)
                for (int col = Dimensions.columns - 2; col >= 0; --col)
                    @new[row, col] = GameField[row, col >= column_index ? col + 1 : col];

            --Dimensions.columns;
            GameField = @new;

            return removed;
        }

        public override string ToString()
        {
            StringBuilder sb = new();

            sb.Append($"P={Player}, DIM=c:{Dimensions.columns},r:{Dimensions.rows}");

            for (int row = 0; row < Dimensions.rows; ++row)
                sb.Append($"\n|{Enumerable.Range(0, Dimensions.columns).Select(col => GameField[row, col].card + (GameField[row, col].visible ? "V" : "H")).StringJoin(",")}|");

            return sb.ToString();
        }

        public override int GetHashCode() => Player.GetHashCode();

        public override bool Equals(object? obj) => Player.Equals((obj as PlayerState)?.Player);
    }

    public sealed class Game
    {
        public static int MAX_PLAYERS = 10;

        private GameState _state;
        private GameWaitingFor _waiting;
        private Player? _final_round_initiator;

        public Stack<Card> DrawPile { get; }
        public Stack<Card> DiscardPile { get; }
        public List<PlayerState> Players { get; }
        public int CurrentPlayerIndex { get; private set; }

        public GameState CurrentGameState
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    $"Game state changed: {_state} -> {value}".Log(LogSource.Game);

                    _state = value;

                    OnGameStateChanged?.Invoke(this);
                }
            }
        }

        public GameWaitingFor WaitingFor
        {
            get => _waiting;
            set
            {
                if (_waiting != value)
                {
                    $"Game waiting for changed: {_waiting} -> {value}".Log(LogSource.Game);

                    _waiting = value;

                    OnGameStateChanged?.Invoke(this);
                }
            }
        }

        public Card? DiscardedCard => DiscardPile.TryPeek(out Card card) ? card : null;

        public PlayerState? CurrentPlayer => CurrentPlayerIndex >= 0 && CurrentPlayerIndex < Players.Count ? Players[CurrentPlayerIndex] : null;


        public event Action<Game, Player>? OnPlayerAdded;
        public event Action<Game, Player>? OnPlayerRemoved;
        public event Action<Game>? OnGameStateChanged;


        public Game(IEnumerable<Player>? players = null)
        {
            Players = (players ?? Array.Empty<Player>()).ToList(p => new PlayerState(this, p));
            CurrentGameState = GameState.Stopped;
            NextPlayer(0);
            DrawPile = new();
            DiscardPile = new();
        }

        public override string ToString() => $"{Players.Count} player(s), {CurrentGameState}";

        public bool TryAddPlayer(Player player, out int player_index)
        {
            player_index = Players.Count;

            if (CurrentGameState is GameState.Stopped && Players.None(p => player.Equals(p.Player)) && Players.Count < MAX_PLAYERS)
            {
                Players.Add(new(this, player));
                OnPlayerAdded?.Invoke(this, player);
                OnGameStateChanged?.Invoke(this);

                $"Player joined: {player}".Log(LogSource.Game);

                return true;
            }

            return false;
        }

        public bool RemovePlayer(Player player) => RemovePlayer(Players.FindIndex(p => p.Player.Equals(player)));

        public bool RemovePlayer(int index)
        {
            if (index >= 0 && index < Players.Count)
            {
                Player player = Players[index].Player;

                if (player.Equals(_final_round_initiator))
                    _final_round_initiator = null;

                if (CurrentPlayerIndex == index && CurrentPlayer?.CurrentlyDrawnCard is Card card)
                    DiscardPile.Push(card);
                else if (CurrentPlayerIndex > index)
                    --CurrentPlayerIndex;

                for (int i = Players.Count - 2; i >= index; --i)
                    Players[i] = Players[i + 1];

                Players.RemoveAt(Players.Count - 1);
                NextPlayer(CurrentPlayerIndex);

                OnPlayerRemoved?.Invoke(this, player);
                OnGameStateChanged?.Invoke(this);

                $"Player left: {player}".Log(LogSource.Game);

                return true;
            }
            else
                return false;
        }

        public int NextPlayer() => NextPlayer(CurrentPlayerIndex + 1);

        public int NextPlayer(int player_index)
        {
            CurrentPlayerIndex = Players.Count == 0 ? 0 : (player_index + Players.Count) % Players.Count;

            if (CurrentPlayer?.VisibleCount is int visible)
                WaitingFor = visible < 2 ? GameWaitingFor.Uncover : GameWaitingFor.Draw;

            $"Next player: {CurrentPlayerIndex}".Log(LogSource.Game);

            return CurrentPlayerIndex;
        }

        public void DealCardsAndRestart(int total_cards = 150, int first_player = 0)
        {
            "Restarting game ...".Log(LogSource.Game);

            total_cards = Math.Max(Math.Max(total_cards, 150), Players.Count * 15 + 30);

            Card[] cards = Enumerable.Range(0, total_cards).PartitionByArraySize(15).SelectMany(arr => arr.Select(i => new Card(i % 15 - 2))).ToArray();

            cards.Shuffle();

            int index = 0;

            for (int i = 0; i < Players.Count; i++)
            {
                PlayerState player = Players[i];

                player.CurrentlyDrawnCard = null;
                player.Dimensions = PlayerState.InitialDimensions;
                player.GameField = new (Card, bool)[player.Dimensions.rows, player.Dimensions.columns];

                for (int row = 0; row < player.Dimensions.rows; ++row)
                    for (int col = 0; col < player.Dimensions.columns; ++col)
                        player.GameField[row, col] = (cards[index++], false);
            }

            DrawPile.Clear();
            DiscardPile.Clear();
            DiscardPile.Push(cards[index++]);

            while (index < cards.Length)
                DrawPile.Push(cards[index++]);

            CurrentGameState = GameState.Running;
            NextPlayer(first_player);
            _final_round_initiator = null;
        }


        public bool CurrentPlayer___SwapDrawn(int row, int column)
        {
            if (WaitingFor == GameWaitingFor.Play && CurrentPlayer?.CurrentlyDrawnCard is Card drawn)
            {
                $"Current: swap drawn card with {row}-{column}".Log(LogSource.Game);

                CurrentPlayer.CurrentlyDrawnCard = CurrentPlayer.GameField[row, column].card;
                CurrentPlayer.GameField[row, column] = (drawn, true);
                WaitingFor = GameWaitingFor.Discard;

                return true;
            }
            else
                return false;
        }

        public bool CurrentPlayer___DiscardDrawn()
        {
            if (WaitingFor is GameWaitingFor.Discard or GameWaitingFor.Play && CurrentPlayer?.CurrentlyDrawnCard is Card card)
            {
                "Current: discard drawn".Log(LogSource.Game);

                DiscardPile.Push(card);
                CurrentPlayer.CurrentlyDrawnCard = null;
                WaitingFor = WaitingFor is GameWaitingFor.Discard ? GameWaitingFor.NextPlayer : GameWaitingFor.Uncover;

                return true;
            }

            return false;
        }

        public bool CurrentPlayer___UncoverCard(int row, int column)
        {
            if (WaitingFor == GameWaitingFor.Uncover && CurrentPlayer?.GameField[row, column] is { card: Card card, visible: false })
            {
                $"Current: uncover card {row}-{column}".Log(LogSource.Game);

                CurrentPlayer.GameField[row, column] = (card, true);

                if (CurrentPlayer.VisibleCount < 2)
                    OnGameStateChanged?.Invoke(this);
                else
                    WaitingFor = GameWaitingFor.NextPlayer;

                return true;
            }
            else
                return false;
        }

        public bool CurrentPlayer___DrawCard(bool from_discard_pile)
        {
            if (WaitingFor == GameWaitingFor.Draw && CurrentPlayer is { CurrentlyDrawnCard: null })
            {
                $"Current: draw card from {(from_discard_pile ? "discard" : "draw")} pile".Log(LogSource.Game);

                CurrentPlayer.CurrentlyDrawnCard = (from_discard_pile ? DiscardPile : DrawPile).Pop();
                WaitingFor = GameWaitingFor.Play;

                return true;
            }
            else
                return false;
        }

        public void CurrentPlayer___RemoveFullColumnsOfIdenticalCards(out int column_count)
        {
            column_count = 0;

            if (CurrentPlayer is PlayerState player)
                for (int col = 0; col < player.Dimensions.columns; ++col)
                {
                    (Card card, bool remove) = player.GameField[0, col];

                    for (int row = 1; remove && row < player.Dimensions.rows; ++row)
                        remove &= player.GameField[row, col].visible && card == player.GameField[row, col].card;

                    if (remove)
                    {
                        $"Removing identical stack {col} from {player}".Log(LogSource.Game);

                        player.RemoveColumn(col);

                        ++column_count;
                        --col;

                        OnGameStateChanged?.Invoke(this);
                    }
                }
        }

        public bool CurrentPlayer___FinishesFinalRound()
        {
            PlayerState next = Players[(CurrentPlayerIndex + 1 + Players.Count) % Players.Count];

            return CurrentGameState is GameState.FinalRound && next.Player.Equals(_final_round_initiator) || next.IsFull;
        }

        public bool CurrentPlayer___TryEnterFinalRound()
        {
            if (CurrentGameState is GameState.Running && (CurrentPlayer?.IsFull ?? false))
            {
                _final_round_initiator = CurrentPlayer.Player;
                CurrentGameState = GameState.FinalRound;

                // TODO : ???

                $"{_final_round_initiator} entered final round".Log(LogSource.Game);

                return true;
            }
            else
                return false;
        }

        public (Player Player, int Points)[] GetCurrentLeaderBoard() => (from p in Players
                                                                         let player = p.Player
                                                                         let points = p.VisiblePoints * (player.Equals(_final_round_initiator) ? 2 : 1)
                                                                         orderby points ascending
                                                                         select (player, points)).ToArray();

        public (Player Player, int Points)[] FinishGame()
        {
            CurrentGameState = GameState.Finished;

            return GetCurrentLeaderBoard();
        }
    }

    public enum GameState
    {
        Stopped = 0,
        Running = 1,
        FinalRound = 2,
        Finished = 3,
    }

    public enum GameWaitingFor
    {
        Draw = 0,
        Play = 1,
        Discard = 2,
        Uncover = 3,
        NextPlayer = 4,
    }
}
