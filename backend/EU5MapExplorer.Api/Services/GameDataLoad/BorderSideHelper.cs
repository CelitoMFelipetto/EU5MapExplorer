namespace EU5MapExplorer.Api.Services.GameDataLoad;

/// <summary>
/// Determines which location index is on the right side of a traced border path.
/// This is critical for determining the correct "reversed" flag when building border rings.
/// </summary>
/// <remarks>
/// The right side of a directed path corresponds to the "interior" for clockwise traversal.
/// For a border between locations A and B:
/// - If A is on the right → path direction is the CW direction for A → NOT reversed for A
/// - If A is on the left → path direction is CCW for A → REVERSED for A
///
/// The right-side pixel is computed using the 2D right-normal: for direction (dx, dy),
/// the right normal is (dy, -dx). We sample the pixel at midpoint + 0.5 * rightNormal.
/// </remarks>
public static class BorderSideHelper
{
    /// <summary>
    /// Given a traced path (as int[][]), determine which location index
    /// is on the RIGHT side of the path direction.
    /// </summary>
    /// <param name="path">Traced path as array of [x, y] points.</param>
    /// <param name="colorMap">2D pixel grid [width, height] of location indices.</param>
    /// <param name="width">Map width.</param>
    /// <param name="height">Map height.</param>
    /// <returns>The location index on the right side, or -1 if outside the map.</returns>
    public static int GetRightSideIndex(int[][] path, int[,] colorMap, int width, int height)
    {
        if (path.Length < 2)
            return -1;
        return GetRightSidePixel(
            path[0][0],
            path[0][1],
            path[1][0],
            path[1][1],
            colorMap,
            width,
            height
        );
    }

    /// <summary>
    /// Given a traced path (as Point list), determine which location index
    /// is on the RIGHT side of the path direction.
    /// </summary>
    public static int GetRightSideIndex(
        IReadOnlyList<Point> path,
        int[,] colorMap,
        int width,
        int height
    )
    {
        if (path.Count < 2)
            return -1;
        return GetRightSidePixel(
            path[0].X,
            path[0].Y,
            path[1].X,
            path[1].Y,
            colorMap,
            width,
            height
        );
    }

    private static int GetRightSidePixel(
        int x1,
        int y1,
        int x2,
        int y2,
        int[,] colorMap,
        int width,
        int height
    )
    {
        int dx = x2 - x1;
        int dy = y2 - y1;

        // Right normal of (dx, dy) = (dy, -dx)
        // Sample point = midpoint + 0.5 * rightNormal
        double midX = (x1 + x2) / 2.0;
        double midY = (y1 + y2) / 2.0;
        int px = (int)Math.Floor(midX + 0.5 * dy);
        int py = (int)Math.Floor(midY - 0.5 * dx);

        if (px < 0 || py < 0 || px >= width || py >= height)
            return -1;
        return colorMap[px, py];
    }

    /// <summary>
    /// Determines whether a border path should be reversed for a given location.
    /// </summary>
    /// <param name="locIdx">The location index we're building a ring for.</param>
    /// <param name="rightSideIdx">The location index on the right side of the traced path.</param>
    /// <returns>True if the path should be reversed for this location's ring traversal.</returns>
    public static bool IsReversedForLocation(int locIdx, int rightSideIdx)
    {
        // If the location is on the right side, the path goes in the correct direction
        // for clockwise traversal of this location's boundary → not reversed.
        // If the location is NOT on the right side → reversed.
        return locIdx != rightSideIdx;
    }
}
