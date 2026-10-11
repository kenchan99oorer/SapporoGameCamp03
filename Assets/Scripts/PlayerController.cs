using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerController : MonoBehaviour
{
    [SerializeField] private OrbitCameraController cameraController;
    [SerializeField, Min(0.0f)] private float moveSpeed = 1.0f;

#if ENABLE_INPUT_SYSTEM
    [Header("Input (Input System)")]
    [SerializeField] private Key rotateLeftKey = Key.LeftArrow;
    [SerializeField] private Key rotateRightKey = Key.RightArrow;
    [SerializeField] private Key moveLeftKey = Key.A;
    [SerializeField] private Key moveRightKey = Key.D;
#else
    [Header("Input (Legacy)")]
    [SerializeField] private KeyCode rotateLeftKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode rotateRightKey = KeyCode.RightArrow;
    [SerializeField] private KeyCode moveLeftKey = KeyCode.A;
    [SerializeField] private KeyCode moveRightKey = KeyCode.D;
#endif

    private void Awake()
    {
        if (cameraController == null)
        {
            cameraController = FindFirstObjectByType<OrbitCameraController>();
        }
    }

    private void Update()
    {
        int rotate = ReadRotateInput();
        if (rotate != 0 && cameraController != null)
        {
            cameraController.BeginStep(rotate);
        }

        int move = ReadMoveInput();
        if (move != 0)
        {
            // Move along the camera's screen-right axis so A/D stay left/right after the
            // view rotates. TargetYaw switches the axis as soon as the rotation key is
            // pressed rather than drifting with the swing.
            float yaw = cameraController != null ? cameraController.TargetYaw : 0.0f;
            Vector3 right = Quaternion.Euler(0.0f, yaw, 0.0f) * Vector3.right;
            transform.position += right * (move * moveSpeed * Time.deltaTime);
        }
    }

    private int ReadRotateInput()
    {
        int dir = 0;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return 0;
        }
        if (kb[rotateLeftKey].wasPressedThisFrame)  dir -= 1;
        if (kb[rotateRightKey].wasPressedThisFrame) dir += 1;
#else
        if (Input.GetKeyDown(rotateLeftKey))  dir -= 1;
        if (Input.GetKeyDown(rotateRightKey)) dir += 1;
#endif
        return dir;
    }

    private int ReadMoveInput()
    {
        int dir = 0;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return 0;
        }
        if (kb[moveLeftKey].isPressed)  dir -= 1;
        if (kb[moveRightKey].isPressed) dir += 1;
#else
        // Held, not pressed-this-frame, to match isPressed above.
        if (Input.GetKey(moveLeftKey))  dir -= 1;
        if (Input.GetKey(moveRightKey)) dir += 1;
#endif
        return dir;
    }
}
