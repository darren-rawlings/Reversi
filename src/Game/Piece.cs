using Godot;
using Renegade.Looks;
using Reversi.Core;
using static Reversi.Game.BoardLayout;
using Side = Reversi.Core.Side;

namespace Reversi.Game;

/// <summary>
/// A two-coloured disc: Dark's colour on one face, Light's on the other, like the real thing. It
/// shows its owner's colour on top; flipping turns it over in the air.
/// </summary>
public partial class Piece : Node3D
{
	private static readonly CylinderMesh HalfDisc = new()
	{
		TopRadius = DiscRadius,
		BottomRadius = DiscRadius * 0.96f,
		Height = DiscThickness / 2,
		RadialSegments = 40,
	};

	private readonly MeshInstance3D _dark = new() { Mesh = HalfDisc, Position = new Vector3(0, DiscThickness / 4, 0) };
	private readonly MeshInstance3D _light = new()
	{
		Mesh = HalfDisc,
		Position = new Vector3(0, -DiscThickness / 4, 0),
		RotationDegrees = new Vector3(180, 0, 0),
	};

	public Side Showing { get; private set; }

	public MeshInstance3D[] Faces => new[] { _dark, _light };

	public void Setup(Side owner, PieceSet set)
	{
		var body = new Node3D();
		AddChild(body);
		body.AddChild(_dark);
		body.AddChild(_light);
		ApplyStyle(set);
		Show(owner);
	}

	public void ApplyStyle(PieceSet set)
	{
		_dark.MaterialOverride = Materials.For(set.One, isDisc: true);
		_light.MaterialOverride = Materials.For(set.Two, isDisc: true);
	}

	/// <summary>Sets which face is up, without animation.</summary>
	public void Show(Side owner)
	{
		Showing = owner;
		Rotation = new Vector3(owner == Side.Dark ? 0 : Mathf.Pi, Rotation.Y, 0);
	}

	/// <summary>
	/// Turns the disc over in the air: up, a half turn, back down. Returns the tween so the caller
	/// can chain the landing sound.
	/// </summary>
	public Tween Flip(Side to, float delay)
	{
		Showing = to;
		float start = Rotation.X;
		var lift = CreateTween();
		lift.TweenInterval(delay);
		lift.TweenProperty(this, "position:y", DiscThickness / 2 + 0.55f, 0.14f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		lift.TweenProperty(this, "position:y", DiscThickness / 2, 0.16f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		var turn = CreateTween();
		turn.TweenInterval(delay);
		turn.TweenProperty(this, "rotation:x", start + Mathf.Pi, 0.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		return lift;
	}
}
