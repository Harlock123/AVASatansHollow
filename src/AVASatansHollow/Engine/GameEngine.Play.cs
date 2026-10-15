namespace AVASatansHollow.Engine;

public sealed partial class GameEngine
{
    public const double ShipSpeed = 300, ShotSpeed = 740, FireRepeat = 0.24, BulletSpeed = 250;
    public const double ShieldSeconds = 1.5, ShieldRecharge = 6;
    public const double StartSeconds = 2.6, ReadySeconds = 1.6, DyingSeconds = 2.2, ClearSeconds = 4, BetweenWaves = 2.5;
    public const int MaxShots = 2, Slots = 12;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 50000;
    public const int PiecePoints = 500;
    public const double LoopSeconds = 0.7;

    public int Level { get; private set; } = 1;
    /// <summary>Which wave of the level this is.</summary>
    public int Wave { get; private set; }
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Ship Ship { get; } = new();
    public List<Shot> Shots { get; } = [];
    public List<Bullet> Bullets { get; } = [];
    public List<Demon> Demons { get; } = [];
    /// <summary>The Devil, once the bridge is crossed.</summary>
    public Devil? Devil { get; private set; }
    /// <summary>How many pieces of the bridge are laid.</summary>
    public int Built { get; private set; }
    /// <summary>A bridge piece is waiting on the ledge at the left.</summary>
    public bool PieceWaiting { get; private set; }
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double WaveTime { get; private set; }
    /// <summary>For tests: nothing swoops or fires, and no new wave comes, unless a test makes it.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _diveIn, _nextWaveIn, _holdFireIn;
    private bool _firePrev, _shieldPrev, _ready;

    public double Pace => Math.Min(1.9, 1 + 0.15 * (Level - 1) + 0.03 * Math.Min(10, Wave - 1));
    public bool BridgeDone => Built >= Field.BridgePieces;
    /// <summary>Where the bridge reaches to, so far.</summary>
    public double BridgeEnd => Field.ChasmX + Built * Field.PieceW;
    /// <summary>How far right the cannon can go: the end of the bridge, or the far bank once it's built.</summary>
    public double RightLimit => BridgeDone ? Field.Right - 20 : BridgeEnd - 18;
    public int Remaining => Demons.Count(d => !d.Dead);
    public double Sway => Math.Sin(WaveTime * 0.6) * 38;
    private int MaxBullets => Math.Min(12, 4 + Wave / 2 + 2 * (Level - 1) + (Devil != null ? 4 : 0));

    /// <summary>The flock gathers over the floor of the Hollow, where the cannon can reach it.</summary>
    public const double FlockX = (Field.Left + Field.ChasmX) / 2 + 20;
    public static (double X, double Y) SlotHome(int slot, double sway) => (FlockX + (slot % 6 - 2.5) * 58 + sway, 150 + slot / 6 * 52);
    public (double X, double Y) Home(Demon d) => SlotHome(d.Slot, Sway);

    public static int Points(Demon d)
    {
        bool flying = d.State != DemonState.Holding;
        return d.Kind == DemonKind.Imp ? (flying ? 300 : 100) : (flying ? 150 : 50);
    }

