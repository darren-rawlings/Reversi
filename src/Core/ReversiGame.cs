using System;
using System.Collections.Generic;
using System.Numerics;

namespace Reversi.Core;

public enum Side
{
	/// <summary>Dark moves first; against the computer, that's you.</summary>
	Dark,
	Light,
}

/// <summary>A disc that turned over, and how far it is from the disc just placed (1 = adjacent).</summary>
public readonly record struct Flip(int Square, int Distance);

/// <summary>What a move did, so the game can animate it exactly.</summary>
public sealed record MoveResult(Side Mover, int Square, IReadOnlyList<Flip> Flips, bool OpponentPasses, bool GameOver);

/// <summary>
/// Reversi on bitboards: one 64-bit mask per side, bit = row * 8 + column, row 0 at the far edge.
/// Legal moves are found by sliding each side's discs in all eight directions at once, which is
/// what makes the computer's search fast enough for a phone.
/// </summary>
public sealed class ReversiGame
{
	public const int Size = 8;

	private const ulong NotA = 0xfefefefefefefefeUL; // excludes column 0
	private const ulong NotH = 0x7f7f7f7f7f7f7f7fUL; // excludes column 7

	private readonly Stack<(ulong Dark, ulong Light, Side Turn, bool Over)> _history = new();

	public ulong DarkDiscs { get; private set; }
	public ulong LightDiscs { get; private set; }
	public Side Turn { get; private set; }
	public bool IsOver { get; private set; }

	public ReversiGame() => Reset();

	private ReversiGame(ReversiGame other)
	{
		DarkDiscs = other.DarkDiscs;
		LightDiscs = other.LightDiscs;
		Turn = other.Turn;
		IsOver = other.IsOver;
	}

	public void Reset()
	{
		// The standard start: two of each in the centre, crossed.
		DarkDiscs = Bit(3, 4) | Bit(4, 3);
		LightDiscs = Bit(3, 3) | Bit(4, 4);
		Turn = Side.Dark;
		IsOver = false;
		_history.Clear();
	}

	public ReversiGame Clone() => new(this);

	/// <summary>Sets up a position (for tutorial clips and tests).</summary>
	public void Load(ulong dark, ulong light, Side turn)
	{
		DarkDiscs = dark;
		LightDiscs = light;
		Turn = turn;
		IsOver = false;
		_history.Clear();
	}

	public int MovesMade => _history.Count;

	public static ulong Bit(int row, int column) => 1UL << (row * Size + column);

	public static Side Opponent(Side side) => side == Side.Dark ? Side.Light : Side.Dark;

	public ulong DiscsOf(Side side) => side == Side.Dark ? DarkDiscs : LightDiscs;

	public int Count(Side side) => BitOperations.PopCount(DiscsOf(side));

	public Side? At(int square)
	{
		ulong bit = 1UL << square;
		if ((DarkDiscs & bit) != 0)
			return Side.Dark;
		if ((LightDiscs & bit) != 0)
			return Side.Light;
		return null;
	}

	public Side? Winner => !IsOver || Count(Side.Dark) == Count(Side.Light)
		? null
		: Count(Side.Dark) > Count(Side.Light) ? Side.Dark : Side.Light;

	public ulong LegalMoves() => IsOver ? 0 : Moves(DiscsOf(Turn), DiscsOf(Opponent(Turn)));

	public bool CanPlay(int square) => (LegalMoves() & (1UL << square)) != 0;

	public IEnumerable<int> LegalSquares()
	{
		ulong moves = LegalMoves();
		while (moves != 0)
		{
			int square = BitOperations.TrailingZeroCount(moves);
			moves &= moves - 1;
			yield return square;
		}
	}

	public MoveResult Play(int square)
	{
		if (!CanPlay(square))
			throw new InvalidOperationException($"Can't play {square} for {Turn}.");
		_history.Push((DarkDiscs, LightDiscs, Turn, IsOver));
		var mover = Turn;
		ulong own = DiscsOf(mover), other = DiscsOf(Opponent(mover));
		var flips = new List<Flip>();
		ulong flipped = 0;
		foreach (int shift in Directions)
		{
			ulong line = 0;
			ulong cursor = Step(1UL << square, shift);
			int distance = 1;
			var run = new List<Flip>();
			while (cursor != 0 && (cursor & other) != 0)
			{
				line |= cursor;
				run.Add(new Flip(BitOperations.TrailingZeroCount(cursor), distance++));
				cursor = Step(cursor, shift);
			}
			if (cursor != 0 && (cursor & own) != 0 && line != 0)
			{
				flipped |= line;
				flips.AddRange(run);
			}
		}
		own |= flipped | (1UL << square);
		other &= ~flipped;
		SetDiscs(mover, own, other);

		bool opponentPasses = false;
		var next = Opponent(mover);
		if (Moves(DiscsOf(next), DiscsOf(mover)) != 0)
		{
			Turn = next;
		}
		else if (Moves(DiscsOf(mover), DiscsOf(next)) != 0)
		{
			opponentPasses = true;
		}
		else
		{
			IsOver = true;
		}
		flips.Sort((a, b) => a.Distance.CompareTo(b.Distance));
		return new MoveResult(mover, square, flips, opponentPasses, IsOver);
	}

	public bool Undo()
	{
		if (_history.Count == 0)
			return false;
		var (dark, light, turn, over) = _history.Pop();
		DarkDiscs = dark;
		LightDiscs = light;
		Turn = turn;
		IsOver = over;
		return true;
	}

	/// <summary>Who played the most recent move, if anyone.</summary>
	public Side? LastMover => _history.Count == 0 ? null : _history.Peek().Turn;

	private void SetDiscs(Side mover, ulong own, ulong other)
	{
		if (mover == Side.Dark)
		{
			DarkDiscs = own;
			LightDiscs = other;
		}
		else
		{
			LightDiscs = own;
			DarkDiscs = other;
		}
	}

	// ------------------------------------------------------------ Bitboard helpers

	/// <summary>Shifts for the eight directions: E, W, S, N, SE, SW, NE, NW.</summary>
	internal static readonly int[] Directions = { 1, -1, 8, -8, 9, 7, -7, -9 };

	/// <summary>Moves every bit one square in a direction, dropping bits that would wrap round an edge.</summary>
	internal static ulong Step(ulong bits, int shift) => shift switch
	{
		1 => (bits << 1) & NotA,
		-1 => (bits >> 1) & NotH,
		8 => bits << 8,
		-8 => bits >> 8,
		9 => (bits << 9) & NotA,
		7 => (bits << 7) & NotH,
		-7 => (bits >> 7) & NotA,
		_ => (bits >> 9) & NotH,
	};

	/// <summary>Every empty square where <paramref name="own"/> could play, for all directions at once.</summary>
	internal static ulong Moves(ulong own, ulong other)
	{
		ulong empty = ~(own | other);
		ulong moves = 0;
		foreach (int shift in Directions)
		{
			ulong run = Step(own, shift) & other;
			for (int i = 0; i < 5; i++)
				run |= Step(run, shift) & other;
			moves |= Step(run, shift) & empty;
		}
		return moves;
	}

	/// <summary>The discs a move at <paramref name="move"/> (one bit) would turn over.</summary>
	internal static ulong FlipsFor(ulong move, ulong own, ulong other)
	{
		ulong flipped = 0;
		foreach (int shift in Directions)
		{
			ulong line = 0;
			ulong cursor = Step(move, shift);
			while ((cursor & other) != 0)
			{
				line |= cursor;
				cursor = Step(cursor, shift);
			}
			if ((cursor & own) != 0)
				flipped |= line;
		}
		return flipped;
	}
}
