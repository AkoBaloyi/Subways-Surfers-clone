using System.Collections.Generic;
using UnityEngine;

// One physical location along the segment where obstacles can appear.
// A segment now holds SEVERAL of these, spaced along its length, instead
// of just one — so a long segment actually has things happening
// throughout it, not just in one spot.
[System.Serializable]
public class LaneGroupSet
{
    public Transform leftJumpVariants;
    public Transform leftSlideVariants;
    public Transform middleJumpVariants;
    public Transform middleSlideVariants;
    public Transform rightJumpVariants;
    public Transform rightSlideVariants;
}