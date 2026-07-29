using UnityEngine;

// ===============================================================
// SegmentContent
//
// This script lives on ONE Train Segment.
//
// Every time a segment is created, it:
//
// 1. Hides every obstacle.
// 2. Randomly chooses whether a lane gets an obstacle.
// 3. Chooses Jump OR Slide.
// 4. Chooses ONE random variant.
// 5. Enables ONLY that obstacle.
//
// Nothing gets instantiated.
// Everything is already placed by you in the Unity Editor.
//
// This keeps positioning perfect.
// ===============================================================

public class SegmentContent : MonoBehaviour
{
    //==============================================================
    // LEFT LANE
    //==============================================================

    [Header("Left Lane")]

    // Drag the "Jump Variants" object here.
    public Transform leftJumpVariants;

    // Drag the "Slide Variants" object here.
    public Transform leftSlideVariants;


    //==============================================================
    // MIDDLE LANE
    //==============================================================

    [Header("Middle Lane")]

    public Transform middleJumpVariants;

    public Transform middleSlideVariants;


    //==============================================================
    // RIGHT LANE
    //==============================================================

    [Header("Right Lane")]

    public Transform rightJumpVariants;

    public Transform rightSlideVariants;


    //==============================================================
    // RANDOM CHANCES
    //==============================================================

    [Header("Random Chances")]

    // Chance that ANY obstacle appears.
    // 0.75 = 75%
    [Range(0, 1)]
    public float obstacleChance = 0.75f;

    // Chance that obstacle is Jump instead of Slide.
    // 0.5 = 50 / 50
    [Range(0, 1)]
    public float jumpChance = 0.5f;



    //--------------------------------------------------------------
    // Unity calls Start automatically.
    //--------------------------------------------------------------
    void Start()
    {
        SetupLane(leftJumpVariants, leftSlideVariants);

        SetupLane(middleJumpVariants, middleSlideVariants);

        SetupLane(rightJumpVariants, rightSlideVariants);
    }



    //--------------------------------------------------------------
    // Handles ONE lane.
    //--------------------------------------------------------------
    void SetupLane(Transform jumpGroup, Transform slideGroup)
    {
        //----------------------------------------------------------
        // Safety check
        //----------------------------------------------------------

        if (jumpGroup == null || slideGroup == null)
            return;


        //----------------------------------------------------------
        // STEP 1
        // Turn EVERYTHING OFF.
        //----------------------------------------------------------

        DisableChildren(jumpGroup);

        DisableChildren(slideGroup);


        //----------------------------------------------------------
        // STEP 2
        // Random chance of NO obstacle.
        //----------------------------------------------------------

        if (Random.value > obstacleChance)
            return;


        //----------------------------------------------------------
        // STEP 3
        // Decide Jump OR Slide.
        //----------------------------------------------------------

        bool useJump = Random.value < jumpChance;


        if (useJump)
        {
            EnableRandomChild(jumpGroup);
        }
        else
        {
            EnableRandomChild(slideGroup);
        }
    }



    //--------------------------------------------------------------
    // Turns every child OFF.
    //--------------------------------------------------------------
    void DisableChildren(Transform parent)
    {
        foreach (Transform child in parent)
        {
            child.gameObject.SetActive(false);
        }
    }



    //--------------------------------------------------------------
    // Picks ONE random child.
    //--------------------------------------------------------------
    void EnableRandomChild(Transform parent)
    {
        if (parent.childCount == 0)
            return;

        int randomIndex = Random.Range(0, parent.childCount);

        parent.GetChild(randomIndex).gameObject.SetActive(true);
    }

}