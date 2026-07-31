using UnityEngine;

// ============================================================================
// PATTERN LIBRARY (now procedural, not a fixed list)
// ----------------------------------------------------------------------------
// Before, this picked from 10 hand-written patterns — noticeable repeats
// after a few minutes of play. Now it GENERATES a fresh pattern every
// single time, following simple rules that keep it fair:
//
//   - Between minObstacleLanes and maxObstacleLanes get an obstacle
//   - NEVER all 3 lanes at once — one clear path always exists
//   - Leftover lanes each independently roll for Points or stay Empty
//
// SegmentGameplay doesn't change at all — it still just calls
// GetRandomPattern(). It just gets something freshly built now instead
// of something pulled off a shelf.
// ============================================================================

public static class PatternLibrary
{
    // Tune these in one place to change how busy segments feel overall.
    public static int minObstacleLanes = 1;
    public static int maxObstacleLanes = 2; // capped on purpose — 3 blocks every lane, unfair
    public static float pointsChanceOnClearLane = 0.5f;

    public static SegmentPattern GetRandomPattern()
    {
        SegmentPattern pattern = new SegmentPattern();

        SegmentPattern.LaneContent[] lanes = new SegmentPattern.LaneContent[3];
        for (int i = 0; i < lanes.Length; i++)
            lanes[i] = SegmentPattern.LaneContent.Empty;

        // shuffle WHICH physical lane (0=left, 1=middle, 2=right) gets
        // picked first, so results don't always favour the same side
        int[] laneOrder = { 0, 1, 2 };
        Shuffle(laneOrder);

        int obstacleCount = Random.Range(minObstacleLanes, maxObstacleLanes + 1);
        obstacleCount = Mathf.Min(obstacleCount, 2); // hard safety cap, never 3

        for (int i = 0; i < obstacleCount; i++)
        {
            bool jump = Random.value < 0.5f;
            lanes[laneOrder[i]] = jump
                ? SegmentPattern.LaneContent.JumpObstacle
                : SegmentPattern.LaneContent.SlideObstacle;
        }

        // whichever lanes weren't used for obstacles each get an
        // independent roll for Points
        for (int i = obstacleCount; i < 3; i++)
        {
            if (Random.value < pointsChanceOnClearLane)
                lanes[laneOrder[i]] = SegmentPattern.LaneContent.Points;
        }

        pattern.leftLane = lanes[0];
        pattern.middleLane = lanes[1];
        pattern.rightLane = lanes[2];

        return pattern;
    }

    static void Shuffle(int[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}