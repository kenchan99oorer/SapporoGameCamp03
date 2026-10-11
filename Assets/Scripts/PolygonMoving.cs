using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PolygonMoving : MonoBehaviour
{
    [SerializeField] private GameObject targetPolygon;      // 動かすポリゴン
    [SerializeField] private List<Vector3> positionList;    // 動く予定の位置
    [SerializeField] private int idxPosition = 0;           // 現在の位置のインデックス
    [SerializeField] private float lerpSpeed = 0.01f;       // 線形補間の速度
    private int idxNext = 0;            // 次の位置のインデックス
    private float lerpTime = 0.0f;      // 線形補間の時間

    private void Update()
    { // インデックスが異ならない場合、スキップ
        if (idxPosition == idxNext) return;

        lerpTime += lerpSpeed;      // 補間を増加

        // 補間後の位置を指定
        targetPolygon.transform.position = Vector3.Lerp(positionList[idxPosition],
            positionList[idxNext],
            lerpTime);

        if (lerpTime >= 1.0f)
        { // 補間係数が1.0fを超えた場合、インデックスを次の値に切り替え
            idxPosition = idxNext;
            lerpTime = 0.0f;
        }
    }

    public void SetPositionIdx(int idx)
    { // インデックスを変更し、補間時間のリセット
        idxNext = idx % positionList.Count;
    }

    public int GetPositionIdx()
    {
        return idxPosition;
    }
}
