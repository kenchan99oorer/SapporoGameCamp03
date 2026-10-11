using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SheepAnimationLink : MonoBehaviour
{
    private static readonly int IsMoving = Animator.StringToHash("isMoving");

    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField, Min(0f)] private float moveThreshold = 0.1f;

    private Vector3 lastPosition;

    private void Start() => lastPosition = transform.position;

    private void Update()
    {
        // 位置の変化で Idle / Walk を切り替え
        float speed = (transform.position - lastPosition).magnitude / Time.deltaTime;
        animator.SetBool(IsMoving, speed > moveThreshold);
        lastPosition = transform.position;

        // A / D で向きを切り替え(元画像は右向き)
        bool right = false;
        bool left = false;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            right = kb[Key.D].isPressed;
            left = kb[Key.A].isPressed;
        }
#else
        right = Input.GetKey(KeyCode.D);
        left = Input.GetKey(KeyCode.A);
#endif
        if (right) spriteRenderer.flipX = false;
        else if (left) spriteRenderer.flipX = true;
    }
}