    public int DevilPoints => 5000 + 2500 * (Level - 1);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Level = 1;
        Wave = 0;
        Built = 0;
        PieceWaiting = false;
        _nextExtra = FirstExtraLife;
        Devil = null;
        Demons.Clear();
        Shots.Clear();
        Bullets.Clear();
        ResetShip();
        SetPhase(Phase.Start);
    }

    /// <summary>A new level: no bridge, and the first wave flying in.</summary>
    private void BeginLevel(int level, bool demo = false)
    {
        Level = level;
        Built = demo ? _rng.Next(Field.BridgePieces + 1) : 0;
        PieceWaiting = false;
        Devil = null;
        Effects.Clear();
        Shots.Clear();
        Bullets.Clear();
        ResetShip();
        Wave = 0;
        BeginWave(1);
        SetPhase(Phase.Start);
        _ready = false;
        Say($"LEVEL {level}", BridgeDone ? "THE BRIDGE IS BUILT - CROSS IT" : "BUILD THE BRIDGE", StartSeconds);
    }

    /// <summary>The next flock flies in from the sides to its places.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Demons.Clear();
        WaveTime = 0;
        _diveIn = 2.5;
        _holdFireIn = 2;
        _nextWaveIn = 0;
        int count = Math.Min(Slots, 8 + Math.Min(4, Level - 1 + wave / 2));
        double imps = Math.Min(0.6, 0.15 + 0.08 * wave + 0.1 * (Level - 1));
        var slots = Enumerable.Range(0, Slots).OrderBy(_ => _rng.Next()).Take(count).OrderBy(s => s).ToList();
        for (int i = 0; i < slots.Count; i++)
        {
            bool fromLeft = i % 2 == 0;
            var d = new Demon
            {
                Kind = _rng.NextDouble() < imps ? DemonKind.Imp : DemonKind.Gargoyle, Slot = slots[i],
                X = fromLeft ? Field.Left - 30 : Field.Right + 30, Y = 120 + _rng.Next(80),
                Vx = fromLeft ? 1 : -1, Vy = 0.5, Wait = i / 2 * 0.3, Phase = _rng.NextDouble() * Math.PI * 2,
            };
            Demons.Add(d);
        }
        if (wave > 1) Say($"WAVE {wave}", null, 1.4);
        Play(Sfx.WaveStart);
    }

    private void ResetShip()
    {
        var s = Ship;
        s.X = Devil != null ? Field.CentreX : Field.LedgeX + 40;
        s.Alive = true;
        s.ShieldTime = s.ShieldCool = s.FireCool = 0;
        s.Carrying = false;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        WaveTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                MoveShip(c, dt, canFire: false);
                MoveDemons(dt, active: false);
                MoveDevil(dt, active: false);
                if (PhaseTime > (_ready ? ReadySeconds : StartSeconds))
                {
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                MoveShip(c, dt, canFire: true);
                MoveShots(dt);
                MoveDemons(dt, active: true);
                MoveDevil(dt, active: true);
                MoveBullets(dt);
                Hits();
                if (Phase == Phase.Play) Progress(dt);
                break;
            case Phase.Dying:
                MoveShots(dt);
                MoveDemons(dt, active: false);
                MoveDevil(dt, active: false);
                MoveBullets(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Clear:
                MoveShots(dt);
                MoveBullets(dt);
                if (PhaseTime > ClearSeconds) BeginLevel(Level + 1);
                break;
        }
    }

    // ───────────────────────────── the cannon ─────────────────────────────

    private void MoveShip(Controls c, double dt, bool canFire)
    {
        var s = Ship;
        bool fresh = c.Fire && !_firePrev, raise = c.Shield && !_shieldPrev;
        _firePrev = c.Fire;
        _shieldPrev = c.Shield;
        if (!s.Alive) return;
        if (s.ShieldTime > 0) s.ShieldTime = Math.Max(0, s.ShieldTime - dt);
        else if (s.ShieldCool > 0) s.ShieldCool = Math.Max(0, s.ShieldCool - dt);
        if (raise && canFire && s.ShieldTime <= 0 && s.ShieldCool <= 0)
        {
            s.ShieldTime = ShieldSeconds;
            s.ShieldCool = ShieldRecharge;
            Play(Sfx.Shield);
        }
        // The shield holds the cannon still.
        if (!s.Shielded) s.X = Math.Clamp(s.X + Math.Sign(c.Dx) * ShipSpeed * dt, Field.Left + 20, RightLimit);
        s.FireCool -= dt;
        if (canFire && c.Fire && (fresh || s.FireCool <= 0) && !s.Shielded && Shots.Count < MaxShots)
        {
            Shots.Add(new Shot { X = s.X, Y = Field.ShipY - 22 });
            s.FireCool = FireRepeat;
            Play(Sfx.Fire);
        }
        if (canFire) Errands();
    }

    /// <summary>Picking up a waiting piece at the ledge, laying a carried one at the end of the bridge, and crossing a finished one.</summary>
    private void Errands()
    {
        var s = Ship;
        if (PieceWaiting && !s.Carrying && s.X < Field.LedgeX + 24)
        {
            PieceWaiting = false;
            s.Carrying = true;
            Play(Sfx.PickUp);
        }
        if (s.Carrying && s.X >= BridgeEnd - 22)
        {
            s.Carrying = false;
            Built++;
            AddScore(PiecePoints);
            Effects.Add(new Effect { Kind = EffectKind.Score, X = BridgeEnd - Field.PieceW / 2, Y = Field.Ground - 30, Text = PiecePoints.ToString(), Life = 1.1, Colour = 0xFFFFE04A });
            Play(Sfx.LayPiece);
            if (BridgeDone)
            {
                Say("THE BRIDGE IS BUILT", "CROSS IT TO FACE THE DEVIL", 3);
                Play(Sfx.BridgeDone);
            }
        }
        if (BridgeDone && Devil == null && s.X >= Field.Right - 24) Cross();
    }

    /// <summary>Over the bridge: the flock scatters and the Devil rises.</summary>
    private void Cross()
    {
        foreach (var d in Demons.Where(d => !d.Dead))
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = d.X, Y = d.Y, Life = 0.5, Colour = Colour(d.Kind) });
        Demons.Clear();
        Bullets.Clear();
        int hp = 22 + 5 * (Level - 1);
        Devil = new Devil { Hp = hp, MaxHp = hp };
        Say("THE DEVIL RISES", $"{hp} HITS TO BRING HIM DOWN", 3);
        Play(Sfx.Cross);
        Play(Sfx.DevilRise);
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
            for (int k = 0; k < 4 && !s.Dead; k++)
            {
                s.Y -= ShotSpeed * dt / 4;
                if (s.Y < Field.Top) s.Dead = true;
                else if (Phase == Phase.Play && ShotHits(s)) s.Dead = true;
            }
        Shots.RemoveAll(s => s.Dead);
    }

    private bool ShotHits(Shot s)
    {
        foreach (var d in Demons)
        {
            if (d.Dead || d.Wait > 0 || Math.Abs(d.X - s.X) > 17 || Math.Abs(d.Y - s.Y) > 14) continue;
            Kill(d);
            return true;
        }
        if (Devil is { Alive: true } v && Math.Abs(s.X - v.X) < Devil.HalfWidth && Math.Abs(s.Y - v.Y) < Devil.HalfHeight && v.Y > 60)
        {
            // Rising, or still smarting from the last hit, he shrugs it off.
            if (v.Y < 190 && v.T < 3 || v.Hurt > 0)
            {
                Effects.Add(new Effect { Kind = EffectKind.Burst, X = s.X, Y = s.Y, Life = 0.2, Size = 0.3, Colour = 0xFF8A8A8A });
                return true;
            }
            v.Hp--;
            v.Hurt = Devil.HurtSeconds;
            AddScore(100);
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = s.X, Y = s.Y, Life = 0.3, Size = 0.5, Colour = 0xFFFFA040 });
            if (v.Alive) Play(Sfx.DevilHit);
            else DevilDown(v);
            return true;
        }
        return false;
    }

    private void Kill(Demon d)
    {
        int points = Points(d);
        d.Dead = true;
        AddScore(points);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = d.X, Y = d.Y, Life = 0.5, Colour = Colour(d.Kind) });
        if (d.State != DemonState.Holding)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = d.X, Y = d.Y - 14, Text = points.ToString(), Life = 0.9, Colour = points >= 300 ? 0xFFFFE04A : 0xFFFFFFFF });
        Play(d.Kind == DemonKind.Imp ? Sfx.ImpHit : Sfx.Hit);
    }

    private void DevilDown(Devil v)
    {
        AddScore(DevilPoints);
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = v.X, Y = v.Y, Life = 3, Size = 2.6 });
        Effects.Add(new Effect { Kind = EffectKind.Word, X = v.X, Y = v.Y + 80, Text = DevilPoints.ToString(), Life = 2.8, Colour = 0xFFFFE04A });
        Play(Sfx.DevilDown);
        Bullets.Clear();
        Devil = null;
        SetPhase(Phase.Clear);
        Music(false);
        Say("THE DEVIL IS DOWN", "THE BRIDGE FALLS - DEEPER YOU GO", ClearSeconds);
    }

    public static uint Colour(DemonKind k) => k == DemonKind.Imp ? 0xFFFF4A2A : 0xFF6AD08A;

    // ───────────────────────────── the flock ─────────────────────────────

    private void MoveDemons(double dt, bool active)
    {
        foreach (var d in Demons)
        {
            if (d.Dead) continue;
            if (d.Wait > 0) { d.Wait -= dt; continue; }
            d.T += dt;
            switch (d.State)
            {
                case DemonState.Coming:
                    Come(d, dt);
                    break;
                case DemonState.Holding:
                    var (hx, hy) = Home(d);
                    (d.Vx, d.Vy) = ((hx - d.X) / dt, (hy - d.Y) / dt);
                    (d.X, d.Y) = (hx, hy);
                    d.Angle = 0;
                    break;
                case DemonState.Swooping:
                    Swoop(d, dt, active);
                    break;
            }
        }
        if (active && !Calm)
        {
            Launch(dt);
            HoldFire(dt);
        }
    }

    /// <summary>In on a curve to its place: steering round towards it, then straight in for the last stretch.</summary>
    private void Come(Demon d, double dt)
    {
        var (hx, hy) = Home(d);
        double dx = hx - d.X, dy = hy - d.Y, dist = Math.Sqrt(dx * dx + dy * dy);
        double speed = 250 * Pace;
        if (dist < speed * dt + 1)
        {
            (d.X, d.Y) = (hx, hy);
            d.State = DemonState.Holding;
            d.T = 0;
            return;
        }
        double heading = Math.Atan2(d.Vy, d.Vx), want = Math.Atan2(dy, dx);
        if (dist < 70) heading = want;
        else heading += Math.Clamp(Math.Atan2(Math.Sin(want - heading), Math.Cos(want - heading)), -3.2 * dt, 3.2 * dt);
        (d.Vx, d.Vy) = (Math.Cos(heading) * speed, Math.Sin(heading) * speed);
        d.X += d.Vx * dt;
        d.Y += d.Vy * dt;
        d.Angle = 0;
    }

    /// <summary>A loop up and out, then down at the cannon (an imp turning hard after it), and on off the bottom.</summary>
    private void Swoop(Demon d, double dt, bool active)
    {
        bool imp = d.Kind == DemonKind.Imp;
        double speed = ((imp ? 250 : 200) + Math.Min(d.T, 2) * 20) * Pace;
        double heading = Math.Atan2(d.Vy, d.Vx);
        if (d.T < LoopSeconds)
        {
            heading = -Math.PI / 2 + d.Side * Math.PI * d.T / LoopSeconds;
            speed = 170;
        }
        else if (d.Y < Field.ShipY - (imp ? 90 : 140))
        {
            d.TargetX += Math.Clamp(Ship.X - d.TargetX, -(imp ? 260 : 150) * dt, (imp ? 260 : 150) * dt);
            double want = Math.Atan2(Field.ShipY + 60 - d.Y, d.TargetX - d.X);
            double turn = Math.Atan2(Math.Sin(want - heading), Math.Cos(want - heading));
            double rate = imp ? 3.4 : 2.2;
            heading += Math.Clamp(turn, -rate * dt, rate * dt);
            heading = Math.Clamp(heading, 0.3, Math.PI - 0.3);
        }
        (d.Vx, d.Vy) = (Math.Cos(heading) * speed, Math.Sin(heading) * speed);
        d.X += d.Vx * dt;
        d.Y += d.Vy * dt;
        if (d.X < Field.Left + 14 && d.Vx < 0 || d.X > Field.Right - 14 && d.Vx > 0) { d.Vx = -d.Vx; d.X = Math.Clamp(d.X, Field.Left + 14, Field.Right - 14); }
        d.Angle = Math.Atan2(d.Vx, d.Vy);
        if (active && !Calm && d.Shots > 0)
        {
            d.FireIn -= dt;
            if (d.FireIn <= 0 && d.Y > 200 && d.Y < Field.ShipY - 170 && Bullets.Count < MaxBullets)
            {
                d.Shots--;
                d.FireIn = (0.3 + 0.3 * _rng.NextDouble()) / Pace;
                Fireball(d.X, d.Y + 12, 100);
            }
        }
        if (d.Y > Field.Bottom + 24)
        {
            // Round again from the top, back to its place (or, for the last few, straight back in).
            d.Y = Field.Top - 40;
            d.T = 0;
            if (active && !Calm && Remaining <= 3) StartSwoop(d);
            else d.State = DemonState.Coming;
            (d.Vx, d.Vy) = (0, 1);
            if (d.State == DemonState.Swooping) d.T = LoopSeconds;
        }
    }

    private void Fireball(double x, double y, double maxVx, double speed = 1)
    {
        double vy = BulletSpeed * Pace * speed;
        double t = Math.Max(0.3, (Field.ShipY - y) / vy);
        double vx = Math.Clamp((Ship.X - x) / t, -maxVx, maxVx) + (_rng.NextDouble() - 0.5) * 30;
        Bullets.Add(new Bullet { X = x, Y = y, Vx = vx, Vy = vy });
        Play(Sfx.Fireball);
    }

    /// <summary>Every so often one (or, near the end of a wave, all that are left) swoops down.</summary>
    private void Launch(double dt)
    {
        var holding = Demons.Where(d => !d.Dead && d.State == DemonState.Holding).ToList();
        if (holding.Count == 0) return;
        int flying = Demons.Count(d => !d.Dead && d.State == DemonState.Swooping);
        if (Remaining <= 3)
        {
            if (flying < 3) StartSwoop(holding[_rng.Next(holding.Count)]);
            return;
        }
        _diveIn -= dt;
        if (_diveIn > 0 || flying >= Math.Min(6, 3 + Wave / 3 + Level - 1)) return;
        _diveIn = Math.Max(0.5, 1.8 - 0.12 * Wave - 0.2 * (Level - 1)) * (0.6 + 0.8 * _rng.NextDouble());
        StartSwoop(holding[_rng.Next(holding.Count)]);
    }

    private void StartSwoop(Demon d)
    {
        d.State = DemonState.Swooping;
        d.T = 0;
        d.Side = d.X < Field.CentreX ? -1 : 1;
        d.TargetX = Ship.X;
        d.Shots = d.Kind == DemonKind.Imp ? 0 : 3 + (Level >= 2 ? 1 : 0);
        d.FireIn = 0.2 + _rng.NextDouble() * 0.4;
        Play(Sfx.Dive);
    }

    /// <summary>Gargoyles in the flock drop fire when the cannon passes under them.</summary>
    private void HoldFire(double dt)
    {
        _holdFireIn -= dt;
        if (_holdFireIn > 0 || Bullets.Count >= MaxBullets) return;
        var above = Demons.Where(d => !d.Dead && d.State == DemonState.Holding && d.Kind == DemonKind.Gargoyle && Math.Abs(d.X - Ship.X) < 90).ToList();
        if (above.Count == 0) return;
        _holdFireIn = (0.9 + 0.8 * _rng.NextDouble()) / Pace;
        var d = above[_rng.Next(above.Count)];
        Fireball(d.X, d.Y + 12, 60);
    }

    // ───────────────────────────── the Devil ─────────────────────────────

    /// <summary>He rises over the Hollow, then sways from side to side, spitting fire and hurling his pitchfork.</summary>
    private void MoveDevil(double dt, bool active)
    {
        if (Devil is not { Alive: true } v) return;
        v.T += dt;
        v.Hurt = Math.Max(0, v.Hurt - dt);
        v.Mouth = Math.Max(0, v.Mouth - dt);
        if (v.Y < 190) { v.Y = Math.Min(190, v.Y + 110 * dt); return; }
        v.X = Field.CentreX + Math.Sin(v.T * 0.55 * Pace) * 210;
        v.Y = 190 + Math.Sin(v.T * 1.3) * 26;
        if (!active || Calm) return;
        v.FireIn -= dt;
        if (v.FireIn <= 0 && Bullets.Count < MaxBullets)
        {
            v.FireIn = (1.1 + 0.5 * _rng.NextDouble()) / Pace;
            v.Mouth = 0.5;
            // A fan of three from his mouth.
            double vy = BulletSpeed * Pace;
            double t = (Field.ShipY - v.Y) / vy, aim = Math.Clamp((Ship.X - v.X) / t, -120, 120);
            for (int i = -1; i <= 1; i++)
                Bullets.Add(new Bullet { X = v.X, Y = v.Y + 30, Vx = aim + i * 70, Vy = vy });
            Play(Sfx.Fireball);
            if (_rng.NextDouble() < 0.25) Play(Sfx.DevilLaugh);
        }
        v.ForkIn -= dt;
        if (v.ForkIn <= 0)
        {
            v.ForkIn = (2.6 + _rng.NextDouble()) / Pace;
            double vy = 210 * Pace, t = (Field.ShipY - v.Y) / vy;
            Bullets.Add(new Bullet { X = v.X, Y = v.Y + 20, Vx = Math.Clamp((Ship.X - v.X) / t, -150, 150), Vy = vy, Pitchfork = true });
            Play(Sfx.Pitchfork);
        }
    }

    private void MoveBullets(double dt)
    {
        foreach (var b in Bullets)
        {
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            if (b.Y > Field.Ground || b.X < Field.Left - 10 || b.X > Field.Right + 10) b.Dead = true;
        }
        Bullets.RemoveAll(b => b.Dead);
    }

    private void Hits()
    {
        var s = Ship;
        if (!s.Alive) return;
        foreach (var b in Bullets)
        {
            double reach = b.Pitchfork ? 18 : 13;
            if (Math.Abs(b.X - s.X) >= reach || Math.Abs(b.Y - Field.ShipY) >= 16) continue;
            b.Dead = true;
            if (s.Shielded) { Play(Sfx.ShieldBlock); continue; }
            Die(b.Pitchfork ? "FORKED" : "BURNT");
            return;
        }
        foreach (var d in Demons)
        {
            if (d.Dead || d.Wait > 0 || d.State == DemonState.Holding) continue;
            if (Math.Abs(d.X - s.X) >= 24 || Math.Abs(d.Y - Field.ShipY) >= 20) continue;
            Kill(d);
            if (s.Shielded) continue;
            Die("CLAWED");
            return;
        }
    }

    private void Die(string cause)
    {
        Cause = cause;
        Ship.Alive = false;
        Lives--;
        Shots.Clear();
        // A piece being carried is dropped, and turns up back on the ledge.
        if (Ship.Carrying)
        {
            Ship.Carrying = false;
            PieceWaiting = true;
        }
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = Ship.X, Y = Field.ShipY, Life = 1.4, Size = 1 });
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
        if (Devil != null) Play(Sfx.DevilLaugh);
    }

    /// <summary>The next cannon, with the flock called back to its places.</summary>
    private void Respawn()
    {
        ResetShip();
        Bullets.Clear();
        Shots.Clear();
        foreach (var d in Demons.Where(d => !d.Dead && d.State == DemonState.Swooping))
        {
            d.State = DemonState.Coming;
            d.T = 0;
        }
        if (Devil is { } v) v.FireIn = v.ForkIn = 2;
        SetPhase(Phase.Start);
        _ready = true;
        _diveIn = 2;
        Say("READY", null, ReadySeconds);
    }

    private void Progress(double dt)
    {
        if (Calm || Devil != null) return;
        if (_nextWaveIn > 0)
        {
            _nextWaveIn -= dt;
            if (_nextWaveIn <= 0) BeginWave(Wave + 1);
            return;
        }
        if (Remaining > 0) return;
        // The flock is gone: a bridge piece turns up on the ledge (if one's still wanted), and the next flock comes.
        bool wanted = Built + (PieceWaiting ? 1 : 0) + (Ship.Carrying ? 1 : 0) < Field.BridgePieces;
        if (wanted && !PieceWaiting)
        {
            PieceWaiting = true;
            Say("A BRIDGE PIECE!", "FETCH IT FROM THE LEDGE", BetweenWaves);
            Play(Sfx.PieceReady);
        }
        _nextWaveIn = BetweenWaves;
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into play, the flock in its places.</summary>
    internal void Go(int level = 1, int built = 0)
    {
        StartGame();
        if (level != 1) BeginLevel(level);
        Built = built;
        foreach (var d in Demons)
        {
            d.Wait = 0;
            d.State = DemonState.Holding;
            (d.X, d.Y) = Home(d);
        }
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void ClearDemons() => Demons.Clear();

    internal Demon PutDemon(DemonKind kind, double x, double y, DemonState state = DemonState.Holding, int slot = 0)
    {
        var d = new Demon { Kind = kind, Slot = slot, X = x, Y = y, State = state };
        if (state == DemonState.Swooping) { d.T = LoopSeconds; d.TargetX = x; (d.Vx, d.Vy) = (0, 1); }
        Demons.Add(d);
        return d;
    }

    internal void PutBullet(double x, double y, double vx = 0, double vy = BulletSpeed, bool pitchfork = false) =>
        Bullets.Add(new Bullet { X = x, Y = y, Vx = vx, Vy = vy, Pitchfork = pitchfork });

    internal void SendSwoop(Demon d) => StartSwoop(d);
    internal void GivePiece() => PieceWaiting = true;
    internal void SetBuilt(int n) => Built = n;
    internal void SetShipX(double x) => Ship.X = x;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
