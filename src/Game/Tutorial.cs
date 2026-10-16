using System.Collections.Generic;
using Renegade.Ui;

namespace Reversi.Game;

/// <summary>The guided tour. Clips are recorded from the game with --clip (see Main.PlayClip).</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("The goal",
			"Finish with more discs showing your colour than your opponent. Dark moves first, then you take turns.",
			"res://tutorial/place.ogv"),
		new TutorialPage("Trap to flip",
			"Place a disc so that a straight line of your opponent's discs is trapped between it and one of yours. Every trapped disc flips to your colour. Glowing dots show where you can play.",
			"res://tutorial/place.ogv"),
		new TutorialPage("Every direction counts",
			"One move can trap lines in several directions at once: across, up and down, and diagonally. They all flip, in a wave out from your disc.",
			"res://tutorial/wave.ogv"),
		new TutorialPage("Passing and the end",
			"If you can't trap anything, your turn passes. When neither side can move, the game ends: the most discs wins, and the winner's discs catch fire.",
			"res://tutorial/end.ogv"),
		new TutorialPage("Tips",
			"Corners can never be flipped back, so they're worth fighting for. Drag sideways to spin the board, or freeze it in Settings. Against the computer, Undo (ad) takes a move back.",
			"res://tutorial/wave.ogv"),
	};
}
