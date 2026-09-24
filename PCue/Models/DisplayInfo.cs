namespace PCue.Models;

public sealed class DisplayInfo
{
    public int Index { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsPrimary { get; init; }

    public override string ToString() => Label;
}
