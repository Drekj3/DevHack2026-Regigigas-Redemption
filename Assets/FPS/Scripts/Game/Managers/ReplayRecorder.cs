
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ReplayFrame
{
    public float time;
    public Vector3 position;
    public Quaternion rotation;
}

public class ReplayRecorder : MonoBehaviour
{
    public Transform player;
    public Transform enemy;
    public Transform camera;
    public float recordInterval = 0.05f;

    public List<ReplayFrame> playerFrames = new List<ReplayFrame>();
    public List<ReplayFrame> enemyFrames = new List<ReplayFrame>();
    public List<ReplayFrame> cameraFrames = new List<ReplayFrame>();

    private float timer;
    private float elapsed;
    public bool recording;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9) && recording)
        {
            StopRecording();
        }

        if (!recording || player == null || enemy == null || camera == null)
            return;

        elapsed += Time.deltaTime;
        timer += Time.deltaTime;

        if (timer >= recordInterval)
        {
            timer = 0f;

            playerFrames.Add(new ReplayFrame
            {
                time = elapsed,
                position = player.position,
                rotation = player.rotation
            });

            enemyFrames.Add(new ReplayFrame
            {
                time = elapsed,
                position = enemy.position,
                rotation = enemy.rotation
            });

            cameraFrames.Add(new ReplayFrame
            {
                time = elapsed,
                position = camera.position,
                rotation = camera.rotation
            });
        }
    }

    public void StartRecording()
    {
        playerFrames.Clear();
        enemyFrames.Clear();
        cameraFrames.Clear();
        elapsed = 0f;
        timer = 0f;
        recording = true;
    }

    public void StopRecording()
    {
        recording = false;
    }

    public void Start()
    {
        StartRecording();
    }
}
