using EU5MapExplorer.Api.Services.GameDataLoad;

namespace EU5MapExplorer.Api.Tests.Services.GameDataLoad;

public class RingBuilderTests
{
    /// <summary>
    /// Three border entries forming a triangle. Each entry's end connects to the
    /// next entry's start, forming a single closed ring.
    /// </summary>
    [Fact]
    public void ThreeEntries_FormSingleRing()
    {
        var entries = new List<RingBuilder.BorderEntry>
        {
            new("a|b", 0, false, new(0, 0), new(1, 0)),
            new("b|c", 0, false, new(1, 0), new(1, 1)),
            new("a|c", 0, true, new(1, 1), new(0, 0)),
        };

        var rings = RingBuilder.BuildRings(entries);

        Assert.Single(rings);
        Assert.Equal(3, rings[0].Borders.Length);
        Assert.Equal("a|b", rings[0].Borders[0].Key);
        Assert.Equal("b|c", rings[0].Borders[1].Key);
        Assert.Equal("a|c", rings[0].Borders[2].Key);
    }

    /// <summary>
    /// Two separate loops (disconnected rings). Should produce two rings.
    /// </summary>
    [Fact]
    public void TwoDisconnectedLoops_ProduceTwoRings()
    {
        var entries = new List<RingBuilder.BorderEntry>
        {
            // Ring 1: triangle
            new("a|b", 0, false, new(0, 0), new(1, 0)),
            new("b|c", 0, false, new(1, 0), new(0, 1)),
            new("a|c", 0, true, new(0, 1), new(0, 0)),
            // Ring 2: triangle elsewhere
            new("d|e", 0, false, new(5, 5), new(6, 5)),
            new("e|f", 0, false, new(6, 5), new(5, 6)),
            new("d|f", 0, true, new(5, 6), new(5, 5)),
        };

        var rings = RingBuilder.BuildRings(entries);

        Assert.Equal(2, rings.Count);
        Assert.All(rings, r => Assert.Equal(3, r.Borders.Length));
    }

    /// <summary>
    /// Single entry that doesn't connect to anything else — produces a ring of length 1.
    /// </summary>
    [Fact]
    public void SingleEntry_ProducesSingletonRing()
    {
        var entries = new List<RingBuilder.BorderEntry>
        {
            new("a|b", 0, false, new(0, 0), new(1, 0)),
        };

        var rings = RingBuilder.BuildRings(entries);

        Assert.Single(rings);
        Assert.Single(rings[0].Borders);
    }

    /// <summary>
    /// Four entries forming a square ring.
    /// </summary>
    [Fact]
    public void FourEntries_FormSquareRing()
    {
        var entries = new List<RingBuilder.BorderEntry>
        {
            new("a|b", 0, false, new(0, 0), new(1, 0)),
            new("b|c", 0, false, new(1, 0), new(1, 1)),
            new("c|d", 0, false, new(1, 1), new(0, 1)),
            new("a|d", 0, true, new(0, 1), new(0, 0)),
        };

        var rings = RingBuilder.BuildRings(entries);

        Assert.Single(rings);
        Assert.Equal(4, rings[0].Borders.Length);
    }

    [Fact]
    public void EmptyInput_ReturnsNoRings()
    {
        var rings = RingBuilder.BuildRings([]);
        Assert.Empty(rings);
    }
}
