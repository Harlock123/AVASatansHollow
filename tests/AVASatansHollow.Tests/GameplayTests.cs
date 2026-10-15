using AVASatansHollow.Engine;

namespace AVASatansHollow.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avasatanshollow-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A level in play with the flock in its places (or none, if a test wants its own), and nothing swooping, firing or coming on its own.</summary>
    private static GameEngine Game(int level = 1, int built = 0, bool keepFlock = false)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(level, built);
        if (!keepFlock) g.ClearDemons();
        return g;
    }

    /// <summary>A real game in play, with plenty of cannons.</summary>
    private static GameEngine Real(int level = 1, int seed = 7, int built = 0)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(level, built);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);
    private static readonly Controls ShieldButton = new(Shield: true);
    private static readonly Controls GoLeft = new(Dx: -1), GoRight = new(Dx: 1);

    /// <summary>Lines up under a demon in the flock, leading it as it sways, and fires.</summary>
    private static void ShootAt(GameEngine g, Demon d)
    {
        g.Tick(default);
        double t = (Field.ShipY - 22 - d.Y) / GameEngine.ShotSpeed;
        g.SetShipX(d.X + d.Vx * t);
        g.Tick(FireButton);
        for (int i = 0; i < 90 && g.Shots.Count > 0; i++) g.Tick(default);
    }

    // ───────────────────────────── the flock ─────────────────────────────

    [Fact]
    public void A_flock_flies_in_from_the_sides_to_its_places()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        Assert.InRange(g.Demons.Count, 8, GameEngine.Slots);
        Assert.All(g.Demons, d => Assert.True(d.X < Field.Left || d.X > Field.Right));
        for (int i = 0; i < 60 * 8 && g.Demons.Any(d => d.State != DemonState.Holding); i++) g.Tick(default);
        Assert.All(g.Demons, d => Assert.Equal(DemonState.Holding, d.State));
    }

    [Fact]
    public void Later_waves_and_levels_bring_more_and_more_imps()
    {
        int Imps(int level) => Enumerable.Range(1, 6).Sum(seed =>
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(level);
            return g.Demons.Count(d => d.Kind == DemonKind.Imp);
        });
        Assert.True(Imps(4) > Imps(1));
        Assert.True(Real(3).Pace > Real(1).Pace);
    }

    [Fact]
    public void Demons_are_worth_more_swooping()
    {
        Assert.Equal(50, GameEngine.Points(new Demon { Kind = DemonKind.Gargoyle, State = DemonState.Holding }));
        Assert.Equal(150, GameEngine.Points(new Demon { Kind = DemonKind.Gargoyle, State = DemonState.Swooping }));
        Assert.Equal(100, GameEngine.Points(new Demon { Kind = DemonKind.Imp, State = DemonState.Holding }));
        Assert.Equal(300, GameEngine.Points(new Demon { Kind = DemonKind.Imp, State = DemonState.Swooping }));
        var g = Game(keepFlock: true);
        var d = g.Demons.First(d => d.Kind == DemonKind.Gargoyle);
        g.Demons.RemoveAll(x => x != d);
        ShootAt(g, d);
        Assert.True(d.Dead);
        Assert.Equal(50, g.Score);
    }

    [Fact]
    public void A_swooper_loops_out_comes_down_and_round_from_the_top_to_its_place()
    {
        var g = Game(keepFlock: true);
        var d = g.Demons[0];
        g.SendSwoop(d);
        g.SetShipX(d.X < Field.CentreX ? Field.ChasmX - 30 : Field.Left + 30);
        bool low = false;
        for (int i = 0; i < 60 * 10 && !(low && d.State == DemonState.Holding); i++)
        {
            g.Tick(default);
            if (d.Y > Field.ShipY - 60) low = true;
        }
        Assert.True(low);
        Assert.Equal(DemonState.Holding, d.State);
    }

    [Fact]
    public void Swooping_gargoyles_drop_fire()
    {
        var g = Real();
        int most = 0;
        for (int i = 0; i < 60 * 12; i++)
        {
            g.Tick(new Controls(Shield: i % 400 == 0));
            most = Math.Max(most, g.Bullets.Count);
        }
        Assert.True(most > 0);
    }

    // ───────────────────────────── the cannon ─────────────────────────────

    [Fact]
    public void The_cannon_runs_from_the_ledge_to_the_end_of_the_bridge_and_no_further()
    {
        var g = Game();
        g.Run(200, GoLeft);
        Assert.Equal(Field.Left + 20, g.Ship.X, 1);
        g.Run(200, GoRight);
        Assert.Equal(Field.ChasmX - 18, g.Ship.X, 1);
        g.SetBuilt(3);
        g.Run(60, GoRight);
        Assert.Equal(Field.ChasmX + 3 * Field.PieceW - 18, g.Ship.X, 1);
    }

    [Fact]
    public void Two_shots_at_a_time_and_holding_the_button_keeps_firing()
    {
        var g = Game();
        g.Run(5, FireButton);
        Assert.Single(g.Shots);
        g.Run(20, FireButton);
        Assert.Equal(GameEngine.MaxShots, g.Shots.Count);
    }

    [Fact]
    public void The_shield_holds_the_cannon_still_stops_fire_and_must_recharge()
    {
        var g = Game();
        g.PutBullet(g.Ship.X, Field.ShipY - 60);
        g.Tick(ShieldButton);
        Assert.True(g.Ship.Shielded);
        double x = g.Ship.X;
        g.Run(30, new Controls(Dx: 1, Shield: true));
        Assert.Equal(x, g.Ship.X);
        Assert.True(g.Ship.Alive);
        Assert.Empty(g.Bullets);
        g.Idle(120);
        Assert.False(g.Ship.Shielded);
        g.Tick(ShieldButton);
        Assert.False(g.Ship.Shielded);
    }

    [Fact]
    public void The_shield_destroys_a_demon_that_flies_into_it()
    {
        var g = Game();
        g.Tick(ShieldButton);
        var d = g.PutDemon(DemonKind.Imp, g.Ship.X, Field.ShipY - 10, DemonState.Swooping);
        g.Tick(default);
        Assert.True(d.Dead);
        Assert.True(g.Ship.Alive);
    }

    // ───────────────────────────── the bridge ─────────────────────────────

    [Fact]
    public void A_cleared_flock_leaves_a_piece_on_the_ledge_and_the_next_flock_comes()
    {
        var g = Game(keepFlock: true);
        g.Demons.RemoveRange(1, g.Demons.Count - 1);
        ShootAt(g, g.Demons[0]);
        g.Calm = false;
        g.Tick(default);
        Assert.True(g.PieceWaiting);
        g.Idle((int)(GameEngine.BetweenWaves * 60) + 2);
        Assert.Equal(2, g.Wave);
        Assert.NotEmpty(g.Demons);
    }

    [Fact]
    public void A_piece_is_picked_up_at_the_ledge_carried_to_the_chasm_and_laid_for_500()
    {
        var g = Game();
        g.GivePiece();
        g.Run(200, GoLeft);
        Assert.True(g.Ship.Carrying);
        Assert.False(g.PieceWaiting);
        g.Run(200, GoRight);
        Assert.False(g.Ship.Carrying);
        Assert.Equal(1, g.Built);
        Assert.Equal(GameEngine.PiecePoints, g.Score);
    }

    [Fact]
    public void Losing_the_cannon_drops_the_piece_back_on_the_ledge()
    {
        var g = Game();
        g.GivePiece();
        g.Run(200, GoLeft);
        Assert.True(g.Ship.Carrying);
        g.PutBullet(g.Ship.X, Field.ShipY - 20);
        g.Idle(5);
        Assert.False(g.Ship.Alive);
        Assert.True(g.PieceWaiting);
    }

    [Fact]
    public void No_more_pieces_than_the_bridge_needs()
    {
        var g = Game(built: Field.BridgePieces - 1);
        g.GivePiece();
        g.Calm = false;
        g.Idle(2);
        g.Idle((int)(GameEngine.BetweenWaves * 60) + 2);
        // The piece waiting is the last; clearing the next flock doesn't bring another.
        g.Calm = true;
        g.Run(200, GoLeft);
        g.Run(300, GoRight);
        Assert.True(g.BridgeDone);
        Assert.False(g.PieceWaiting);
    }

    // ───────────────────────────── the Devil ─────────────────────────────

    [Fact]
    public void Crossing_the_finished_bridge_scatters_the_flock_and_raises_the_Devil()
    {
        var g = Game(built: Field.BridgePieces, keepFlock: true);
        Assert.NotEmpty(g.Demons);
        g.Run(300, GoRight);
        Assert.NotNull(g.Devil);
        Assert.Empty(g.Demons);
        Assert.Equal(22, g.Devil!.Hp);
    }

    [Fact]
    public void The_Devil_spits_fire_and_throws_his_pitchfork()
    {
        var g = Game(built: Field.BridgePieces);
        g.SetLives(99);
        g.Run(400, GoRight);
        Assert.NotNull(g.Devil);
        g.Calm = false;
        bool fan = false, fork = false;
        for (int i = 0; i < 60 * 8; i++)
        {
            g.Tick(new Controls(Dx: i / 60 % 2 == 0 ? -1 : 1, Shield: i % 300 == 0));
            if (g.Bullets.Count(b => !b.Pitchfork) >= 3) fan = true;
            if (g.Bullets.Any(b => b.Pitchfork)) fork = true;
        }
        Assert.True(fan);
        Assert.True(fork);
    }

    [Fact]
    public void Bringing_the_Devil_down_wins_the_level_and_the_bridge_falls()
    {
        var g = Game(built: Field.BridgePieces);
        g.Run(300, GoRight);
        var v = g.Devil!;
        g.Idle(120);
        int hits = 0;
        for (int i = 0; i < 60 * 60 && v.Alive; i++)
        {
            g.SetShipX(Math.Clamp(v.X, Field.Left + 20, Field.Right - 20));
            int before = v.Hp;
            g.Tick(new Controls(Fire: i % 2 == 0));
            if (v.Hp < before) hits++;
        }
        Assert.False(v.Alive);
        Assert.Equal(22, hits);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(2, g.Level);
        Assert.Equal(0, g.Built);
        Assert.Null(g.Devil);
    }

    // ───────────────────────────── losing cannons ─────────────────────────────

    [Fact]
    public void Fire_burns_the_cannon_and_the_next_one_comes_after_a_moment()
    {
        var g = Game(keepFlock: true);
        int lives = g.Lives;
        g.PutBullet(g.Ship.X, Field.ShipY - 40);
        g.Idle(20);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("BURNT", g.Cause);
        Assert.Equal(lives - 1, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal("READY", g.Message);
        Assert.True(g.Ship.Alive);
    }

    [Fact]
    public void A_swooper_that_hits_the_cannon_goes_down_with_it()
    {
        var g = Game();
        var d = g.PutDemon(DemonKind.Imp, g.Ship.X, Field.ShipY - 10, DemonState.Swooping);
        g.Tick(default);
        Assert.True(d.Dead);
        Assert.Equal("CLAWED", g.Cause);
    }

    [Fact]
    public void A_pitchfork_can_only_be_dodged_or_shielded()
    {
        var g = Game();
        g.PutBullet(g.Ship.X, 300, pitchfork: true);
        g.Run(120, FireButton);
        Assert.Equal("FORKED", g.Cause);
    }

    [Fact]
    public void The_game_ends_with_the_last_cannon_and_a_good_score_gets_initials()
    {
        var g = Game(keepFlock: true);
        g.SetLives(1);
        g.SetScore(500000);
        g.PutBullet(g.Ship.X, Field.ShipY - 20);
        g.Idle(5);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(4 * 60);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void An_extra_cannon_at_20000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(19600);
        g.GivePiece();
        g.Run(200, GoLeft);
        g.Run(200, GoRight);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_pilot_builds_the_bridge_and_brings_the_Devil_down()
    {
        var g = Real();
        for (int i = 0; i < 60 * 240 && g.Level == 1; i++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(2, g.Level);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Built} {g.Lives} {g.Ship.X:0.00} {g.Remaining}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real();
        g.Idle(120);
        var before = string.Join(",", g.Demons.Select(d => $"{d.X:0.0}"));
        g.Pause();
        g.Idle(120);
        Assert.Equal(before, string.Join(",", g.Demons.Select(d => $"{d.X:0.0}")));
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
