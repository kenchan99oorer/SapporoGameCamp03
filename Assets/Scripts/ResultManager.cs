using UnityEngine;
using UnityEngine.InputSystem;

public class ResultManager : MonoBehaviour
{
    [SerializeField] private string selectSceneName;        // セレクトシーンの名前
    [SerializeField] private AudioPlayer audioPlayer;       // オーディオプレイヤー
    public static float clearTime = 0.0f;     // クリア時間

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        audioPlayer.Play(0);
    }

    // Update is called once per frame
    void Update()
    {
        if (ButtonController.IsPressed(Key.Enter))
        {
            SetNextScene();
        }

        if (ButtonController.IsPressed(Key.Escape))
        { // アプリ終了
            Application.Quit();
        }
    }

    public void SetNextScene()
    {
        SceneController sceneControlller = FindAnyObjectByType<SceneController>();
        sceneControlller.SetScene(selectSceneName);
    }
}
