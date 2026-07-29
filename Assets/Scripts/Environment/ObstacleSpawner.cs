using System.Collections.Generic;
using UnityEngine;

public class ObstacleSpawner : MonoBehaviour
{
    [Header("Obstacle Sets (one per physical location along the segment)")]
    public List<LaneGroupSet> obstacleSets;

    // Now takes an ARRAY of patterns — one independently-generated
    // pattern per set, so each location along the segment gets its own
    // fresh layout instead of the whole segment sharing one pattern.
    public void Build(SegmentPattern[] patterns)
    {
        for (int i = 0; i < obstacleSets.Count; i++)
        {
            LaneGroupSet set = obstacleSets[i];
            SegmentPattern pattern = patterns[i];

            ClearSet(set);
            PlaceLane(pattern.leftLane, set.leftJumpVariants, set.leftSlideVariants);
            PlaceLane(pattern.middleLane, set.middleJumpVariants, set.middleSlideVariants);
            PlaceLane(pattern.rightLane, set.rightJumpVariants, set.rightSlideVariants);
        }
    }

    void PlaceLane(SegmentPattern.LaneContent content, Transform jumpGroup, Transform slideGroup)
    {
        if (content == SegmentPattern.LaneContent.JumpObstacle) EnableRandomChild(jumpGroup);
        else if (content == SegmentPattern.LaneContent.SlideObstacle) EnableRandomChild(slideGroup);
    }

    void ClearSet(LaneGroupSet set)
    {
        ClearGroup(set.leftJumpVariants); ClearGroup(set.leftSlideVariants);
        ClearGroup(set.middleJumpVariants); ClearGroup(set.middleSlideVariants);
        ClearGroup(set.rightJumpVariants); ClearGroup(set.rightSlideVariants);
    }

    void ClearGroup(Transform group)
    {
        if (group == null) return;
        foreach (Transform child in group) child.gameObject.SetActive(false);
    }

    void EnableRandomChild(Transform parent)
    {
        if (parent == null || parent.childCount == 0) return;
        int index = Random.Range(0, parent.childCount);
        parent.GetChild(index).gameObject.SetActive(true);
    }
}