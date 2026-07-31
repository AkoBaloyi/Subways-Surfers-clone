using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class StarGroupSet
{
    public Transform leftStarVariants;
    public Transform middleStarVariants;
    public Transform rightStarVariants;
}

public class PointSpawner : MonoBehaviour
{
    public List<StarGroupSet> starSets;

    public void Build(SegmentPattern[] patterns)
    {
        for (int i = 0; i < starSets.Count; i++)
        {
            StarGroupSet set = starSets[i];
            SegmentPattern pattern = patterns[i];

            ClearSet(set);
            if (pattern.leftLane == SegmentPattern.LaneContent.Points) EnableRandomChild(set.leftStarVariants);
            if (pattern.middleLane == SegmentPattern.LaneContent.Points) EnableRandomChild(set.middleStarVariants);
            if (pattern.rightLane == SegmentPattern.LaneContent.Points) EnableRandomChild(set.rightStarVariants);
        }
    }

    void ClearSet(StarGroupSet set)
    {
        ClearGroup(set.leftStarVariants); ClearGroup(set.middleStarVariants); ClearGroup(set.rightStarVariants);
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