
using UnityEngine;
using System.Collections.Generic;

public class ReplayPlayer : MonoBehaviour
{
    ReplayRecorder recorder;
    public Transform player;
    public Transform enemy;
    public Transform camera;
    public List<ReplayFrame> playerFrames;
    public List<ReplayFrame> enemyFrames;
    public List<ReplayFrame> cameraFrames;

    public float playbackSpeed = 1f;

    private float replayTime;
    private bool playing;

    private InputAction m_StartReplay;

    public void Start()
    {
        playerFrames = recorder.playerFrames;
        enemyFrames = recorder.enemyFrames;
        cameraFrames = recorder.cameraFrames;

        m_StartReplay = InputSystem.actions.FindAction("Player/Move");
        m_StartReplay.Enable();
    }

    public void Play()
    {
        if (playerFrames == null || enemyFrames == null || cameraFrames == null || playerFrames.Count < 2)
            return;

        replayTime = 0f;
        playing = true;
    }

    public void Pause()
    {
        playing = false;
    }

    public void Resume()
    {
        playing = true;
    }

    public void SetTime(float time)
    {
        replayTime = Mathf.Clamp(
            time, 0f, playerFrames[playerFrames.Count - 1].time
        );

        UpdateReplay();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9) && !playing)
        {
            Play();
        }

        if (!playing || playerFrames == null || enemyFrames == null || cameraFrames == null || playerFrames.Count < 2)
            return;

        replayTime += Time.deltaTime * playbackSpeed;

        if (replayTime >= playerFrames[playerFrames.Count - 1].time)
        {
            replayTime = playerFrames[playerFrames.Count - 1].time;
            playing = false;
        }

        UpdateReplay();
    }

    void UpdateReplay()
    {
        int index = 0;

        while (index < playerFrames.Count - 2 &&
               playerFrames[index + 1].time < replayTime)
        {
            index++;
        }

        ReplayFrame playerFrame1 = playerFrames[index];
        ReplayFrame playerFrame2 = playerFrames[index + 1];

        ReplayFrame enemyFrame1 = enemyFrames[index];
        ReplayFrame enemyFrame2 = enemyFrames[index + 1];

        ReplayFrame cameraFrame1 = cameraFrames[index];
        ReplayFrame cameraFrame2 = cameraFrames[index + 1];

        float duration = playerFrame2.time - playerFrame1.time;

        float t = duration > 0f
            ? (replayTime - playerFrame1.time) / duration
            : 0f;

        player.position =
            Vector3.Lerp(playerFrame1.position, playerFrame2.position, t);

        player.rotation =
            Quaternion.Slerp(playerFrame1.rotation, playerFrame2.rotation, t);


        enemy.position =
            Vector3.Lerp(enemyFrame1.position, enemyFrame2.position, t);

        enemy.rotation =
            Quaternion.Slerp(enemyFrame1.rotation, enemyFrame2.rotation, t);

        
        camera.position =
            Vector3.Lerp(cameraFrame1.position, cameraFrame2.position, t);

        camera.rotation =
            Quaternion.Slerp(cameraFrame1.rotation, cameraFrame2.rotation, t);
    }
}
