using System.Linq;
using System.Numerics;
using Renegade.Core;
using Reversi.Core;
using Xunit;

namespace Reversi.Tests;

public class ReversiTests
{
	private static int Sq(int row, int column) => row * ReversiGame.Size + column;

	[Fact]
	public void StartsWithTheCrossedCentreAndFourMovesForDark()
	{
		var game = new ReversiGame();
		Assert.Equal(2, game.Count(Side.Dark));
		Assert.Equal(2, game.Count(Side.Light));
		Assert.Equal(new[] { Sq(2, 3), Sq(3, 2), Sq(4, 5), Sq(5, 4) }, game.LegalSquares().OrderBy(s => s));
	}

	[Fact]
	public void PlayingFlipsTheOutflankedDisc()
	{
		var game = new ReversiGame();
		var result = game.Play(Sq(2, 3));
		Assert.Equal(new[] { new Flip(Sq(3, 3), 1) }, result.Flips);
		Assert.Equal(4, game.Count(Side.Dark));
		Assert.Equal(1, game.Count(Side.Light));
		Assert.Equal(Side.Light, game.Turn);
	}

	[Fact]
	public void IllegalSquaresAreRefused()
	{
		var game = new ReversiGame();
		Assert.False(game.CanPlay(Sq(0, 0)));
		Assert.Throws<System.InvalidOperationException>(() => game.Play(Sq(0, 0)));
	}

	[Fact]
	public void DiscsNeverWrapRoundTheEdges()
	{
		// A light disc at the right edge and a dark one at the next row's left edge must not
		// count as neighbours.
		ulong own = ReversiGame.Bit(3, 0);
		ulong other = ReversiGame.Bit(2, 7);
		Assert.Equal(0UL, ReversiGame.Moves(own, other) & ReversiGame.Bit(2, 6));
	}

	[Fact]
	public void UndoRestoresEverything()
	{
		var game = new ReversiGame();
		game.Play(Sq(2, 3));
		game.Play(game.LegalSquares().First());
		Assert.True(game.Undo());
		Assert.True(game.Undo());
		Assert.Equal(new ReversiGame().DarkDiscs, game.DarkDiscs);
		Assert.Equal(Side.Dark, game.Turn);
		Assert.False(game.Undo());
	}

	[Fact]
	public void AFullGameAlwaysEndsWithEverySquareAccountedFor()
	{
		var game = new ReversiGame();
		var computer = new ComputerPlayer(Difficulty.Medium, seed: 3);
		int moves = 0;
		while (!game.IsOver && moves++ < 100)
		{
			game.Play(computer.ChooseMove(game));
			Assert.Equal(0UL, game.DarkDiscs & game.LightDiscs);
		}
		Assert.True(game.IsOver);
	}

	[Fact]
	public void HardBeatsEasyMostOfTheTime()
	{
		int hardWins = 0;
		for (int i = 0; i < 6; i++)
		{
			var game = new ReversiGame();
			var hard = new ComputerPlayer(Difficulty.Hard, seed: i);
			var easy = new ComputerPlayer(Difficulty.Easy, seed: 50 + i);
			bool hardIsDark = i % 2 == 0;
			while (!game.IsOver)
				game.Play(((game.Turn == Side.Dark) == hardIsDark ? hard : easy).ChooseMove(game));
			if (game.Winner == (hardIsDark ? Side.Dark : Side.Light))
				hardWins++;
		}
		Assert.True(hardWins >= 5, $"Hard won only {hardWins} of 6.");
	}

	[Fact]
	public void HardThinksQuicklyEnoughForAPhone()
	{
		var game = new ReversiGame();
		var hard = new ComputerPlayer(Difficulty.Hard, seed: 1);
		long worst = 0;
		while (!game.IsOver)
		{
			var watch = System.Diagnostics.Stopwatch.StartNew();
			int move = hard.ChooseMove(game);
			worst = System.Math.Max(worst, watch.ElapsedMilliseconds);
			game.Play(move);
		}
		Assert.True(worst < 600, $"Slowest move took {worst} ms.");
	}
}
