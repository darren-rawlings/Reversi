using Godot;
using Reversi.Core;

namespace Reversi.Game;

/// <summary>Where everything is, in board space: y up, the playing surface at y = 0, row 0 at the far edge.</summary>
public static class BoardLayout
{
	public const float Cell = 1.0f;
	public const float Border = 0.4f;
	public const float Half = ReversiGame.Size * Cell / 2f + Border;
	public const float Thickness = 0.45f;

	public const float DiscRadius = 0.42f;
	public const float DiscThickness = 0.14f;

	/// <summary>Centre of a square on the board's surface.</summary>
	public static Vector3 SquareCenter(int square) =>
		new((square % ReversiGame.Size - 3.5f) * Cell, 0, (square / ReversiGame.Size - 3.5f) * Cell);

	/// <summary>The square under a board-space point, or -1 off the grid.</summary>
	public static int SquareAt(Vector3 local)
	{
		int column = Mathf.FloorToInt(local.X / Cell + 4f);
		int row = Mathf.FloorToInt(local.Z / Cell + 4f);
		if (column < 0 || column >= ReversiGame.Size || row < 0 || row >= ReversiGame.Size)
			return -1;
		return row * ReversiGame.Size + column;
	}
}
