namespace EU5MapExplorer.Api.Services.GameDataLoad;

/// <summary>A 2D integer point (pixel grid vertex).</summary>
public readonly record struct Point(int X, int Y);

/// <summary>Reference to a named border segment, with direction.</summary>
public readonly record struct BorderRef(string Key, bool Reversed);

/// <summary>An ordered ring of border references that forms a closed polygon.</summary>
public sealed class BorderRing(BorderRef[] borders)
{
    public BorderRef[] Borders { get; } = borders;
}
