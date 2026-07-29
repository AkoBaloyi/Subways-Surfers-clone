using UnityEngine;

public class SegmentGameplay : MonoBehaviour
{
    public enum SegmentType { Empty, Obstacles, Trains, Points, Mixed }

    [Header("Gameplay Type")]
    public SegmentType segmentType;

    [Header("How many obstacle sets does this segment have?")]
    public int setCount = 3; // match this to how many sets you build below

    [Header("Current Patterns (one per set)")]
    public SegmentPattern[] currentPatterns;

    void Start()
    {
        ChooseGameplayType();
        ChoosePatterns();
        PrintPatterns();
        BuildSegment();
    }

    void ChooseGameplayType()
    {
        int randomType = Random.Range(0, 5);
        segmentType = (SegmentType)randomType;
        Debug.Log(gameObject.name + " became a " + segmentType + " segment.");
    }

    void ChoosePatterns()
    {
        currentPatterns = new SegmentPattern[setCount];
        for (int i = 0; i < setCount; i++)
            currentPatterns[i] = PatternLibrary.GetRandomPattern();
    }

    void PrintPatterns()
    {
        for (int i = 0; i < currentPatterns.Length; i++)
        {
            Debug.Log(
                $"Set {i} — LEFT: {currentPatterns[i].leftLane} | " +
                $"MIDDLE: {currentPatterns[i].middleLane} | " +
                $"RIGHT: {currentPatterns[i].rightLane}"
            );
        }
    }

    void BuildSegment()
    {
        ObstacleSpawner obstacleSpawner = GetComponent<ObstacleSpawner>();
        if (obstacleSpawner != null) obstacleSpawner.Build(currentPatterns);

        PointSpawner pointSpawner = GetComponent<PointSpawner>();
        if (pointSpawner != null) pointSpawner.Build(currentPatterns);
    }
}