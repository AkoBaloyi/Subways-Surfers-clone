using System.Collections.Generic;
using UnityEngine;

public class TrainSpawner : MonoBehaviour
{
    [Header("Train Prefabs")]
    public GameObject[] trainPrefabs;

    [Header("Player Reference")]
    public Transform player;

    [Header("Rail Position Markers")]
    public Transform leftRailMarker;
    public Transform rightRailMarker;

    [Header("Movement")]
    public float trainSpeed = 20f;
    public float trainTravelDistance = 300f;

    [Header("Spawn Distance")]
    // How far ahead of the player trains first appear — pushed further out
    // than before so they're visible approaching rather than popping in close.
    public float spawnAheadDistance = 600f;

    [Header("Spawn Timing (within an active wave)")]
    public float minSpawnInterval = 2f;
    public float maxSpawnInterval = 4f;

    [Header("Wave Pattern")]
    // Alternates between a burst of train spawns ("active wave") and a
    // quiet stretch with none at all ("quiet wave") — same rhythm as
    // Subway Surfers, so the player gets breathing room to focus on
    // jump/slide obstacles without trains competing for attention.
    public float minActiveWaveDuration = 6f;
    public float maxActiveWaveDuration = 12f;
    public float minQuietWaveDuration = 4f;
    public float maxQuietWaveDuration = 8f;

    [Header("Lane Pattern")]
    [Range(0f, 1f)]
    public float alternateChance = 0.8f;

    [Header("Model Orientation")]
    public float modelRotationY = 90f;

    // Other scripts (obstacle placement, difficulty, UI, whatever) can
    // subscribe to this to know exactly when and where a train spawns —
    // e.g. OnTrainSpawned += (isLeft, worldX) => { ... }
    // Fully-qualified as System.Action instead of "using System;" —
    // avoids clashing with UnityEngine.Random vs System.Random
    public static event System.Action<bool, float> OnTrainSpawned;

    // Lets any script check "is a train wave currently active?" without
    // needing its own copy of the timing logic.
    public bool IsActiveWave { get; private set; } = true;

    private float spawnTimer;
    private float nextSpawnTime;
    private float waveTimer;
    private float currentWaveDuration;
    private bool lastSpawnWasLeft;
    private bool hasSpawnedOnce = false;

    private class ActiveTrain
    {
        public GameObject obj;
        public float startX;
    }
    private List<ActiveTrain> activeTrains = new List<ActiveTrain>();

    void Start()
    {
        if (leftRailMarker == null || rightRailMarker == null)
        {
            Debug.LogError("Left Rail Marker or Right Rail Marker isn't assigned on TrainSpawner!");
        }

        StartNewWave(true);
        ScheduleNextSpawn();
    }

    void Update()
    {
        if (player == null) return;

        // wave timing — counts down regardless of whether we're actively spawning
        waveTimer += Time.deltaTime;
        if (waveTimer >= currentWaveDuration)
        {
            StartNewWave(!IsActiveWave);
        }

        // only spawn during an active wave
        if (IsActiveWave)
        {
            spawnTimer += Time.deltaTime;
            if (spawnTimer >= nextSpawnTime)
            {
                SpawnTrain();
                ScheduleNextSpawn();
                spawnTimer = 0f;
            }
        }

        for (int i = activeTrains.Count - 1; i >= 0; i--)
        {
            ActiveTrain train = activeTrains[i];
            if (train.obj == null)
            {
                activeTrains.RemoveAt(i);
                continue;
            }

            train.obj.transform.Translate(Vector3.left * trainSpeed * Time.deltaTime, Space.World);

            float traveled = Mathf.Abs(train.obj.transform.position.x - train.startX);
            if (traveled > trainTravelDistance)
            {
                Destroy(train.obj);
                activeTrains.RemoveAt(i);
            }
        }
    }

    void StartNewWave(bool makeActive)
    {
        IsActiveWave = makeActive;
        waveTimer = 0f;
        currentWaveDuration = makeActive
            ? Random.Range(minActiveWaveDuration, maxActiveWaveDuration)
            : Random.Range(minQuietWaveDuration, maxQuietWaveDuration);

        Debug.Log(makeActive
            ? $"Train wave: ACTIVE for {currentWaveDuration:F1}s"
            : $"Train wave: QUIET for {currentWaveDuration:F1}s");
    }

    void ScheduleNextSpawn()
    {
        nextSpawnTime = Random.Range(minSpawnInterval, maxSpawnInterval);
    }

    void SpawnTrain()
    {
        if (trainPrefabs.Length == 0 || leftRailMarker == null || rightRailMarker == null) return;

        GameObject prefab = trainPrefabs[Random.Range(0, trainPrefabs.Length)];

        bool spawnLeft;
        if (!hasSpawnedOnce)
        {
            spawnLeft = Random.value < 0.5f;
        }
        else if (Random.value < alternateChance)
        {
            spawnLeft = !lastSpawnWasLeft;
        }
        else
        {
            spawnLeft = lastSpawnWasLeft;
        }

        lastSpawnWasLeft = spawnLeft;
        hasSpawnedOnce = true;

        Transform marker = spawnLeft ? leftRailMarker : rightRailMarker;

        Vector3 spawnPos = new Vector3(
            player.position.x + spawnAheadDistance,
            marker.position.y,
            marker.position.z
        );

        Quaternion rotation = Quaternion.Euler(0f, 180f + modelRotationY, 0f);

        GameObject trainObj = Instantiate(prefab, spawnPos, rotation);
        activeTrains.Add(new ActiveTrain { obj = trainObj, startX = spawnPos.x });

        // announce it so any other script can react — e.g. avoid placing a
        // hard obstacle combo at the same world position, or just log it
        // for you to visually check against your segment obstacle placement
        OnTrainSpawned?.Invoke(spawnLeft, spawnPos.x);
        Debug.Log($"Train spawned {(spawnLeft ? "LEFT" : "RIGHT")} at X: {spawnPos.x:F1}");
    }
}