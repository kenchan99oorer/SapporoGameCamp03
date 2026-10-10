using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private OrbitCameraController cameraController;

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

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        int direction = ReadInput();
        if (direction != 0)
        {
            cameraController.BeginStep(direction);
        }

        int dir = PlayerInput();
        if (dir != 0)
        {
            transform.position += new Vector3(dir * 4.0f * Time.deltaTime, 0.0f, 0.0f);
        }
    }

    private int ReadInput()
    {
        int dir = 0;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return 0;
        }
        if (kb[rotateLeftKey].wasPressedThisFrame) dir -= 1;
        if (kb[rotateRightKey].wasPressedThisFrame) dir += 1;
#else
        if (Input.GetKeyDown(rotateLeftKey))  dir -= 1;
        if (Input.GetKeyDown(rotateRightKey)) dir += 1;
#endif
        return dir;
    }

    private int PlayerInput()
    {
        int dir = 0;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            return 0;
        }
        if (kb[moveLeftKey].isPressed) dir -= 1;
        if (kb[moveRightKey].isPressed) dir += 1;
#else
        if (Input.GetKeyDown(moveLeftKey))  dir -= 1;
        if (Input.GetKeyDown(moveRightKey)) dir += 1;
#endif
        return dir;
    }

}


