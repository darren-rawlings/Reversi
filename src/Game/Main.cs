using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Looks;
using Renegade.Play;
using Renegade.Ui;
using Reversi.Core;
using static Reversi.Game.BoardLayout;
using Side = Reversi.Core.Side;

namespace Reversi.Game;

/// <summary>
/// Reversi. Tap a glowing square to drop a disc; every disc it outflanks turns over in a wave that
/// ripples out from it, one after another, each with a click (the signature "flip wave"). Drag
/// sideways to spin the board. The rules decide first (<see cref="ReversiGame.Play"/> lists every
/// flip and how far it is from the new disc), then the discs act it out.
/// </summary>
public partial class Main : Node3D
{
	public const string Title = "Reversi";

	/// <summary>Google's test ad units until the real ones exist in AdMob (see the release playbook).</summary>
	private static readonly AdMobConfig AdUnits = new(
		InterstitialUnitId: "ca-app-pub-3310172999423635/8969720496",
		RewardedUnitId: "ca-app-pub-3310172999423635/1384453529");

	private const Side Human = Side.Dark;

	/// <summary>Seconds between one ring of the flip wave and the next.</summary>
	private const float WaveStep = 0.09f;

	private readonly ReversiGame _game = new();
	private readonly Scoreboard _wins = new();
	private readonly Piece?[] _pieces = new Piece?[64];
	private readonly MeshInstance3D[] _hints = new MeshInstance3D[64];
	private readonly ImpactBank<Style> _impacts = new(Styles.Impact);
	private readonly ComputerTurn _computer = new();

	private GameSettings _settings = null!;
	private Node3D _rig = null!;
	private Camera3D _camera = null!;
	private SoundPool _sound = null!;
	private GameHud _hud = null!;
	private IAdService _ads = null!;
	private AdSchedule? _adSchedule;
	private SpinControl _spin = null!;
	private MeshInstance3D[] _boardMeshes = System.Array.Empty<MeshInstance3D>();
	private bool _busy;
	private bool _roundCounted;
	private string? _note;

	private bool _demo;
	private bool _quitAfterDemo;
	private string? _clip;
	private float _initialSpin;

	private bool VsComputer => _settings.Mode == GameMode.PlayerVsComputer || _demo;

	private bool IsComputerTurn => !_game.IsOver && (_demo || (VsComputer && _game.Turn != Human));

	private PieceSet Discs => Styles.Pieces(_settings.Pieces);

	public override void _Ready()
	{
		DevLog.Tag = "rev";
		// Flight recorder: checks every sound reaches the output and logs pauses, focus and ads to user://blackbox.log.
		AudioWatch.Attach(this, Title);
		_settings = GameSettings.Load(board: Style.Wood, pieces: Style.Marble);
		ReadDeveloperArgs();
		AudioServer.SetBusMute(0, _settings.Muted);
		BuildWorld();

		_sound = new SoundPool();
		AddChild(_sound);
		_adSchedule ??= new AdSchedule(_settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		_ads = AdServices.Create(this, AdUnits, _demo, new PlaceholderAds
		{
			TextColor = theme.Text,
			RewardTitle = "Watch to undo your move",
			ClaimText = "Claim undo",
			SkipText = "Skip (no undo)",
		});

		_hud = new GameHud(theme);
		AddChild(_hud);
		_hud.Build(new HudOptions(Title, "Tap a glowing square  ·  Drag sideways to spin", PiecesLabel: "Discs",
			PieceName: s => $"{Styles.Pieces(s).NameOne} & {Styles.Pieces(s).NameTwo}"), _settings);
		_hud.NewGamePressed += OnNewGamePressed;
		_hud.UndoPressed += OnUndoPressed;
		_hud.RestartPressed += OnRestartPressed;
		_hud.SetTutorial(Tutorial.Pages);
		_hud.ModeChosen += (mode, difficulty) =>
		{
			_settings.Mode = mode;
			_settings.Difficulty = difficulty;
			_settings.Save();
			_wins.Reset();
			_ = StartGame();
		};
		_hud.BoardChosen += style =>
		{
			_settings.Board = style;
			_settings.Save();
			ApplyBoardStyle();
		};
		_hud.PiecesChosen += style =>
		{
			_settings.Pieces = style;
			_settings.Save();
			foreach (var piece in _pieces)
				piece?.ApplyStyle(Discs);
		};

		_spin = new SpinControl(_rig, () => !_busy && !_settings.FreezeBoard);
		// Freeze the board also turns it back to where it started.
		_spin.Frozen = () => _settings.FreezeBoard && !_busy;
		_spin.Tapped += OnTapped;
		if (_clip != null)
		{
			_hud.Visible = false;
			_ = PlayClip(_clip);
			return;
		}
		_ = StartGame();
		if (!_demo)
			_hud.OfferTutorialOnce(_settings);
		_hud.ApplyDevFlags();
	}

	// ---------------------------------------------------------------- World

	private void BuildWorld()
	{
		// Fixed width, so the whole board fits any phone; looking down steeply from your side.
		_camera = new Camera3D { Position = new Vector3(0, 10.6f, 6.6f), Fov = 52, KeepAspect = Camera3D.KeepAspectEnum.Width };
		AddChild(_camera);
		_camera.LookAt(new Vector3(0, -0.4f, 0.3f));
		Stage.Build(this, -Thickness, new Color("1a0f09"));

		_rig = new Node3D();
		AddChild(_rig);
		_rig.RotateY(_initialSpin);

		var slab = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(Half * 2, Thickness, Half * 2) },
			Position = new Vector3(0, -Thickness / 2, 0),
		};
		_rig.AddChild(slab);

