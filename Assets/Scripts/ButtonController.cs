using UnityEngine;
using UnityEngine.InputSystem;

public class ButtonController : MonoBehaviour
{
    [SerializeField] private string pushAnimationName;  // ボタンを押すアニメーションの名前
    [SerializeField] private GameObject targetDoor;     // 削除 (もしくはアクションを起こすドアオブジェクト)
    [SerializeField] private GameObject switchObj;      // マテリアルを変更するモデル
    [SerializeField] private Camera cam;                // カメラ
    [SerializeField] private Vector3 allowedDirection;  // 動作を許可するカメラのベクトル
    [SerializeField] private float toleranceDeg;  // ベクトルのずれの許容範囲
    [SerializeField] private string playerTag;    // プレイヤーのタグ
    [SerializeField] private Key interractKey;    // インタラクトキー
    [SerializeField] private Material pushedMat;  // 押された後のマテリアル
    private Animator animator;          // アニメーター
    private bool isInside;              // ボタンの中に入っているか
    private bool isPressing;            // インタラクトキーが押されているか
    private bool isTriggerPressing;     // フラグのFixedUpdate持ち込み
    private bool pusing;                // 押されているか

    public bool isPushing => pusing;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    { // フラグリセット + アニメーター取得
        isInside = false;
        animator = GetComponent<Animator>();

        animator.speed = 0f;        // アニメーションの速度を0にして固定する
    }

    // Update is called once per frame
    void Update()
    { // 押されたか取得
        isPressing = IsPressed(interractKey);
        if(isPressing == true)
        {
            if (Push() == false)
            { // 失敗
                Debug.Log("Push False");
            }
        }
    }

    private void FixedUpdate()
    {
        isTriggerPressing = isPressing;
    }

    public static bool IsPressed(Key key)
    {
        return (key != Key.None) ? Keyboard.current?[key].wasPressedThisFrame ?? false : false;
    }

    /// <summary>
    /// ボタンを押す
    /// </summary>
    public bool Push()
    { // 正面に存在するか判定
        if (IsAllowedDirection(cam) == false) return false;     // 失敗

        // ボタンを押すことの出来る位置にいるか判定
        if (isInside != true) return false;     // 失敗

        // 既にボタンが押されていたら失敗
        if (pusing == true) return false;

        // Do Func
        Destroy(targetDoor);
        pusing = true;          // 押された為、フラグを立てる

        // アニメーション再生
        animator.Play(pushAnimationName);
        animator.speed = 1f;        // アニメーションの速度を1にして再生する

#if FALSE
        // マテリアルを切り替え
        var render = switchObj.GetComponent<Renderer>();
        render.material = pushedMat;
#endif

        // 押すことに成功
        return true;
    }

    private void OnTriggerEnter(Collider other)
    { // プレイヤーのタグではない場合、スキップ
        if (other.tag != playerTag) return;

        isInside = true;        // 中に入ったため、フラグを立てる
        Debug.Log("Player Inside");
    }

    private void OnTriggerExit(Collider other)
    { // プレイヤーのタグではない場合、スキップ
        if (other.tag != playerTag) return;

        isInside = false;        // 中に入ったため、フラグを下す
        Debug.Log("Player Outside");
    }

    /// <summary>
    /// ボタンの向きとカメラの向いている方向が水平かどうかを調べる 
    /// (ボタンが正面に見えているかを角度から判定する)
    /// </summary>
    private bool IsAllowedDirection(Camera cam)
    {
        return IsCameraYawParallel(cam, allowedDirection, toleranceDeg);
    }

    /// <summary>
    /// カメラのY軸回転のみの向きが、dir(水平成分)と平行かどうか判定する。
    /// 同方向・逆方向の両方を「平行」とみなす。
    /// </summary>
    public static bool IsCameraYawParallel(Camera cam, Vector3 dir, float toleranceDeg = 1f)
    {
        // カメラのY軸角度(float)
        float yaw = cam.transform.eulerAngles.y;

        // Y軸のみの向きベクトル(水平面上)
        Vector3 camDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        // 比較対象も水平面に射影
        Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);
        if (flatDir.sqrMagnitude < 1e-6f) return false;

        // 内積 = cos(角度)。1に近いほど同じ向き
        float dot = Vector3.Dot(camDir, flatDir.normalized);
        return dot > Mathf.Cos(toleranceDeg * Mathf.Deg2Rad);
    }
}
