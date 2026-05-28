using UnityEngine;

/// <summary>
/// Ensures the ContentKit Void background is applied at runtime.
/// URP's 2D renderer can ignore camera.clearFlags set in the Editor;
/// setting them in Awake guarantees they take effect every time the scene plays.
/// Added automatically by PushmanSetup Pushman/10.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ContentKitCamera : MonoBehaviour
{
    void Awake()
    {
        var cam = GetComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.067f, 0.063f, 0.035f);  // ContentKit Void #111009
    }
}
