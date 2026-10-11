using UnityEngine;

public class TresureBoxController : MonoBehaviour
{
    [SerializeField] private string openAnimationName;      // 宝箱の開くアニメーションの名前
    private Animator animator;          // アニメーター
    private bool opened;                // 開閉状態

    public bool IsOpened => opened;     // 開いているか

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    { // アニメーター取得
        animator = GetComponent<Animator>();
        opened = false;

        animator.speed = 0f;        // アニメーションの速度を0にして固定する
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    #region TEST
    private void OnTriggerEnter(Collider other)
    {
        if (other.tag != "Player") return;
        Open();
    }
    #endregion

    public void Open()
    { // 既に開いているならスキップ
        if (opened == true) return;
        // アニメーション再生
        animator.Play(openAnimationName);
        Debug.Log("Play : " + openAnimationName);

        opened = true;              // 開いた為、フラグを立てる
        animator.speed = 1f;        // アニメーションの速度を1にして再生する
    }
}
