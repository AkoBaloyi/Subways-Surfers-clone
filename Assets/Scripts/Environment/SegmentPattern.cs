using System;

// SegmentPattern is just DATA — no logic at all. It describes what each
// of the 3 lanes should contain for one segment. PatternLibrary hands
// these out, SegmentGameplay stores whichever one got picked, and the
// spawner scripts below read it to know what to actually turn on.
[Serializable]
public class SegmentPattern
{
    public enum LaneContent
    {
        Empty,
        JumpObstacle,
        SlideObstacle,
        Points,
        MovingTrain,
        ParkedTrain
    }

    public LaneContent leftLane;
    public LaneContent middleLane;
    public LaneContent rightLane;
}