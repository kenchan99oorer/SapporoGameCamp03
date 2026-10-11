using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class CursorHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private bool _isDebugLog = false;      // ログ出力するか
    private bool _isPointerEnter = false;       // マウスが上に載っているかどうか
    private Vector3 defScale;                   // 基本サイズ                         
    
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if(_isDebugLog && _isPointerEnter)
        { // デバッグ出力
            Debug.Log("カーソルが上に乗ってるよ!");
        }
    }

    // マウスが上に乗った時呼び出される関数
    public void OnPointerEnter(PointerEventData eventData)
    {
        defScale = transform.localScale;
        transform.localScale *= 1.5f;
        _isPointerEnter = true;
    }

    // マウスが上から離れたとき呼び出される関数
    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = defScale;
        _isPointerEnter = false;
    }

    // 現在カーソルが上に載っているかの取得処理
    public bool IsPointerEnter()
    {
        return _isPointerEnter;
    }
}
