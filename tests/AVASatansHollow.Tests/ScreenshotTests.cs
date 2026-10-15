using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASatansHollow.Engine;
using AVASatansHollow.Views;

namespace AVASatansHollow.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set HOLLOW_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avasatanshollow-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("HOLLOW_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASatansHollow-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 64250, 3);
        g.HighScores.Insert("AVA", 41800, 2);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_flock()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.SetLives(4);
        g.Idle(70);
        c.Advance(0.5);
        Shot(w, c, "flying-in");
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 5 && g.Ship.Alive && g.Shots.Count > 0 && g.Demons.Count(d => d.State == DemonState.Swooping && d.Y > 300) >= 2 && g.Bullets.Count >= 2));
        Shot(w, c, "swooping");
    }

    [AvaloniaFact]
    public void Building_the_bridge()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Built >= 2 && g.Ship.Carrying && g.Ship.X > Field.LedgeX + 160, 200));
        Shot(w, c, "carrying");
    }

    [AvaloniaFact]
    public void The_Devil()
    {
        var (w, c, g) = Open(4);
        g.Go(1, Field.BridgePieces);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Devil is { Y: >= 180 } v && v.Hp < v.MaxHp - 3 && v.Mouth > 0.2 && g.Bullets.Any(b => b.Pitchfork) && g.Ship.Alive && g.Shots.Count > 0, 120));
        Shot(w, c, "devil");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 0.7, 120));
        Shot(w, c, "devil-down");
    }

    [AvaloniaFact]
    public void The_shield_and_a_cannon_lost()
    {
        var (w, c, g) = Open(5);
        g.Go(2);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 4 && g.Ship.Alive && g.Bullets.Any(b => b.Y > 560 && Math.Abs(b.X - g.Ship.X) < 60)));
        g.Run(1, new AVASatansHollow.Engine.Controls(Shield: true));
        for (int t = 0; t < 20; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Shot(w, c, "shield");
        Assert.True(PlayUntil(g, c, g => g.Ship.Alive && !g.Ship.Shielded && g.Phase == Phase.Play));
        g.PutBullet(g.Ship.X, Field.ShipY - 20);
        g.Idle(30);
        c.Advance(0.4);
        Shot(w, c, "lost");
    }
}
