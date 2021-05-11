using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System;

using Unknown6656.Common;

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

        // TODO : +/-/++/-- etc.?
    }

    public sealed class PlayerState
    {
        public Game Game { get; }
        public Player Player { get; }
        public (int rows, int columns) Dimensions;
        public (Card card, bool visible)[,] GameField;
        public Card? CurrentlyDrawnCard = null;

        public int VisiblePoints => GameField.Cast<(Card c, bool v)>().Aggregate(0, (s, t) => s + (t.v ? 0 :t.c.Value));

        public int VisibleCount => GameField.Cast<(Card c, bool v)>().Count(t => t.v);

        public bool IsFull => VisibleCount >= Dimensions.rows * Dimensions.columns;


        internal PlayerState(Game game, Player player)
        {
            Game = game;
            Player = player;
            Dimensions = (rows: 3, columns: 4);
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
                    _state = value;

                    OnGameStateChanged?.Invoke(this);
                }
            }
        }

        public Card? DiscardedCard => DiscardPile.TryPeek(out Card card) ? card : null;

        public PlayerState CurrentPlayer => Players[CurrentPlayerIndex];


        public event Action<Game, Player>? OnPlayerAdded;
        public event Action<Game, Player>? OnPlayerRemoved;
        public event Action<Game>? OnGameStateChanged;


        public Game(IEnumerable<Player>? players = null)
        {
            Players = (players ?? Array.Empty<Player>()).ToList(p => new PlayerState(this, p));
            CurrentGameState = GameState.Stopped;
            CurrentPlayerIndex = 0;
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

                if (CurrentPlayerIndex == index && CurrentPlayer.CurrentlyDrawnCard is Card card)
                    DiscardPile.Push(card);
                else if (CurrentPlayerIndex > index)
                    --CurrentPlayerIndex;

                for (int i = Players.Count - 2; i >= index; --i)
                    Players[i] = Players[i + 1];

                Players.RemoveAt(Players.Count - 1);
                NextPlayer(CurrentPlayerIndex);

                OnPlayerRemoved?.Invoke(this, player);
                OnGameStateChanged?.Invoke(this);

                return true;
            }
            else
                return false;
        }

        public int NextPlayer() => NextPlayer(CurrentPlayerIndex + 1);

        public int NextPlayer(int player_index)
        {
            CurrentPlayerIndex = Players.Count == 0 ? 0 : (player_index + Players.Count) % Players.Count;

            OnGameStateChanged?.Invoke(this);

            return CurrentPlayerIndex;
        }

        public void DealCardsAndRestart(int total_cards = 150, int first_player = 0)
        {
            total_cards = Math.Max(total_cards, Players.Count * 15 + 30);

            Card[] cards = Enumerable.Range(0, total_cards).PartitionByArraySize(15).SelectMany(arr => arr.Select(i => new Card(i % 15 - 2))).ToArray();

            cards.Shuffle();

            int index = 0;

            for (int i = 0; i < Players.Count; i++)
            {
                PlayerState player = Players[i];

                player.CurrentlyDrawnCard = null;
                player.Dimensions = (rows: 3, columns: 4);
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


        public bool CurrentPlayer___SwapDrawnCardWithGridAndThenDiscard(int row, int column)
        {
            if (CurrentPlayer.CurrentlyDrawnCard is Card drawn)
            {
                DiscardPile.Push(CurrentPlayer.GameField[row, column].card);
                CurrentPlayer.GameField[row, column] = (drawn, true);
                OnGameStateChanged?.Invoke(this);

                return true;
            }
            else
                return false;
        }

        public bool CurrentPlayer___DiscardDrawnCardThenAndTurnOver(int row, int column)
        {
            if (!CurrentPlayer.GameField[row, column].visible)
                if (CurrentPlayer.CurrentlyDrawnCard is Card drawn)
                {
                    DiscardPile.Push(drawn);
                    CurrentPlayer.GameField[row, column] = (drawn, true);
                    OnGameStateChanged?.Invoke(this);

                    return true;
                }

            return false;
        }

        public bool CurrentPlayer___DrawCard(bool from_discard_pile)
        {
            if (CurrentPlayer.CurrentlyDrawnCard is { })
                return false;

            CurrentPlayer.CurrentlyDrawnCard = (from_discard_pile ? DiscardPile : DrawPile).Pop();
            OnGameStateChanged?.Invoke(this);

            return true;
        }

        public void CurrentPlayer___RemoveFullColumnsOfIdenticalCards(out int column_count)
        {
            PlayerState player = CurrentPlayer;

            column_count = 0;

            for (int col = 0; col < player.Dimensions.columns; ++col)
            {
                Card[] cards = (from row in Enumerable.Range(0, player.Dimensions.rows)
                                let item = player.GameField[row, col]
                                where item.visible
                                select item.card).Distinct().ToArray();

                if (Enumerable.Range(0, player.Dimensions.rows).All(row => player.GameField[row, col].visible))
                {
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
            if (CurrentGameState is GameState.Running && CurrentPlayer.IsFull)
            {
                _final_round_initiator = CurrentPlayer.Player;
                CurrentGameState = GameState.FinalRound;

                // TODO : ???

                return true;
            }
            else
                return false;
        }

        public IReadOnlyDictionary<Player, int> FinishGame()
        {
            CurrentGameState = GameState.Finished;

            return (from p in Players
                    let player = p.Player
                    let points = p.VisiblePoints * (player.Equals(_final_round_initiator) ? 2 : 1)
                    orderby points ascending
                    select (player, points)).ToDictionary();
        }

    }

    public enum GameState
    {
        Stopped,
        Running,
        FinalRound,
        Finished,
    }
}
