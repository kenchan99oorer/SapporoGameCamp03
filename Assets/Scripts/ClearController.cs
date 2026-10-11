using UnityEngine;

public class ClearController : MonoBehaviour
{
    [SerializeField] private Canvas clearCanvas;
    [SerializeField] private string titleSceneName;
    [SerializeField] private TresureBoxController boxController;     // •ó” 

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        clearCanvas.enabled = false;
    }

    // Update is called once per frame
    void Update()
    {
        if (boxController.IsOpened == false && clearCanvas.enabled == false) return;

        clearCanvas.enabled = true;
    }

    public void SetNextScene()
    {
        SceneController sceneControlller = FindAnyObjectByType<SceneController>();
        sceneControlller.SetScene(titleSceneName);
    }
}
