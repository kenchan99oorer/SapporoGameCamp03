using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
using System.IO;

public class SceneController : MonoBehaviour
{
    // インスペクタからPrefab化したCanvasを居れる用の変数
    public GameObject fade;

    GameObject fadeCanvas;      // 操作するCanvas
    string lastSceneName;       // 直前のシーンの名前

    // Start is called before the first frame update
    void Start()
    {
        // FadeManager.csのisFadeInstanceを参照する
        if (!FadeManager.isFadeInstance)
        {
            Instantiate(fade);
        }

        // 起動時にCanvasの生成をちょっと待つ
        Invoke("findFadeObject", 0.02f);
    }

    // Update is called once per frame
    void Update()
    {
    }

    void findFadeObject()
    {
        fadeCanvas = GameObject.FindGameObjectWithTag("Fade");      // canvasを見つける

        // フラグを立てる
        fadeCanvas.GetComponent<FadeManager>().fadeIn();
    }

    public void NextScene()
    {
        // 現在のシーン番号に1足した番号を持っておく
        int nSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;

        // 最大シーン番号と比較して超えそうになったら0に戻す
        if (nSceneIndex >= SceneManager.sceneCountInBuildSettings)
        {
            nSceneIndex = 0;
        }

        sceneChange(nSceneIndex);
    }

    public void SetScene(string sceneName)
    {
        int nIdxScene;

        // シーンが見つからなかった場合
        if (!IsSceneInBuildSettings(sceneName, out nIdxScene))
        { // 処理をスキップ
            Debug.Log("シーンが存在しません。 : " + sceneName);
            return;
        }

        // 指定したシーンのインデックスへ移動
        sceneChange(nIdxScene);
    }

    public async void sceneChange(int sceneIndex)
    { // シーン変更の非同期処理
        // フラグを立てる
        fadeCanvas.GetComponent<FadeManager>().fadeOut();

        await Task.Delay(200);                  // 暗転するまで待つ

        lastSceneName = SceneManager.GetActiveScene().name;     // 名前を保存

        SceneManager.LoadScene(sceneIndex);     // シーン変更
    }

    public string getLastSceneName() { return lastSceneName; }

    public static bool IsSceneInBuildSettings(string sceneName, out int idx)
    {
        idx = -1;       // インデックスを無効値に設定

        // Build Settings に登録されている総シーン数をループ
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            // シーンのビルドインパスを取得 (例: "Assets/Scenes/MenuScene.unity")
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);

            // パスからファイル名（拡張子なし）を抽出 (例: "MenuScene")
            string nameInBuild = Path.GetFileNameWithoutExtension(scenePath);

            // 一致するものがあればインデックスとtrueを返す
            if (nameInBuild == sceneName)
            {
                idx = i;
                return true;
            }
        }

        return false;
    }
}
