using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;       // 追加！

public class FadeManager : MonoBehaviour
{
    public static bool isFadeInstance = false;

    bool isFadeIn = false;              // フェードインするフラグ
    bool isFadeOut = false;             // フェードアウトするフラグ

    public float alpha = 0.0f;          // 透過率、これを変化させていく！
    public float fadeSpeed = 0.2f;      // フェードにかかる時間！

    // Start is called before the first frame update
    void Start()
    {
        // 起動時
        if (!isFadeInstance)
        {
            DontDestroyOnLoad(this);
            isFadeInstance = true;
        }
        else
        { // 起動時以外は重複しないようにする！
            Destroy(this);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (isFadeIn)
        { // フェードイン時
            alpha -= Time.deltaTime / fadeSpeed;

            if (alpha <= 0.0f)
            { // 透明になったらフェードイン終了
                isFadeIn = false;
                alpha = 0.0f;
            }

            this.GetComponentInChildren<Image>().color = new Color(0.0f, 0.0f, 0.0f, alpha);
        }
        else if (isFadeOut)
        { // フェードアウト時
            alpha += Time.deltaTime / fadeSpeed;

            if (alpha >= 1.0f)
            { // 真っ黒になったらフェードアウトを終了
                isFadeOut = false;
                alpha = 1.0f;
            }

            this.GetComponentInChildren<Image>().color = new Color(0.0f, 0.0f, 0.0f, alpha);
        }
    }

    public void fadeIn()
    { // フェードイン開始関数
        isFadeIn = true;
        isFadeOut = false;
    }

    public void fadeOut()
    { // フェードアウト開始関数
        isFadeIn = false;
        isFadeOut = true;
    }
}