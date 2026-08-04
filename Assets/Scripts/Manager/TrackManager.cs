using System.Collections.Generic;
using UnityEngine;

// This script auto-detects my segment length directly from the prefab itself,
// using the Segment Start and Segment End markers already inside it.
// I don't need a separate reference object sitting in the scene anymore —
// it reads straight off the prefab asset before spawning anything.
public class TrackManager : MonoBehaviour
{
    [Header("Track Settings")]
    public GameObject trackPrefab;      // my segment prefab
    public int startingSegments = 5;    // how many segments exist before the game starts

    [Header("Player Reference")]
    public Transform player;

    private List<GameObject> spawnedSegments = new List<GameObject>();
    private float segmentLength;   // auto-calculated at Start, not typed in
    private float nextSpawnX = 0f; // always increases, never resets — this is what stopped my earlier infinite spawn/recycle loop

    // Offset from the segment's root to the leading edge of its floor. The floor inside my prefab is
    // authored a long way from the root, so spawning the root at nextSpawnX put the floor about 105
    // units behind where the player actually was. Measuring this lets me place the root wherever it
    // needs to be so that the FLOOR lands at nextSpawnX instead.
    private float floorOffsetFromRoot;
    private bool measuredFromFloor;

    void Start()
    {
        MeasureSegment();

        if (segmentLength <= 0f)
        {
            Debug.LogError("Segment length came back as 0 or negative — something's wrong with my markers. Falling back to 10.");
            segmentLength = 10f;
        }
        else
        {
            Debug.Log($"Auto-detected segment length: {segmentLength}" +
                      (measuredFromFloor ? $", measured from the floor, which sits {floorOffsetFromRoot} from the root." : ", measured from the markers."));
        }

        // Start the frontier at the END of the floor that already exists in the scene, so the first
        // segment I spawn continues from it instead of starting back at world zero and leaving a gap
        // the player runs across with nothing underneath them.
        if (measuredFromFloor)
        {
            nextSpawnX = trackPrefab.transform.position.x + floorOffsetFromRoot + segmentLength;
        }

        for (int i = 0; i < startingSegments; i++)
        {
            SpawnSegment();
        }
    }

    // Works out how long a segment is and where its floor sits relative to its root.
    // The floor is the reference rather than the markers, because the floor is what the player runs
    // on: segments have to tile edge to edge on the floor, whatever the markers happen to say.
    // Falls back to the Segment Start/End markers if there's no floor to find.
    void MeasureSegment()
    {
        segmentLength = 0f;
        floorOffsetFromRoot = 0f;
        measuredFromFloor = false;

        if (trackPrefab == null)
        {
            Debug.LogError("Track Prefab isn't assigned on TrackManager!");
            return;
        }

        Renderer floor = FindFloorRenderer();
        if (floor != null)
        {
            Bounds bounds = floor.bounds;
            segmentLength = bounds.size.x;
            floorOffsetFromRoot = bounds.min.x - trackPrefab.transform.position.x;
            measuredFromFloor = true;
            return;
        }

        Debug.LogWarning("No floor object found in the segment, so I'm falling back to the Segment Start/End markers. If the floor isn't authored near the segment root, segments won't line up under the player.");
        segmentLength = MeasureSegmentLength();
    }

    Renderer FindFloorRenderer()
    {
        foreach (Transform t in trackPrefab.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.Trim().ToLower() != "ground") continue;

            Renderer renderer = t.GetComponent<Renderer>();
            if (renderer != null) return renderer;
        }

        return null;
    }

    float MeasureSegmentLength()
    {
        if (trackPrefab == null)
        {
            Debug.LogError("Track Prefab isn't assigned on TrackManager!");
            return 0f;
        }

        // this searches EVERY child and grandchild in the prefab, including inactive ones,
        // so it doesn't matter how deep Segment Start/End are nested
        Transform[] allChildren = trackPrefab.GetComponentsInChildren<Transform>(true);

        Transform start = null;
        Transform end = null;

        foreach (Transform t in allChildren)
        {
            // trimmed + case-insensitive compare so a stray space or capital letter doesn't break it
            string cleanName = t.name.Trim().ToLower();
            if (cleanName == "segment start") start = t;
            if (cleanName == "segment end") end = t;
        }

        if (start == null || end == null)
        {
            // if this fires, I print every child name I DID find, so I can see
            // exactly what's actually named what instead of guessing
            Debug.LogError("Couldn't find Segment Start/Segment End. Here's every child name I found on the prefab:");
            foreach (Transform t in allChildren)
                Debug.Log(" - " + t.name);
            return 0f;
        }

        // my track runs along X, so I only care about the X distance between them
        return end.localPosition.x - start.localPosition.x;
    }

    void Update()
    {
        if (player == null)
        {
            Debug.LogWarning("Player field on TrackManager is empty — nothing will spawn.");
            return;
        }

        float playerDistance = player.position.x;
        float threshold = nextSpawnX - (startingSegments * segmentLength);

        if (playerDistance > threshold)
        {
            SpawnSegment();
            RecycleOldestIfNeeded();
        }
    }

    void SpawnSegment()
    {
        // nextSpawnX is where the FLOOR should begin, not where the root goes. Because the floor is
        // authored away from the root, I place the root back by that same offset so the floor lands
        // exactly where the previous segment's floor ended. Without this the roots tile correctly and
        // the floors all land about 105 units behind the player, which is what used to leave them
        // running over nothing.
        Vector3 spawnPos = Vector3.right * (nextSpawnX - floorOffsetFromRoot);
        GameObject segment = Instantiate(trackPrefab, spawnPos, Quaternion.identity);
        spawnedSegments.Add(segment);
        Debug.Log($"Spawned segment: floor runs from X {nextSpawnX} to {nextSpawnX + segmentLength} (root placed at {spawnPos.x}).");

        nextSpawnX += segmentLength; // move my frontier forward to the new segment's end, ready for next time
    }

    void RecycleOldestIfNeeded()
    {
        // once I'm a couple segments ahead, destroy the oldest instead of letting them pile up forever
        if (spawnedSegments.Count > startingSegments + 2)
        {
            GameObject oldest = spawnedSegments[0];
            spawnedSegments.RemoveAt(0);
            Destroy(oldest);
        }
    }
}