using UnityEngine;

// TEMPORARY — I have to delete this once Ako's real Player Controller exists.
// It Just moves forward automatically so we can test endless spawning.
public class TEMP_AutoRunner : MonoBehaviour
{
    public float speed = 15f;

    void Update()
    {
        transform.Translate(Vector3.right * speed * Time.deltaTime);
        // Vector3.right because our track runs along X, same as TrackManager
    }
}