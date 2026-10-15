namespace AVASatansHollow.Engine;

/// <summary>Left and right, the fire button and the shield.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Shield = false);

/// <summary>
/// The screen, in pixels. The battle is fought in the column between <see cref="Left"/> and <see cref="Right"/>.
/// The cannon runs along the floor of the Hollow at <see cref="ShipY"/>, from the ledge at the left (where the
/// bridge pieces are left) to the lava chasm at <see cref="ChasmX"/>, which the bridge spans.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Left = 192, Right = 832, Top = 70, Bottom = 768;
    public const double CentreX = (Left + Right) / 2;
    public const double ShipY = 704, Ground = 728;
    /// <summary>The bridge: six pieces span the chasm.</summary>
    public const int BridgePieces = 6;
    public const double PieceW = 40, ChasmX = Right - BridgePieces * PieceW - 16;
    /// <summary>Where a bridge piece waits to be picked up.</summary>
    public const double LedgeX = Left + 30;
}

public sealed class Ship
{
    public double X = Field.Left + 60;
    public bool Alive = true;
    public double ShieldTime, ShieldCool, FireCool;
    /// <summary>A bridge piece on its back, on the way to the chasm.</summary>
    public bool Carrying;
    public bool Shielded => ShieldTime > 0;
}

public sealed class Shot
{
    public double X, Y;
    public bool Dead;
}

public sealed class Bullet
{
    public double X, Y, Vx, Vy;
    /// <summary>The Devil's pitchfork: big, heavy, and it can't be shot down.</summary>
    public bool Pitchfork, Dead;
}

public enum DemonKind
{
    /// <summary>Winged gargoyles: steady, and they drop fire as they swoop.</summary>
    Gargoyle,
    /// <summary>Little red imps: quick, and they turn hard after the cannon.</summary>
    Imp,
}

public enum DemonState
{
    /// <summary>Flying in, or back from the top, to its place.</summary>
    Coming,
    /// <summary>In its place in the flock, flapping.</summary>
    Holding,
    /// <summary>Swooping down at the cannon.</summary>
    Swooping,
}

public sealed class Demon
{
    public DemonKind Kind;
    public int Slot;
    public DemonState State = DemonState.Coming;
    public double X, Y, Vx, Vy, Angle, T, Phase;
    public bool Dead;
    public int Side = 1, Shots;
    public double FireIn, TargetX;
    /// <summary>How long before it sets off, flying in.</summary>
    public double Wait;
}

/// <summary>The Devil: a great horned head over the Hollow. It takes many hits.</summary>
public sealed class Devil
{
    public const double HalfWidth = 46, HalfHeight = 50, HurtSeconds = 0.4;
    public double X = Field.CentreX, Y = -120, T, FireIn = 1.5, ForkIn = 3.5, Hurt;
    public int Hp, MaxHp;
    public bool Alive => Hp > 0;
    /// <summary>His mouth gapes as he spits fire.</summary>
    public double Mouth;
}

public enum EffectKind { Score, Burst, Word, Blast, Lava }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
