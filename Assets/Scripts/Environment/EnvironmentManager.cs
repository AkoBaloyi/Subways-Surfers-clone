using System.Collections.Generic;
using UnityEngine;

// Dedicated environment spawner — separate from TrackManager so each script
// only does one job. Same auto-detect-length logic, just scoped to
// environment chunks (buildings, scenery, etc.) instead of the track itself.
//
// Auto-detects my chunk length directly from the prefab, using the
// Segment Start and Segment End markers already inside it. I don't need a
// separate reference object sitting in the scene — it reads straight off
// the prefab asset before spawning anything.
public class EnvironmentManager : MonoBehaviour
{
    [Header("Environment Settings")]
    public GameObject environmentPrefab;   // my environment chunk prefab
    public int startingChunks = 5;         // how many chunks exist before the game starts

    [Header("Player Reference")]
    public Transform player;

    [Header("Scene Cleanup")]
    [Tooltip("If a placed instance of this prefab already sits in the scene, assign it here. Its OWN 'Segment End' marker is read directly to find exactly where it ends in the world — even if this instance isn't sitting at X: 0. The first clone spawns strictly after that point. Leave empty if there isn't one.")]
    public GameObject existingSceneInstance;

    private List<GameObject> spawnedChunks = new List<GameObject>();
    private float chunkLength;     // auto-calculated at Start, not typed in
    private float nextSpawnX;      // always increases, never resets — this is what stopped my earlier infinite spawn/recycle loop

    void Start()
    {
        chunkLength = MeasureChunkLength();

        if (chunkLength <= 0f)
        {
            Debug.LogError("Chunk length came back as 0 or negative — something's wrong with my markers. Falling back to 10.");
            chunkLength = 10f;
        }
        else
        {
            Debug.Log($"Auto-detected environment chunk length: {chunkLength}");
        }

        if (existingSceneInstance != null)
        {
            // Don't assume the placed instance sits at X: 0 — read its OWN
            // Segment End marker to find exactly where it actually ends in
            // the world, then start spawning strictly after that.
            float? endX = FindSegmentEndWorldX(existingSceneInstance);

            if (endX.HasValue)
            {
                nextSpawnX = endX.Value;
                Debug.Log($"Existing scene instance '{existingSceneInstance.name}' ends at X: {nextSpawnX} (read from its own Segment End marker). First clone starts strictly there.");
            }
            else
            {
                Debug.LogWarning($"Couldn't find a Segment End marker on '{existingSceneInstance.name}' — falling back to chunk length from X: 0. Position may be off.");
                nextSpawnX = chunkLength;
            }
        }
        else
        {
            nextSpawnX = 0f;
        }

        for (int i = 0; i < startingChunks; i++)
        {
            SpawnChunk();
        }
    }

    float? FindSegmentEndWorldX(GameObject instance)
    {
        // searches every child and grandchild, including inactive ones,
        // and returns the WORLD position — this is what actually matters
        // regardless of how deep it's nested or where its parent sits
        Transform[] allChildren = instance.GetComponentsInChildren<Transform>(true);

        foreach (Transform t in allChildren)
        {
            string cleanName = t.name.Trim().ToLower();
            if (cleanName == "segment end")
                return t.position.x;
        }

        return null;
    }

    float MeasureChunkLength()
    {
        if (environmentPrefab == null)
        {
            Debug.LogError("Environment Prefab isn't assigned on EnvironmentManager!");
            return 0f;
        }

        // this searches EVERY child and grandchild in the prefab, including inactive ones,
        // so it doesn't matter how deep Segment Start/End are nested
        Transform[] allChildren = environmentPrefab.GetComponentsInChildren<Transform>(true);

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

        // my environment runs along X, so I only care about the X distance between them
        return end.localPosition.x - start.localPosition.x;
    }

    void Update()
    {
        if (player == null)
        {
            Debug.LogWarning("Player field on EnvironmentManager is empty — nothing will spawn.");
            return;
        }

        float playerDistance = player.position.x;
        float threshold = nextSpawnX - (startingChunks * chunkLength);

        if (playerDistance > threshold)
        {
            SpawnChunk();
            RecycleOldestIfNeeded();
        }
    }

    void SpawnChunk()
    {
        // I spawn exactly at nextSpawnX — which always sits right at the END
        // of the previously spawned chunk, so there's never a gap or overlap
        Vector3 spawnPos = Vector3.right * nextSpawnX;
        GameObject chunk = Instantiate(environmentPrefab, spawnPos, Quaternion.identity);
        spawnedChunks.Add(chunk);
        Debug.Log($"Spawned environment chunk starting at X: {spawnPos.x}, ending at X: {spawnPos.x + chunkLength}");

        nextSpawnX += chunkLength; // move my spawn point forward to the new chunk's end, ready for next time
    }

    void RecycleOldestIfNeeded()
    {
        // once I'm a couple chunks ahead, destroy the oldest instead of letting them pile up forever
        if (spawnedChunks.Count > startingChunks + 2)
        {
            GameObject oldest = spawnedChunks[0];
            spawnedChunks.RemoveAt(0);
            Destroy(oldest);
        }
    }
}