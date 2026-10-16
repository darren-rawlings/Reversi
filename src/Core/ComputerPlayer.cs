using System;
using System.Numerics;
using Renegade.Core;

namespace Reversi.Core;

/// <summary>
/// The computer opponent: negamax with alpha-beta on bitboards. The evaluation values corners
/// (they can never be flipped), punishes the squares next to an empty corner (they give it away),
/// and counts mobility; near the end it simply counts discs. Easy sometimes plays at random.
/// </summary>
public sealed class ComputerPlayer
{
	private const int Win = 100_000;

	/// <summary>Classic square weights: corners great, the X and C squares beside them poor.</summary>
	private static readonly int[] Weights =
	{
		100, -20, 10,  5,  5, 10, -20, 100,
		-20, -50, -2, -2, -2, -2, -50, -20,
		 10,  -2,  1,  1,  1,  1,  -2,  10,
		  5,  -2,  1,  0,  0,  1,  -2,   5,
		  5,  -2,  1,  0,  0,  1,  -2,   5,
		 10,  -2,  1,  1,  1,  1,  -2,  10,
		-20, -50, -2, -2, -2, -2, -50, -20,
		100, -20, 10,  5,  5, 10, -20, 100,
	};

	private readonly Random _random;

	public Difficulty Difficulty { get; }

	public ComputerPlayer(Difficulty difficulty, int? seed = null)
	{
		Difficulty = difficulty;
		_random = seed is { } s ? new Random(s) : new Random();
	}

	private int Depth => Difficulty switch
	{
		Difficulty.Easy => 1,
		Difficulty.Medium => 3,
		_ => 6,
	};

	public int ChooseMove(ReversiGame game)
	{
		ulong own = game.DiscsOf(game.Turn), other = game.DiscsOf(ReversiGame.Opponent(game.Turn));
		ulong moves = ReversiGame.Moves(own, other);
		int count = BitOperations.PopCount(moves);
		if (count == 0)
			throw new InvalidOperationException("No legal moves.");
		if (Difficulty == Difficulty.Easy && _random.NextDouble() < 0.35)
			return Nth(moves, _random.Next(count));

		// Search the whole endgame exactly once few squares are left.
		int empty = 64 - BitOperations.PopCount(own | other);
		int depth = empty <= (Difficulty == Difficulty.Hard ? 10 : 0) ? empty : Depth;

		int best = int.MinValue;
		var bestMoves = new System.Collections.Generic.List<int>();
		ulong remaining = moves;
		while (remaining != 0)
		{
			int square = BitOperations.TrailingZeroCount(remaining);
			remaining &= remaining - 1;
			ulong bit = 1UL << square;
			ulong flips = ReversiGame.FlipsFor(bit, own, other);
			int score = -Search(other & ~flips, own | flips | bit, depth - 1, -Win * 2, Win * 2, false);
			if (score > best)
			{
				best = score;
				bestMoves.Clear();
			}
			if (score == best)
				bestMoves.Add(square);
		}
		return bestMoves[_random.Next(bestMoves.Count)];
	}

	/// <summary>Negamax: the score of the position for the side to move (own), with alpha-beta pruning.</summary>
	private static int Search(ulong own, ulong other, int depth, int alpha, int beta, bool passed)
	{
		ulong moves = ReversiGame.Moves(own, other);
		if (moves == 0)
		{
			if (passed || ReversiGame.Moves(other, own) == 0)
			{
				int margin = BitOperations.PopCount(own) - BitOperations.PopCount(other);
				return Math.Sign(margin) * Win + margin;
			}
			return -Search(other, own, depth, -beta, -alpha, true);
		}
		if (depth <= 0)
			return Evaluate(own, other, moves);

		int value = -Win * 2;
		// Corners first: they're usually best, so alpha-beta prunes more.
		foreach (ulong set in new[] { moves & 0x8100000000000081UL, moves & ~0x8100000000000081UL })
		{
			ulong remaining = set;
			while (remaining != 0)
			{
				ulong bit = remaining & (ulong)-(long)remaining;
				remaining &= remaining - 1;
				ulong flips = ReversiGame.FlipsFor(bit, own, other);
				int score = -Search(other & ~flips, own | flips | bit, depth - 1, -beta, -alpha, false);
				value = Math.Max(value, score);
				alpha = Math.Max(alpha, value);
				if (alpha >= beta)
					return value;
			}
		}
		return value;
	}

	private static int Evaluate(ulong own, ulong other, ulong ownMoves)
	{
		int position = 0;
		for (int square = 0; square < 64; square++)
		{
			ulong bit = 1UL << square;
			if ((own & bit) != 0)
				position += Weights[square];
			else if ((other & bit) != 0)
				position -= Weights[square];
		}
		int mobility = BitOperations.PopCount(ownMoves) - BitOperations.PopCount(ReversiGame.Moves(other, own));
		return position + mobility * 8;
	}

	private static int Nth(ulong bits, int n)
	{
		for (int i = 0; i < n; i++)
			bits &= bits - 1;
		return BitOperations.TrailingZeroCount(bits);
	}
}
