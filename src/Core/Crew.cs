namespace DreamGame.Core;

public enum SeatRole
{
    Driver,
    Gunner,
    Commander,
}

/// <summary>A crew seat in a design, and which part of the vehicle it sits in.</summary>
public sealed class SeatInfo
{
    public Int3 Cell;
    public SeatRole Role;
    public int Section;

    /// <summary>A drone core: flown by remote, so nobody is actually sitting in it.</summary>
    public bool Remote;
}

public static class Crew
{
    public static SeatRole? RoleOf(BlockKind kind) => kind switch
    {
        BlockKind.DriverSeat => SeatRole.Driver,
        BlockKind.DroneCore => SeatRole.Driver,
        BlockKind.GunnerSeat => SeatRole.Gunner,
        BlockKind.CommanderSeat => SeatRole.Commander,
        _ => null,
    };

    public static string Name(SeatRole r) => r switch
    {
        SeatRole.Driver => "Driver",
        SeatRole.Gunner => "Gunner",
        _ => "Commander",
    };
}
