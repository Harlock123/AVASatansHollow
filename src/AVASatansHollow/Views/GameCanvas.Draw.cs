using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASatansHollow.Engine;
using Effect = AVASatansHollow.Engine.Effect;

namespace AVASatansHollow.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASatansHollow/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private const uint Lava = 0xFFFF6A1A, Ember = 0xFFFFB04A, Rock = 0xFF3A2228, Slab = 0xFF8A7A6A;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        if (InGame)
        {
            using (dc.PushClip(new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top)))
            {
                DrawFloor(dc);
                if (g.Devil is { } v) DrawDevil(dc, new Point(v.X, v.Y), 1, v.Mouth > 0 ? 1 : 0.15, v.Hurt > 0);
                foreach (var d in g.Demons.Where(d => !d.Dead && d.Wait <= 0)) DrawDemon(dc, d);
                foreach (var s in g.Shots)
                {
                    dc.DrawEllipse(Brush(Alpha(Ember, 0.35)), null, new Point(s.X, s.Y + 6), 5, 11);
                    dc.FillRectangle(Brush(0xFFFFF4C0), new Rect(s.X - 1.5, s.Y, 3, 14));
                }
                foreach (var b in g.Bullets)
                {
                    if (b.Pitchfork) DrawPitchfork(dc, new Point(b.X, b.Y), Math.Atan2(b.Vx, b.Vy));
                    else
                    {
                        double f = 0.5 + 0.5 * Math.Sin(_clock * 30 + b.X);
                        dc.DrawEllipse(Brush(Alpha(Lava, 0.4)), null, new Point(b.X, b.Y - 4), 6, 10);
                        Poly(dc, 0xFFFF9A2A, null, 0, new Point(b.X - 5, b.Y), new Point(b.X, b.Y - 12 - 4 * f), new Point(b.X + 5, b.Y), new Point(b.X, b.Y + 5));
                        dc.DrawEllipse(Brush(0xFFFFF0A0), null, new Point(b.X, b.Y), 2.5, 2.5);
                    }
                }
                if (g.Ship.Alive) DrawCannon(dc, new Point(g.Ship.X, Field.ShipY), g.Ship.ShieldTime, g.Ship.Carrying, 1);
            }
            DrawPanels(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 18), 26, Yellow, center: true);
    }

    /// <summary>The Hollow: a cavern glowing red from below, embers drifting up; the panels are the rock walls.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xFF160808), new Rect(0, 0, Field.Width, Field.Height));
        bool inGame = InGame;
        if (inGame)
        {
            var col = new Rect(Field.Left, Field.Top, Field.Right - Field.Left, Field.Bottom - Field.Top);
            dc.FillRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromUInt32(0xFF050208), 0), new GradientStop(Color.FromUInt32(0xFF1A0608), 0.6), new GradientStop(Color.FromUInt32(0xFF4A120A), 1) },
            }, col);
            // Rough rock along the column's edges.
            for (int i = 0; i < 40; i++)
            {
                double y = Field.Top + i * 18;
                double wl = 6 + Hash(i, 1, 3) % 12, wr = 6 + Hash(i, 2, 3) % 12;
                dc.FillRectangle(Brush(Rock), new Rect(Field.Left, y, wl, 18));
                dc.FillRectangle(Brush(Rock), new Rect(Field.Right - wr, y, wr, 18));
            }
        }
        double left = inGame ? Field.Left : 0, width = inGame ? Field.Right - Field.Left : Field.Width;
        for (int k = 0; k < 70; k++)
        {
            double speed = 20 + Hash(k, 2, 6) % 50;
            double x = left + Hash(k, 1, 6) % (uint)width + Math.Sin(_clock * 1.3 + k) * 6;
            double y = Field.Height - (Hash(k, 3, 6) % 768 + _clock * speed) % 768;
            if (inGame && y < Field.Top) continue;
            double a = Math.Min(1, (y - Field.Top) / 500);
            dc.FillRectangle(Brush(Alpha(k % 3 == 0 ? 0xFFFFE070 : Ember, 0.25 + 0.6 * a)), new Rect(x, y, 2, 2));
        }
    }

    /// <summary>The floor of the Hollow, the ledge with any waiting piece, the lava chasm and the bridge across it.</summary>
    private void DrawFloor(DrawingContext dc)
    {
        var g = Engine;
        double gy = Field.Ground;
        // The rock floor.
        dc.FillRectangle(Brush(0xFF4A2A22), new Rect(Field.Left, gy, Field.ChasmX - Field.Left, Field.Bottom - gy));
        for (int i = 0; i < 24; i++)
        {
            double x = Field.Left + Hash(i, 4, 1) % (uint)(Field.ChasmX - Field.Left - 10), y = gy + 6 + Hash(i, 5, 1) % 30;
            dc.FillRectangle(Brush(0xFF2A1612), new Rect(x, y, 6 + Hash(i, 6, 1) % 10, 3));
        }
        dc.FillRectangle(Brush(0xFF7A4A3A), new Rect(Field.Left, gy, Field.ChasmX - Field.Left, 3));
        // The far bank.
        dc.FillRectangle(Brush(0xFF4A2A22), new Rect(Field.Right - 16, gy, 16, Field.Bottom - gy));
        // The lava, churning, its glow rising.
        double cw = Field.Right - 16 - Field.ChasmX;
        dc.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromUInt32(0x00FF6A1A), 0), new GradientStop(Color.FromUInt32(0x60FF6A1A), 1) },
        }, new Rect(Field.ChasmX, gy - 90, cw, 90));
        dc.FillRectangle(Brush(0xFFB0300A), new Rect(Field.ChasmX, gy + 14, cw, Field.Bottom - gy));
        for (int i = 0; i < 14; i++)
        {
            double x = Field.ChasmX + (Hash(i, 7, 2) % (uint)cw + _clock * (10 + i % 4 * 6)) % cw;
            double y = gy + 18 + Math.Sin(_clock * 3 + i) * 4;
            dc.DrawEllipse(Brush(Alpha(i % 2 == 0 ? Lava : 0xFFFFC040, 0.8)), null, new Point(x, y), 8 + i % 3 * 3, 3);
        }
        // The bridge, slab by slab.
        for (int i = 0; i < g.Built; i++) DrawSlab(dc, new Point(Field.ChasmX + (i + 0.5) * Field.PieceW, gy + 6));
        // A piece waiting on the ledge.
        if (g.PieceWaiting)
        {
            double glow = 0.5 + 0.5 * Math.Sin(_clock * 6);
            dc.DrawEllipse(Brush(Alpha(Yellow, 0.25 * glow + 0.1)), null, new Point(Field.LedgeX, gy - 6), 30, 14);
            DrawSlab(dc, new Point(Field.LedgeX, gy - 6));
        }
    }

    private static void DrawSlab(DrawingContext dc, Point at)
    {
        var r = new Rect(at.X - Field.PieceW / 2 + 1, at.Y - 6, Field.PieceW - 2, 12);
        dc.FillRectangle(Brush(Slab), r, 2);
        dc.DrawRectangle(null, Pen(0xFF4A3A2A, 1.2), r, 2);
        dc.DrawLine(Pen(0xFFB0A090, 1), new Point(r.X + 4, r.Y + 3), new Point(r.Right - 6, r.Y + 3));
    }

    // ───────────────────────────── the cannon ─────────────────────────────

    /// <summary>The cannon: a squat steel turret on treads with a glowing barrel; a slab on its back when it's carrying one; under its shield, a golden dome.</summary>
    private void DrawCannon(DrawingContext dc, Point at, double shield, bool carrying, double scale)
    {
        double k = scale;
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        // Treads.
        dc.FillRectangle(Brush(0xFF2A2A34), new Rect(at.X - 22 * k, at.Y + 12 * k, 44 * k, 10 * k), (float)(4 * k));
        for (int i = 0; i < 5; i++) dc.DrawEllipse(Brush(0xFF6A6A7A), null, P(-16 + i * 8, 17), 2.6 * k, 2.6 * k);
        // Body and turret.
        Poly(dc, 0xFF7A6AA0, 0xFF2A2240, 1.2, P(-20, 12), P(-14, -2), P(14, -2), P(20, 12));
        dc.DrawEllipse(Brush(0xFF9A8AC8), Pen(0xFF2A2240, 1), P(0, -2), 11 * k, 8 * k);
        dc.FillRectangle(Brush(0xFFC0B8E0), new Rect(at.X - 3 * k, at.Y - 22 * k, 6 * k, 18 * k));
        dc.DrawEllipse(Brush(Alpha(Ember, 0.6 + 0.3 * Math.Sin(_clock * 12))), null, P(0, -22), 3.5 * k, 3.5 * k);
        if (carrying)
        {
            dc.FillRectangle(Brush(Slab), new Rect(at.X - Field.PieceW / 2 * k, at.Y - 12 * k, 14 * k, 9 * k), 2);
            dc.FillRectangle(Brush(Slab), new Rect(at.X + (Field.PieceW / 2 - 14) * k, at.Y - 12 * k, 14 * k, 9 * k), 2);
            dc.FillRectangle(Brush(Slab), new Rect(at.X - Field.PieceW / 2 * k, at.Y - 7 * k, Field.PieceW * k, 6 * k), 2);
        }
        if (shield > 0)
        {
            double a = Math.Min(1, shield / 0.3), pulse = 0.5 + 0.5 * Math.Sin(_clock * 30);
            dc.DrawEllipse(Brush(Alpha(Yellow, 0.16 * a)), Pen(Alpha(0xFFFFF0A0, (0.6 + 0.4 * pulse) * a), 3), P(0, 2), 34 * k, 30 * k);
        }
    }

    // ───────────────────────────── the demons ─────────────────────────────

    private void DrawDemon(DrawingContext dc, Demon d)
    {
        using (dc.PushTransform(Matrix.CreateRotation(-d.Angle) * Matrix.CreateTranslation(d.X, d.Y)))
            DrawDemonShape(dc, d.Kind, _clock * 7 + d.Phase, 1);
    }

    /// <summary>A gargoyle (green, bat-winged, horned) or an imp (small, red, with a barbed tail), about (0, 0), facing down.</summary>
    private static void DrawDemonShape(DrawingContext dc, DemonKind kind, double clock, double k)
    {
        Point P(double x, double y) => new(x * k, y * k);
        double flap = Math.Sin(clock);
        if (kind == DemonKind.Gargoyle)
        {
            double lift = 7 * flap;
            Poly(dc, 0xFF2A6A4A, 0xFF0A2A1A, 1, P(-4, -4), P(-20, -10 - lift), P(-17, -2 - lift / 2), P(-22, 4 - lift / 3), P(-12, 2), P(-5, 6));
            Poly(dc, 0xFF2A6A4A, 0xFF0A2A1A, 1, P(4, -4), P(20, -10 - lift), P(17, -2 - lift / 2), P(22, 4 - lift / 3), P(12, 2), P(5, 6));
            dc.DrawEllipse(Brush(0xFF6AD08A), Pen(0xFF1A4A2A, 1), P(0, 0), 7 * k, 10 * k);
            Poly(dc, 0xFFE0D0A0, null, 0, P(-5, -7), P(-9, -15), P(-3, -9));
            Poly(dc, 0xFFE0D0A0, null, 0, P(5, -7), P(9, -15), P(3, -9));
            dc.DrawEllipse(Brush(0xFFFFE040), null, P(-2.6, 3), 1.8 * k, 1.8 * k);
            dc.DrawEllipse(Brush(0xFFFFE040), null, P(2.6, 3), 1.8 * k, 1.8 * k);
        }
        else
        {
            double lift = 5 * flap;
            Poly(dc, 0xFF8A1A1A, null, 0, P(-3, -2), P(-12, -8 - lift), P(-11, 1 - lift / 2), P(-4, 3));
            Poly(dc, 0xFF8A1A1A, null, 0, P(3, -2), P(12, -8 - lift), P(11, 1 - lift / 2), P(4, 3));
            // The barbed tail, behind.
            dc.DrawLine(Pen(0xFFC02A2A, 1.6 * k), P(0, -6), P(3 * Math.Sin(clock * 0.7), -15));
            Poly(dc, 0xFFC02A2A, null, 0, P(3 * Math.Sin(clock * 0.7) - 3, -14), P(3 * Math.Sin(clock * 0.7), -19), P(3 * Math.Sin(clock * 0.7) + 3, -14));
            dc.DrawEllipse(Brush(0xFFFF4A2A), Pen(0xFF6A0A0A, 1), P(0, 1), 6 * k, 8 * k);
            Poly(dc, 0xFFFFD0A0, null, 0, P(-4, 4), P(-7, 11), P(-2, 6));
            Poly(dc, 0xFFFFD0A0, null, 0, P(4, 4), P(7, 11), P(2, 6));
            dc.DrawEllipse(Brush(0xFFFFFF80), null, P(-2.2, 3), 1.5 * k, 1.5 * k);
            dc.DrawEllipse(Brush(0xFFFFFF80), null, P(2.2, 3), 1.5 * k, 1.5 * k);
        }
    }

    // ───────────────────────────── the Devil ─────────────────────────────

    /// <summary>The Devil's head: red, horned, with burning eyes, a pointed beard and a mouth that gapes as he spits fire.</summary>
    private void DrawDevil(DrawingContext dc, Point at, double k, double mouth, bool hurt)
    {
        Point P(double x, double y) => new(at.X + x * k, at.Y + y * k);
        uint skin = hurt ? 0xFFFFE0E0 : 0xFFC0201A, dark = hurt ? 0xFFFFB0B0 : 0xFF6A0A0A;
        // The glow about him.
        dc.DrawEllipse(Brush(Alpha(Lava, 0.18 + 0.08 * Math.Sin(_clock * 3))), null, P(0, 0), 80 * k, 74 * k);
        // Horns.
        Poly(dc, 0xFFE8D8B0, 0xFF6A5A3A, 1.2, P(-30, -30), P(-58, -70), P(-44, -66), P(-20, -40));
        Poly(dc, 0xFFE8D8B0, 0xFF6A5A3A, 1.2, P(30, -30), P(58, -70), P(44, -66), P(20, -40));
        // Ears.
        Poly(dc, skin, dark, 1.2, P(-40, -10), P(-58, -24), P(-44, 6));
        Poly(dc, skin, dark, 1.2, P(40, -10), P(58, -24), P(44, 6));
        // The face, narrowing to a pointed beard.
        Poly(dc, skin, dark, 2, P(-40, -38), P(40, -38), P(44, 0), P(30, 30), P(10, 44), P(0, 66), P(-10, 44), P(-30, 30), P(-44, 0));
        Poly(dc, 0xFF2A0A0A, null, 0, P(-12, 40), P(12, 40), P(0, 66));
        // Brows and burning eyes.
        Poly(dc, dark, null, 0, P(-34, -20), P(-6, -10), P(-8, -5), P(-34, -14));
        Poly(dc, dark, null, 0, P(34, -20), P(6, -10), P(8, -5), P(34, -14));
        double glow = 0.7 + 0.3 * Math.Sin(_clock * 8);
        Poly(dc, Alpha(0xFFFFE040, glow), null, 0, P(-28, -8), P(-10, -4), P(-14, 2), P(-26, 0));
        Poly(dc, Alpha(0xFFFFE040, glow), null, 0, P(28, -8), P(10, -4), P(14, 2), P(26, 0));
        // The mouth: a grin, gaping when he spits.
        double open = 4 + 14 * mouth;
        Poly(dc, 0xFF1A0004, dark, 1.5, P(-22, 16), P(22, 16), P(14, 16 + open), P(-14, 16 + open));
        for (int i = -2; i <= 2; i++) Poly(dc, 0xFFF0F0E0, null, 0, P(i * 7 - 2.5, 16), P(i * 7 + 2.5, 16), P(i * 7, 21));
        if (mouth > 0.5) dc.DrawEllipse(Brush(Alpha(Lava, 0.8)), null, P(0, 18 + open / 2), 8 * k, open / 3 * k);
    }

    private static void DrawPitchfork(DrawingContext dc, Point at, double angle)
    {
        using (dc.PushTransform(Matrix.CreateRotation(-angle) * Matrix.CreateTranslation(at.X, at.Y)))
        {
            dc.DrawLine(Pen(0xFF8A5A3A, 3), new Point(0, -34), new Point(0, 0));
            dc.DrawLine(Pen(0xFFD0D0E0, 2.5), new Point(-9, 0), new Point(9, 0));
            foreach (double x in new[] { -9.0, 0, 9 })
            {
                dc.DrawLine(Pen(0xFFD0D0E0, 2.5), new Point(x, 0), new Point(x, 12));
                Poly(dc, 0xFFF0F0FF, null, 0, new Point(x - 2.5, 11), new Point(x + 2.5, 11), new Point(x, 17));
            }
        }
    }

    // ───────────────────────────── effects ─────────────────────────────

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)e.X, 1) % 10 * 0.06, d = r * (0.6 + Hash(k, 2, 3) % 10 * 0.06);
                    dc.FillRectangle(Brush(Alpha(k % 2 == 0 ? 0xFFFFF0C0 : e.Colour, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2, c.Y + Math.Sin(a) * d - 2, 4, 4));
                }
                break;
            }
            case EffectKind.Blast or EffectKind.Lava:
            {
                double r = (16 + 70 * Math.Sqrt(t)) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, (1 - t) * 0.8)), null, c, r * 0.6, r * 0.6);
                dc.DrawEllipse(null, Pen(Alpha(Fire, 1 - t), 6), c, r, r * 0.8);
                for (int k = 0; k < 18; k++)
                {
                    double a = k * Math.PI / 9 + Hash(k, (int)e.X, 5) % 10 * 0.05, d = r * (0.7 + Hash(k, 3, 5) % 10 * 0.05);
                    uint col = k % 3 == 0 ? 0xFFFFFFFF : k % 3 == 1 ? Fire : Yellow;
                    dc.FillRectangle(Brush(Alpha(col, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 3, c.Y + Math.Sin(a) * d * 0.8 - 3, 6, 6));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 40, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, panels and screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
            Text(dc, g.Devil != null ? $"LEVEL {g.Level} - THE DEVIL" : $"LEVEL {g.Level}  WAVE {g.Wave}", new Point(Field.Width / 2, 36), 36, White, center: true);
    }

    /// <summary>The spare cannons and the shield on the left; the bridge and what's what on the right.</summary>
    private void DrawPanels(DrawingContext dc)
    {
        var g = Engine;
        double lx = Field.Left / 2, rx = Field.Right + (Field.Width - Field.Right) / 2;
        Text(dc, "CANNONS", new Point(lx, 120), 30, Dim, center: true);
        int spare = Math.Max(0, g.Lives - (g.Ship.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 8); i++)
            DrawCannon(dc, new Point(lx - 48 + i % 3 * 48, 166 + i / 3 * 48), 0, false, 0.75);

        Text(dc, "SHIELD", new Point(lx, 330), 30, Dim, center: true);
        var s = g.Ship;
        double charge = s.Shielded ? s.ShieldTime / GameEngine.ShieldSeconds : 1 - s.ShieldCool / GameEngine.ShieldRecharge;
        bool ready = !s.Shielded && s.ShieldCool <= 0;
        var bar = new Rect(lx - 70, 356, 140, 18);
        dc.FillRectangle(Brush(0xFF2A1010), bar);
        dc.FillRectangle(Brush(s.Shielded ? Yellow : ready ? 0xFF4AE06A : 0xFFA06A3A), new Rect(bar.X, bar.Y, bar.Width * Math.Clamp(charge, 0, 1), bar.Height));
        dc.DrawRectangle(null, Pen(Dim, 1.5), bar);
        Text(dc, s.Shielded ? "UP" : ready ? "READY" : "CHARGING", new Point(lx, 396), 26, ready ? 0xFF4AE06A : s.Shielded ? Yellow : Dim, center: true);

        Text(dc, "THE BRIDGE", new Point(rx, 120), 30, Dim, center: true);
        for (int i = 0; i < Field.BridgePieces; i++)
        {
            var r = new Rect(rx - 78 + i * 26, 150, 24, 14);
            if (i < g.Built) dc.FillRectangle(Brush(Slab), r, 2);
            else dc.DrawRectangle(null, Pen(0xFF6A4A3A, 1.2), r, 2);
        }
        string what = g.Devil != null ? "BRING HIM DOWN" : g.BridgeDone ? "CROSS IT!" : g.Ship.Carrying ? "LAY THE PIECE" : g.PieceWaiting ? "FETCH THE PIECE" : "CLEAR THE FLOCK";
        bool urgent = g.Devil == null && (g.BridgeDone || g.Ship.Carrying || g.PieceWaiting);
        if (!urgent || (int)(_clock * 3) % 2 == 0) Text(dc, what, new Point(rx, 190), 24, urgent ? Yellow : White, center: true);

        if (g.Devil is { } v)
        {
            Text(dc, "THE DEVIL", new Point(rx, 260), 30, Red, center: true);
            var hp = new Rect(rx - 70, 284, 140, 16);
            dc.FillRectangle(Brush(0xFF2A1010), hp);
            dc.FillRectangle(Brush(Red), new Rect(hp.X, hp.Y, hp.Width * v.Hp / Math.Max(1, v.MaxHp), hp.Height));
            dc.DrawRectangle(null, Pen(Dim, 1.5), hp);
            Text(dc, $"{v.Hp} HITS LEFT", new Point(rx, 320), 24, White, center: true);
        }
        else
        {
            using (dc.PushTransform(Matrix.CreateTranslation(rx - 52, 270))) DrawDemonShape(dc, DemonKind.Gargoyle, _clock * 7, 1);
            Text(dc, "50 / 150", new Point(rx + 22, 270), 24, White, center: true);
            using (dc.PushTransform(Matrix.CreateTranslation(rx - 52, 316))) DrawDemonShape(dc, DemonKind.Imp, _clock * 7 + 1, 1.2);
            Text(dc, "100 / 300", new Point(rx + 22, 316), 24, White, center: true);
            Text(dc, "IN PLACE / SWOOPING", new Point(rx, 350), 20, Dim, center: true);
            Text(dc, $"PIECE LAID {GameEngine.PiecePoints}", new Point(rx, 384), 22, Dim, center: true);
            Text(dc, $"THE DEVIL {g.DevilPoints}", new Point(rx, 410), 22, Dim, center: true);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 470;
        var (w, h) = (Math.Min(Field.Right - Field.Left, Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60), g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xC0200808), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 12 ? 44 : 56, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawDevil(dc, new Point(cx, 300), 1.3, 0.5 + 0.5 * Math.Sin(_clock * 2), false);
        Text(dc, "AVA SATAN'S HOLLOW", new Point(cx, 96), 96, Lava, center: true);
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", new Point(cx, 164), 28, Dim, center: true);
        string[] lines =
        [
            "SHOOT DOWN THE GARGOYLES AND IMPS - EACH FLOCK LEAVES A BRIDGE PIECE",
            "CARRY THE PIECES TO THE LAVA AND BUILD A BRIDGE - THEN CROSS IT TO FACE THE DEVIL",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 418 + i * 30), 24, White, center: true);
        double[] xs = [cx - 300, cx - 100, cx + 100, cx + 300];
        using (dc.PushTransform(Matrix.CreateTranslation(xs[0], 498))) DrawDemonShape(dc, DemonKind.Gargoyle, _clock * 7, 1.5);
        using (dc.PushTransform(Matrix.CreateTranslation(xs[1], 498))) DrawDemonShape(dc, DemonKind.Imp, _clock * 7 + 1, 1.6);
        DrawSlab(dc, new Point(xs[2], 498));
        DrawCannon(dc, new Point(xs[3], 494), 0.8, false, 1);
        (string Name, uint Colour, string A)[] table =
        [
            ("GARGOYLE", 0xFF6AD08A, "50 - SWOOPING 150"),
            ("IMP", 0xFFFF4A2A, "100 - SWOOPING 300"),
            ("BRIDGE PIECE", Slab, "500 WHEN LAID"),
            ("SHIELD", Yellow, "BUT HOLDS YOU STILL"),
        ];
        for (int i = 0; i < table.Length; i++)
        {
            Text(dc, table[i].Name, new Point(xs[i], 536), 26, table[i].Colour, center: true);
            Text(dc, table[i].A, new Point(xs[i], 562), 21, Dim, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 620), 46, Yellow, center: true);
        Text(dc, "LEFT/RIGHT: MOVE    SPACE: FIRE    DOWN OR X: SHIELD    P: PAUSE", new Point(cx, 678), 25, Dim, center: true);
        Text(dc, $"EXTRA CANNON AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 712), 23, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE DAMNED", new Point(cx, 130), 72, Lava, center: true);
        Text(dc, "RANK NAME    SCORE  LEVEL", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
