using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Pan")]
    [SerializeField] private float panSpeed = 12f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 4f;
    [SerializeField] private float minZoom = 2f;
    [SerializeField] private float maxZoom = 80f;

    private Camera _cam;

    private void Awake() => _cam = GetComponent<Camera>();

    private void Update() { HandlePan(); HandleZoom(); }

    private void HandlePan()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        if (Mathf.Approximately(h, 0f) && Mathf.Approximately(v, 0f)) return;
        float scaledSpeed = panSpeed * (_cam.orthographicSize / 10f);
        transform.Translate(new Vector3(h, v, 0f) * (scaledSpeed * Time.deltaTime), Space.World);
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(scroll, 0f)) return;
        _cam.orthographicSize -= scroll * zoomSpeed * _cam.orthographicSize;
        _cam.orthographicSize  = Mathf.Clamp(_cam.orthographicSize, minZoom, maxZoom);
    }
}