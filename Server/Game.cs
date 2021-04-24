using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unknown6656.Common;
using Unknown6656.Imaging;
using Unknown6656.Mathematics.Numerics;

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
            Card[] removed = Enumerable.Range(0, Dimensions.rows).ToArray(row => GameField[row, column_index]);

            for (int row = 0; row < Dimensions.rows; ++row)
                for (int col = Dimensions.columns - 2; col >= 0; --col)
                    @new[row, col] = GameField[row, col >= column_index ? col + 1 : col];

            --Dimensions.columns;

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
        public readonly Stack<Card> DrawPile { get; }
        public readonly Stack<Card> DiscardPile { get; }
        public PlayerState[] Players;
        public int CurrentPlayerIndex;

        public Card? DiscardedCard => DiscardPile.TryPeek(out Card card) ? card : null;

        public PlayerState CurrentPlayer => Players[CurrentPlayerIndex];


        public Game(IEnumerable<Player> players)
        {
            Players = players.ToArray(p => new PlayerState(this, p));
            CurrentPlayerIndex = 0;
            DrawPile = new();
            DiscardPile = new();
        }

        public bool RemovePlayer(Player player)
        {
            for (int i = 0; i < Players.Length; ++i)
                if (Players[i].Player.Equals(player))
                    return RemovePlayer(i);

            return false;
        }

        public bool RemovePlayer(int index)
        {
            if (index >= 0 && index < Players.Length)
            {
                if (CurrentPlayerIndex == index && CurrentPlayer.CurrentlyDrawnCard is Card card)
                    DiscardPile.Push(card);
                else if (CurrentPlayerIndex > index)
                    --CurrentPlayerIndex;

                Array.Resize(ref Players, Players.Length - 1);
                CurrentPlayerIndex = (CurrentPlayerIndex + Players.Length) % Players.Length;

                return true;
            }
            else
                return false;
        }

        public int NextPlayer() => NextPlayer(CurrentPlayerIndex + 1);

        public int NextPlayer(int player_index) => CurrentPlayerIndex = (player_index + Players.Length) % Players.Length;

        public void ResetAndDealCards(int total_cards, int first_player)
        {
            total_cards = Math.Min(total_cards, Players.Length * 12 + 30);

            Card[] cards = Enumerable.Range(0, total_cards).PartitionByArraySize(15).SelectMany(arr => arr.Select(i => new Card(i % 15 - 2))).ToArray();

            cards.Shuffle();

            int index = 0;

            for (int i = 0; i < Players.Length; i++)
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

            NextPlayer(first_player);
        }



        //private void CurrentPlayer___TurnOverCard(int row, int column) => CurrentPlayer.GameField[row, column].visible = true;

        public bool CurrentPlayer___SwapDrawnCardWithGridAndThenDiscard(int row, int column)
        {
            if (CurrentPlayer.CurrentlyDrawnCard is Card drawn)
            {
                DiscardPile.Push(CurrentPlayer.GameField[row, column].card);
                CurrentPlayer.GameField[row, column] = (drawn, true);

                return true;
            }
            else
                return false;
        }

        public bool CurrentPlayer___DrawCard(bool from_discard_pile)
        {
            if (CurrentPlayer.CurrentlyDrawnCard is { })
                return false;

            CurrentPlayer.CurrentlyDrawnCard = (from_discard_pile ? DiscardPile : DrawPile).Pop();

            return true;
        }

        public bool CurrentPlayer___RemoveFullColumns(out int column_count)
        {
            PlayerState player = CurrentPlayer;

            for (int col = 0; col < player.Dimensions.columns; ++col)
                if (Enumerable.Range(0, player.Dimensions.rows).All(row => player.GameField[row, col].visible))
                {
                    

                }



        }
    }
}
