namespace AVASatansHollow.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). It looks a moment ahead for fireballs,
/// pitchforks and swooping demons and slips aside from them, raising the shield when there's nowhere to go.
/// When it's safe enough it runs the bridge's errands: fetching a waiting piece from the ledge, laying it,
/// and crossing a finished bridge. Otherwise it lines up under the best target (a swooper first; the Devil
/// once he's up) allowing for where it will be when the shot gets there, and holds the trigger down.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>How soon something will hit the cannon if it sits at <paramref name="x"/> (a large number for nothing soon).</summary>
    private static double Danger(GameEngine g, double x)
    {
        double soonest = 99;
        foreach (var b in g.Bullets)
        {
            if (b.Vy <= 0) continue;
            double t = (Field.ShipY - b.Y) / b.Vy;
            if (t < -0.06 || t > 1.3) continue;
            if (Math.Abs(b.X + b.Vx * Math.Max(0, t) - x) < (b.Pitchfork ? 30 : 23)) soonest = Math.Min(soonest, Math.Max(0, t));
        }
        foreach (var d in g.Demons)
        {
            if (d.Dead || d.Wait > 0 || d.State != DemonState.Swooping) continue;
            for (double t = 0; t <= 0.9; t += 0.05)
            {
                double dx = d.X + d.Vx * t, dy = d.Y + d.Vy * t;
                if (Math.Abs(dy - Field.ShipY) < 30 && Math.Abs(dx - x) < 38) { soonest = Math.Min(soonest, t); break; }
            }
        }
        return soonest;
    }

    public Controls Decide(GameEngine g)
    {
        var s = g.Ship;
        if (!s.Alive || g.Phase is not (Phase.Play or Phase.Start)) return default;
        bool shieldReady = s.ShieldTime <= 0 && s.ShieldCool <= 0;
        double lo = Field.Left + 20, hi = g.RightLimit;
        double now = Danger(g, s.X);
        if (now < 0.7)
        {
            double best = double.MaxValue, goal = s.X;
            for (double dx = -240; dx <= 240; dx += 10)
            {
                double x = s.X + dx;
                if (x < lo || x > hi) continue;
                double time = Math.Abs(dx) / GameEngine.ShipSpeed;
                if (time > now - 0.04 && dx != 0) continue;
                if (Danger(g, x) < 1.0) continue;
                bool clear = true;
                for (double k = 10; k < Math.Abs(dx) && clear; k += 10)
                    if (Danger(g, s.X + Math.Sign(dx) * k) < k / GameEngine.ShipSpeed + 0.05) clear = false;
                if (clear && Math.Abs(dx) < best) { best = Math.Abs(dx); goal = x; }
            }
            if (best < double.MaxValue && Math.Abs(goal - s.X) > 2)
            {
                Doing = "dodge";
                return new Controls(Math.Sign(goal - s.X), 0, Fire: true);
            }
            if (now < 0.3 && shieldReady)
            {
                Doing = "shield";
                return new Controls(Shield: true);
            }
            if (s.Shielded) { Doing = "shielded"; return default; }
        }

        // The bridge's errands, when nothing's swooping low.
        bool calm = !g.Demons.Any(d => !d.Dead && d.State == DemonState.Swooping && d.Y > 380);
        double? errand = null;
        if (s.Carrying) errand = g.BridgeEnd - 18;
        else if (g.PieceWaiting) errand = Field.LedgeX;
        else if (g.BridgeDone && g.Devil == null) errand = Field.Right - 20;

        double aim = s.X;
        bool lined = false;
        if (g.Devil is { Alive: true } v)
        {
            double tShot = (Field.ShipY - v.Y) / GameEngine.ShotSpeed;
            aim = Field.CentreX + Math.Sin((v.T + tShot) * 0.55 * g.Pace) * 210;
            if (v.Y < 190) aim = v.X;
            Doing = "devil";
        }
        else if (errand is { } e && (calm || g.Remaining == 0))
        {
            aim = e;
            Doing = s.Carrying ? "lay" : g.PieceWaiting ? "fetch" : "cross";
        }
        else
        {
            Demon? target = null;
            double cost = double.MaxValue;
            foreach (var d in g.Demons)
            {
                if (d.Dead || d.Wait > 0 || d.Y > Field.ShipY - 40 || d.Y < Field.Top) continue;
                double tShot = Math.Max(0, (Field.ShipY - 22 - d.Y) / GameEngine.ShotSpeed);
                double px = d.X + d.Vx * tShot;
                if (px < lo - 10 || px > hi + 10) continue;
                double c = Math.Abs(px - s.X) / GameEngine.ShipSpeed - (d.State == DemonState.Swooping ? 0.6 : 0) - d.Y / 1500;
                if (c < cost) { cost = c; target = d; aim = px; }
            }
            Doing = target == null ? "wait" : target.State == DemonState.Swooping ? "swooper" : "target";
            if (target == null && errand is { } e2) { aim = e2; Doing = "errand"; }
        }
        aim = Math.Clamp(aim, lo, hi);
        int dir = Math.Abs(aim - s.X) > 4 ? Math.Sign(aim - s.X) : 0;
        if (dir != 0 && Danger(g, s.X + dir * 20) < 0.6) dir = 0;
        // Something above worth a shot as it goes?
        foreach (var d in g.Demons)
            if (!d.Dead && d.Wait <= 0 && d.Y < Field.ShipY - 40 && Math.Abs(d.X + d.Vx * (Field.ShipY - d.Y) / GameEngine.ShotSpeed - s.X) < 14) lined = true;
        if (g.Devil is { } dv && Math.Abs(dv.X - s.X) < Devil.HalfWidth - 6) lined = true;
        return new Controls(dir, 0, lined || Math.Abs(aim - s.X) < 14 && Doing is "target" or "swooper" or "devil");
    }
}
