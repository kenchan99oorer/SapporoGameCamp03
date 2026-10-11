using UnityEngine;

public class PlayerBillboard : MonoBehaviour
{
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        if (mainCamera == null) return;

        transform.forward = mainCamera.transform.forward;
    }
}
