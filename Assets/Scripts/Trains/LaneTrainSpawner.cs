using UnityEngine;

// Handles trains that block a PLAYER LANE as an obstacle — different job
// from Trains/TrainSpawner.cs, which spawns decorative background trains
// on the outer rails beside the track. Kept as a separate class name on
// purpose to avoid a duplicate-class compile error.
public class LaneTrainSpawner : MonoBehaviour
{
    [Header("Left Lane Train Variants")]
    public Transform leftMovingTrainVariants;
    public Transform leftParkedTrainVariants;

    [Header("Middle Lane Train Variants")]
    public Transform middleMovingTrainVariants;
    public Transform middleParkedTrainVariants;

    [Header("Right Lane Train Variants")]
    public Transform rightMovingTrainVariants;
    public Transform rightParkedTrainVariants;

    public void Build(SegmentPattern pattern)
    {
        ClearAll();
        PlaceLane(pattern.leftLane, leftMovingTrainVariants, leftParkedTrainVariants);
        PlaceLane(pattern.middleLane, middleMovingTrainVariants, middleParkedTrainVariants);
        PlaceLane(pattern.rightLane, rightMovingTrainVariants, rightParkedTrainVariants);
    }

    void PlaceLane(SegmentPattern.LaneContent content, Transform movingGroup, Transform parkedGroup)
    {
        if (content == SegmentPattern.LaneContent.MovingTrain) EnableRandomChild(movingGroup);
        else if (content == SegmentPattern.LaneContent.ParkedTrain) EnableRandomChild(parkedGroup);
    }

    void ClearAll()
    {
        ClearGroup(leftMovingTrainVariants); ClearGroup(leftParkedTrainVariants);
        ClearGroup(middleMovingTrainVariants); ClearGroup(middleParkedTrainVariants);
        ClearGroup(rightMovingTrainVariants); ClearGroup(rightParkedTrainVariants);
    }

    void ClearGroup(Transform group)
    {
        if (group == null) return;
        foreach (Transform child in group)
            child.gameObject.SetActive(false);
    }

    void EnableRandomChild(Transform parent)
    {
        if (parent == null || parent.childCount == 0) return;
        int index = Random.Range(0, parent.childCount);
        parent.GetChild(index).gameObject.SetActive(true);
    }
}