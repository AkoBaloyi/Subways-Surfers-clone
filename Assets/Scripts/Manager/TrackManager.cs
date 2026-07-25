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

    void Start()
    {
        segmentLength = MeasureSegmentLength();

        if (segmentLength <= 0f)
        {
            Debug.LogError("Segment length came back as 0 or negative — something's wrong with my markers. Falling back to 10.");
            segmentLength = 10f;
        }
        else
        {
            Debug.Log($"Auto-detected segment length: {segmentLength}");
        }

        for (int i = 0; i < startingSegments; i++)
        {
            SpawnSegment();
        }
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
        // I spawn exactly at nextSpawnX — which always sits right at the END
        // of the previously spawned segment, so there's never a gap or overlap
        Vector3 spawnPos = Vector3.right * nextSpawnX;
        GameObject segment = Instantiate(trackPrefab, spawnPos, Quaternion.identity);
        spawnedSegments.Add(segment);
        Debug.Log($"Spawned segment starting at X: {spawnPos.x}, ending at X: {spawnPos.x + segmentLength}");

        nextSpawnX += segmentLength; // move my spawn point forward to the new segment's end, ready for next time
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