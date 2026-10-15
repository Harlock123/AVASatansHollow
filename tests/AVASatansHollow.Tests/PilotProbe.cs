using AVASatansHollow.Engine;

namespace AVASatansHollow.Tests;

/// <summary>How the computer pilot gets on, level by level (set HOLLOW_PROBE to run it, for tuning; HOLLOW_PROBE=lazy for one that just sits and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("HOLLOW_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "hollow-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var marks = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int level = 1, built = 0, t = 0;
            bool devil = false;
            double start = 0;
            for (; t < 60 * 900 && g.Level <= 4; t++)
            {
                var c = mode == "lazy" ? new Controls(Fire: true) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                string where = g.Devil != null ? $"L{g.Level}devil" : $"L{g.Level}";
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"{where} {g.Cause}"] = deaths.GetValueOrDefault($"{where} {g.Cause}") + 1;
                if (g.Built != built) { built = g.Built; if (built > 0) marks.Add($"b{built}@{t / 60}s/w{g.Wave}"); }
                if ((g.Devil != null) != devil) { devil = g.Devil != null; if (devil) marks.Add($"devil@{t / 60}s"); }
                if (g.Level != level) { marks.Add($"L{level}:{(t - start) / 60:0}s"); level = g.Level; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: level {g.Level} after {t / 60:0}s score {g.Score} | {string.Join(" ", marks)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
