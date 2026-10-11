using UnityEngine;
using UnityEngine.InputSystem;

public class ResultManager : MonoBehaviour
{
    [SerializeField] private string selectSceneName;        // セレクトシーンの名前
    public static float clearTime = 0.0f;     // クリア時間

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (ButtonController.IsPressed(Key.Enter))
        {
            SceneController sceneControlller = FindAnyObjectByType<SceneController>();
            sceneControlller.SetScene(selectSceneName);
        }

        if (ButtonController.IsPressed(Key.Escape))
        { // アプリ終了
            Application.Quit();
        }
    }
}
