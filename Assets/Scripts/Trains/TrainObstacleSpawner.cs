using System.Collections.Generic;
using UnityEngine;

public class TrainObstacleSpawner : MonoBehaviour
{
    [Header("Train Prefabs")]
    // Add your train variations here.
    // Example:
    // Element 0 = Red Train
    // Element 1 = Blue Train
    public GameObject[] trainPrefabs;


    [Header("Lane Spawn Points")]
    // Create empty objects on each lane and assign them.
    public Transform leftLane;
    public Transform middleLane;
    public Transform rightLane;



    [Header("Player Reference")]
    public Transform player;



    [Header("Train Type")]
    // Controls the chance of getting a moving train.
    // 1 = always moving
    // 0 = always parked
    [Range(0, 1)]
    public float movingTrainChance = 0.5f;



    [Header("Spawn Timing")]
    // Time between train events.
    // Example:
    // 60 = one train every minute
    // 5 = every 5 seconds for testing
    public float spawnInterval = 60f;



    [Header("Moving Train Settings")]
    public float trainSpeed = 20f;

    public float travelDistance = 300f;



    [Header("Spawn Distance")]
    // How far ahead of player the train appears.
    public float spawnAheadDistance = 600f;



    [Header("Rotation")]
    public float trainRotationY = 90f;



    private float timer;



    private List<ActiveTrain> activeTrains =
        new List<ActiveTrain>();


    // Stores train information while it exists.
    private class ActiveTrain
    {
        public GameObject train;
        public bool moving;
        public float startX;
    }




    void Start()
    {
        if (player == null)
        {
            Debug.LogError("Player missing!");
        }


        SpawnTrainEvent();

        timer = 0;
    }




    void Update()
    {
        if (player == null)
            return;



        timer += Time.deltaTime;



        // Spawn next train after timer finishes
        if (timer >= spawnInterval)
        {
            SpawnTrainEvent();

            timer = 0;
        }



        MoveTrains();
    }





    void SpawnTrainEvent()
    {
        // Pick whether train moves or stays parked
        bool moving =
            Random.value < movingTrainChance;



        // Pick which lane the train occupies
        int blockedLane =
            Random.Range(0, 3);



        Transform lane;



        if (blockedLane == 0)
            lane = leftLane;

        else if (blockedLane == 1)
            lane = middleLane;

        else
            lane = rightLane;



        SpawnTrain(lane, moving);



        Debug.Log(
            "Train spawned on lane "
            + blockedLane
            +
            " Moving: "
            + moving
        );
    }






    void SpawnTrain(Transform lane, bool moving)
    {
        if (trainPrefabs.Length == 0)
        {
            Debug.LogError("No train prefabs!");
            return;
        }



        // Pick random train model
        GameObject prefab =
            trainPrefabs[
            Random.Range(0, trainPrefabs.Length)
            ];



        Vector3 position =
            new Vector3(
                player.position.x + spawnAheadDistance,
                lane.position.y,
                lane.position.z
            );



        Quaternion rotation =
            Quaternion.Euler(
                0,
                180 + trainRotationY,
                0
            );



        GameObject train =
            Instantiate(
                prefab,
                position,
                rotation
            );



        activeTrains.Add(
            new ActiveTrain
            {
                train = train,
                moving = moving,
                startX = position.x
            }
        );
    }





    void MoveTrains()
    {
        for (int i = activeTrains.Count - 1; i >= 0; i--)
        {
            ActiveTrain active =
                activeTrains[i];



            if (active.train == null)
            {
                activeTrains.RemoveAt(i);
                continue;
            }



            // Only moving trains move
            if (active.moving)
            {
                active.train.transform.Translate(
                    Vector3.left *
                    trainSpeed *
                    Time.deltaTime,
                    Space.World
                );
            }



            float distance =
                Mathf.Abs(
                active.train.transform.position.x
                -
                active.startX
                );



            // Remove train after passing
            if (distance > travelDistance)
            {
                Destroy(active.train);

                activeTrains.RemoveAt(i);
            }
        }
    }
}