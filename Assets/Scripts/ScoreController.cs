using UnityEngine;
using TMPro;

public class ScoreController : MonoBehaviour
{
    private TextMeshProUGUI scoreboard;        // スコアボード

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    { // TMPro取得
        scoreboard = GetComponent<TextMeshProUGUI>();

        scoreboard.SetText("{0:2} s", ResultManager.clearTime);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