		// Grooves between the squares: thin dark strips, a hair above the surface.
		var groove = new StandardMaterial3D { AlbedoColor = new Color(0, 0, 0, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		float span = ReversiGame.Size * Cell;
		for (int i = 0; i <= ReversiGame.Size; i++)
		{
			float at = (i - 4) * Cell;
			_rig.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.004f, span) }, MaterialOverride = groove, Position = new Vector3(at, 0.002f, 0) });
			_rig.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(span, 0.004f, 0.04f) }, MaterialOverride = groove, Position = new Vector3(0, 0.002f, at) });
		}
		_boardMeshes = new[] { slab };
		ApplyBoardStyle();

		// Legal-move markers: soft glowing dots that breathe.
		var hintMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(1f, 0.78f, 0.35f, 0.55f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		var dot = new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 0.01f, RadialSegments = 24 };
		for (int square = 0; square < 64; square++)
		{
			_hints[square] = new MeshInstance3D { Mesh = dot, MaterialOverride = hintMaterial, Position = SquareCenter(square) + new Vector3(0, 0.006f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_rig.AddChild(_hints[square]);
		}
		Stage.WarmUp(this, -Thickness, Sparkle.Halo);
	}

	private void ApplyBoardStyle()
	{
		var material = Materials.For(Styles.BoardLook(_settings.Board));
		foreach (var mesh in _boardMeshes)
			mesh.MaterialOverride = material;
	}

	private Piece AddPiece(int square, Side owner)
	{
		var piece = new Piece();
		piece.Setup(owner, Discs);
		piece.Position = SquareCenter(square) + new Vector3(0, DiscThickness / 2, 0);
		piece.RotateY((float)GD.RandRange(0, Mathf.Tau));
		_rig.AddChild(piece);
		_pieces[square] = piece;
		return piece;
	}

	// ---------------------------------------------------------------- Input

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_hud.MenuOpen)
			_spin.HandleInput(@event);
	}

	private void OnTapped(Vector2 screen)
	{
		if (_busy || IsComputerTurn || _game.IsOver)
			return;
		var from = _camera.ProjectRayOrigin(screen);
		var direction = _camera.ProjectRayNormal(screen);
		if (direction.Y > -0.01f)
			return;
		var world = from + direction * (-from.Y / direction.Y);
		int square = SquareAt(_rig.ToLocal(world));
		if (square >= 0 && _game.CanPlay(square))
			_ = PlayMove(square);
	}

	// ---------------------------------------------------------------- Turns

	private async Task PlayMove(int square)
	{
		_busy = true;
		_spin.Stop();
		_hud.HideHint();
		_note = null;
		var result = _game.Play(square);
		DevLog.Write($"{result.Mover} plays {square}: {result.Flips.Count} flips, pass={result.OpponentPasses}, over={result.GameOver}");
		ShowHints(null);
		RefreshUi(mover: result.Mover);

		// The new disc drops onto its square.
		var piece = AddPiece(square, result.Mover);
		var rest = piece.Position;
		piece.Position = rest + new Vector3(0, 1.4f, 0);
		var drop = CreateTween();
		drop.TweenProperty(piece, "position", rest, 0.22f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await ToSignal(drop, Tween.SignalName.Finished);
		PlayClack(piece.GlobalPosition, 0.9f, onBoard: true);

		// The flip wave: each ring of outflanked discs turns over a beat after the one before.
		float last = 0f;
		foreach (var flip in result.Flips)
		{
			var flipped = _pieces[flip.Square]!;
			float delay = 0.05f + (flip.Distance - 1) * WaveStep;
			last = Mathf.Max(last, delay);
			var tween = flipped.Flip(result.Mover, delay);
			tween.TweenCallback(Callable.From(() =>
			{
				PlayClack(flipped.GlobalPosition, 0.6f, onBoard: false);
				Glint(flipped);
			}));
		}
		if (result.Flips.Count > 0)
			await Wait(last + 0.45f);

		if (result.OpponentPasses)
			_note = $"{NameOf(ReversiGame.Opponent(result.Mover))} can't move: {NameOf(result.Mover)} again";
		if (_game.IsOver && !_roundCounted)
		{
			_roundCounted = true;
			if (_game.Winner is { } winner)
			{
				_wins.Win(first: winner == Side.Dark);
				await SetWinnerAlight(winner, square);
			}
		}
		_busy = false;
		RefreshUi();
	}

	/// <summary>A flash of warm light across a disc as it lands face up: the wave is easy to follow.</summary>
	private void Glint(Piece piece)
	{
		foreach (var face in piece.Faces)
			face.MaterialOverlay = Sparkle.Halo;
		GetTree().CreateTimer(0.18).Timeout += () =>
		{
			foreach (var face in piece.Faces)
				face.MaterialOverlay = null;
		};
	}

	/// <summary>A disc landing: on the board it's heard as both materials; a flip is disc on disc.</summary>
	private void PlayClack(Vector3 at, float strength, bool onBoard)
	{
		var pieces = _settings.Pieces;
		_sound.PlayImpact(_impacts.Random(pieces), at, strength, Styles.GainDb(pieces) + 2f);
		if (onBoard)
			_sound.PlayImpact(_impacts.Random(_settings.Board), at, strength * 0.6f, Styles.GainDb(_settings.Board));
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_spin.Process(dt);
		if (_busy || !IsComputerTurn)
			return;
		if (!_computer.Thinking)
		{
			var player = new ComputerPlayer(_demo ? Difficulty.Medium : _settings.Difficulty);
			var snapshot = _game.Clone();
			_computer.MinThinkTime = _demo ? 0.2f : 0.7f;
			_computer.Start(() => player.ChooseMove(snapshot), $"computer ({player.Difficulty})");
			RefreshUi();
			return;
		}
		if (_computer.Poll(dt) is { } move && _game.CanPlay(move))
			_ = PlayMove(move);
	}

	// ---------------------------------------------------------------- Games, undo, ads

	private void OnNewGamePressed()
	{
		if (_busy)
			return;
		bool adDue = _adSchedule!.GameFinished();
		_settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		_settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			_ads.ShowInterstitial(() => _ = StartGame());
		}
		else
		{
			_ = StartGame();
		}
	}

	private async Task StartGame()
	{
		_computer.Cancel();
		_busy = true;
		_roundCounted = false;
		_note = null;
		PutOutFire();
		_game.Reset();
		await SyncPieces();
		_busy = false;
		RefreshUi();
	}

	/// <summary>
	/// Start over: free once the game is over or before anyone has moved; otherwise against the
	/// computer it's a resignation (the computer gets the win) unless the player watches an ad.
	/// </summary>
	private void OnRestartPressed()
	{
		if (_busy)
			return;
		if (_game.IsOver || _game.MovesMade == 0)
		{
			_ = StartGame();
			return;
		}
		_hud.AskRestart(VsComputer && !_demo, countLoss =>
		{
			if (countLoss)
				_wins.Win(first: Human != Side.Dark);
			_ = StartGame();
		}, then => _ads.ShowRewarded(rewarded: then, closed: () => { }));
	}

	// ---------------------------------------------------------------- The winner's fire

	private readonly List<Flames> _fires = new();

	/// <summary>
	/// Every one of the winner's discs catches fire, in a wave spreading from the last disc played;
	/// the nearest dozen get real flames (more would be too much for a phone), the rest glow.
	/// </summary>
	private async Task SetWinnerAlight(Side winner, int from)
	{
		var origin = SquareCenter(from);
		var discs = Enumerable.Range(0, 64).Where(s => _game.At(s) == winner && _pieces[s] != null)
			.OrderBy(s => (SquareCenter(s) - origin).Length()).ToList();
		for (int i = 0; i < discs.Count; i++)
		{
			var piece = _pieces[discs[i]]!;
			_fires.Add(Flames.SetAlight(_rig, piece.Position + new Vector3(0, 0.05f, 0), piece.Faces, radius: 0.28f, size: 0.8f, withLight: i == 0, particles: i < 12));
			if (i < 12)
				await Wait(0.06f);
		}
		_hud.SetStatus(NameOf(winner) == "You" ? "You win!" : $"{NameOf(winner)} wins!", ColorOf(winner));
		await Wait(0.4f);
	}

	private void PutOutFire()
	{
		foreach (var fire in _fires)
			fire.Extinguish();
		_fires.Clear();
		foreach (var piece in _pieces)
		{
			if (piece == null)
				continue;
			foreach (var face in piece.Faces)
				face.MaterialOverlay = null;
		}
	}

	// ---------------------------------------------------------------- Tutorial clips

	/// <summary>
	/// Plays one scripted scene for a tutorial video, with the HUD hidden. Recorded with
	/// `--write-movie tutorial/name.ogv --resolution 600x600 -- --clip=name`.
	/// </summary>
	private async Task PlayClip(string clip)
	{
		_settings.Mode = GameMode.PlayerVsPlayer;
		_settings.Muted = false;
		_game.Reset();
		switch (clip)
		{
			case "place":
				await SyncPieces();
				ShowHints(_game.LegalSquares());
				await Wait(1.4f);
				await PlayMove(19);
				ShowHints(_game.LegalSquares());
				await Wait(1.0f);
				await PlayMove(_game.LegalSquares().First());
				ShowHints(_game.LegalSquares());
				await Wait(1.0f);
				await PlayMove(_game.LegalSquares().Last());
				await Wait(1.2f);
				break;
			case "wave":
			{
				ulong dark = ReversiGame.Bit(2, 5) | ReversiGame.Bit(7, 4) | ReversiGame.Bit(3, 3) | ReversiGame.Bit(4, 5);
				ulong light = ReversiGame.Bit(6, 1) | ReversiGame.Bit(5, 2) | ReversiGame.Bit(4, 3) | ReversiGame.Bit(3, 4)
					| ReversiGame.Bit(7, 1) | ReversiGame.Bit(7, 2) | ReversiGame.Bit(7, 3) | ReversiGame.Bit(4, 4);
				_game.Load(dark, light, Side.Dark);
				await SyncPieces();
				_hints[56].Visible = true;
				await Wait(1.4f);
				await PlayMove(56);
				await Wait(1.8f);
				break;
			}
			case "end":
			{
				// A real game played most of the way through, then finished by the computer.
				var computer = new ComputerPlayer(Difficulty.Medium, seed: 4);
				while (64 - System.Numerics.BitOperations.PopCount(_game.DarkDiscs | _game.LightDiscs) > 6 && !_game.IsOver)
					_game.Play(computer.ChooseMove(_game));
				var dark = _game.DarkDiscs;
				var light = _game.LightDiscs;
				var turn = _game.Turn;
				_game.Load(dark, light, turn);
				await SyncPieces();
				await Wait(0.6f);
				while (!_game.IsOver)
				{
					await PlayMove(computer.ChooseMove(_game));
					await Wait(0.3f);
				}
				await Wait(2.5f);
				break;
			}
		}
		GetTree().Quit();
	}

	/// <summary>
	/// Makes the discs on the board match the rules: removes the ones that shouldn't be there (they
	/// shrink away), adds missing ones (they pop in), and turns any showing the wrong face.
	/// </summary>
	private async Task SyncPieces()
	{
		bool changed = false;
		for (int square = 0; square < 64; square++)
		{
			var owner = _game.At(square);
			var piece = _pieces[square];
			if (owner == null && piece != null)
			{
				_pieces[square] = null;
				var shrink = CreateTween();
				shrink.TweenProperty(piece, "scale", Vector3.One * 0.01f, 0.2f);
				shrink.TweenCallback(Callable.From(piece.QueueFree));
				changed = true;
			}
			else if (owner is { } side && piece == null)
			{
				var added = AddPiece(square, side);
				added.Scale = Vector3.One * 0.01f;
				CreateTween().TweenProperty(added, "scale", Vector3.One, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
				changed = true;
			}
			else if (owner is { } shown && piece != null && piece.Showing != shown)
			{
				piece.Flip(shown, 0f);
				changed = true;
			}
		}
		if (changed)
			await Wait(0.35f);
	}

	/// <summary>Undo is offered against the computer on your turn (or after the game ends), once you've moved.</summary>
	private bool CanUndo => VsComputer && !_demo && !_busy && _game.MovesMade > 0 && (_game.IsOver || _game.Turn == Human);

	private void OnUndoPressed()
	{
		if (!CanUndo)
			return;
		DevLog.Write("ad: rewarded (undo)");
		_hud.SetUndoVisible(false);
		_ads.ShowRewarded(rewarded: () => _ = UndoLastTurn(), closed: () => RefreshUi());
	}

	/// <summary>Takes back the computer's replies and then your last move.</summary>
	private async Task UndoLastTurn()
	{
		PutOutFire();
		_computer.Cancel();
		if (_game.IsOver && _roundCounted && _game.Winner is { } winner)
			_wins.Unwin(first: winner == Side.Dark);
		_roundCounted = false;
		while (_game.LastMover == ReversiGame.Opponent(Human))
			_game.Undo();
		if (_game.LastMover == Human)
			_game.Undo();
		_note = null;
		_busy = true;
		RefreshUi();
		await SyncPieces();
		_busy = false;
		RefreshUi();
	}

	// ---------------------------------------------------------------- UI

	private string NameOf(Side side) => VsComputer && !_demo
		? (side == Human ? "You" : "Computer")
		: Discs.NameOf(side == Side.Dark);

	private Color ColorOf(Side side) => Discs.UiColorOf(side == Side.Dark);

	private void ShowHints(IEnumerable<int>? squares)
	{
		foreach (var hint in _hints)
			hint.Visible = false;
		if (squares == null)
			return;
		foreach (int square in squares)
			_hints[square].Visible = true;
	}

	private void RefreshUi(Side? mover = null)
	{
		_hud.SetScore($"{NameOf(Side.Dark)}  {_game.Count(Side.Dark)}   —   {_game.Count(Side.Light)}  {NameOf(Side.Light)}");
		_hud.SetNewGameVisible(_game.IsOver && !_busy);
		_hud.SetUndoVisible(CanUndo);
		_hud.SetRestartVisible(!_game.IsOver);

		if (_game.IsOver)
		{
			if (_busy)
				return;
			ShowHints(null);
			var winner = _game.Winner;
			string wins = $"(wins {_wins.First}–{_wins.Second})";
			string text = winner switch
			{
				null => $"A draw, 32 each! {wins}",
				{ } w when NameOf(w) == "You" => $"You win! {wins}",
				{ } w => $"{NameOf(w)} wins {wins}",
			};
			_hud.SetStatus(text, winner is { } side ? ColorOf(side) : Colors.White);
			if (_demo && _quitAfterDemo)
				GetTree().CreateTimer(1.5).Timeout += () => GetTree().Quit();
			return;
		}

		var turn = mover ?? _game.Turn;
		string status = _note ?? (mover == null && IsComputerTurn && !_demo ? "Computer is thinking…" : NameOf(turn) == "You" ? "Your turn" : $"{NameOf(turn)}'s turn");
		_hud.SetStatus(status, ColorOf(turn));
		ShowHints(_busy || IsComputerTurn ? null : _game.LegalSquares());
	}

	private SignalAwaiter Wait(float seconds) => ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	// ---------------------------------------------------------------- Developer flags

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo` (the
	/// computer plays both sides), `--quit-after-demo`, `--games-since-ad=4`, `--board=Wood`,
	/// `--pieces=Marble`, `--spin=30` (degrees). Style flags aren't saved.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					_demo = true;
					_settings.Muted = false;
					break;
				case "--quit-after-demo":
					_quitAfterDemo = true;
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--board" when System.Enum.TryParse(value, out Style board):
					_settings.Board = board;
					break;
				case "--pieces" when System.Enum.TryParse(value, out Style pieces):
					_settings.Pieces = pieces;
					break;
				case "--spin":
					_initialSpin = Mathf.DegToRad(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
					break;
				case "--clip":
					_clip = value;
					break;
			}
		}
	}
}
