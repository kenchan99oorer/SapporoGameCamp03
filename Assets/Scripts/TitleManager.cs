using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class TitleManager : MonoBehaviour
{
    [SerializeField] private string nextSceneName;          // 次に遷移するシーンの名前
    [SerializeField] private AudioPlayer audioPlayer;       // オーディオプレイヤー

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        audioPlayer.Play(0);
    }

    // Update is called once per frame
    void Update()
    {
        if(ButtonController.IsPressed(Key.Escape))
        { // アプリ終了
            Application.Quit();
        }
        else if (ButtonController.IsPressed(Key.Enter))
        { // 次のシーンへ移行
            SetNextScene();
        }
    }

    public void SetNextScene()
    {
        SceneController sceneControlller = FindAnyObjectByType<SceneController>();
        sceneControlller.SetScene(nextSceneName);
    }
}
