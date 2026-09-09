using UnityEngine;

/// <summary>
/// Keyboard and mouse stand-in for the XR pedestrian rig, so the pedestrian
/// co-simulation can be driven and tested without a headset.
///
/// It is deliberately a plain transform walker rather than a CharacterController:
/// the generated road network has no guaranteed colliders, and a
/// CharacterController with nothing to stand on falls through the world. Ground
/// following is an optional raycast that keeps the last good height when it
/// hits nothing, so this works whether or not the meshes have colliders.
///
/// The root object stays at ground level and the camera is a child at eye
/// height, mirroring how an XR Origin is laid out. That matters because
/// SimulationController reads the *root* position and sends it to SUMO, so the
/// pedestrian must be reported standing on the pavement, not floating at 1.7 m.
///
/// Uses the legacy Input API, which this project supports
/// (Active Input Handling = "Both"), so it needs no Input Actions asset.
/// </summary>
[DisallowMultipleComponent]
public class DesktopPedestrianController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Normal walking speed in m/s. 1.4 matches the ped_adult vType in " +
             "the SUMO route file.")]
    public float walkSpeed = 1.4f;

    [Tooltip("Speed while holding Shift. Kept below the 6 m/s clamp that " +
             "SimulationController applies to a pedestrian ego.")]
    public float runSpeed = 3.0f;

    [Tooltip("Speed while holding Left Ctrl, for edging up to a kerb.")]
    public float slowSpeed = 0.6f;

    [Tooltip("Seconds to reach target speed. A little smoothing stops the " +
             "speed trace sent to SUMO from being a square wave.")]
    [Range(0f, 1f)] public float acceleration = 0.15f;

    [Header("Look")]
    public float mouseSensitivity = 2.0f;
    public bool invertY = false;
    [Range(60f, 89f)] public float maxPitch = 85f;

    [Header("Rig")]
    [Tooltip("Camera height above the root, in metres. The root itself stays " +
             "on the ground because SUMO is told the root position.")]
    public float eyeHeight = 1.7f;

    [Tooltip("Camera to drive. Left empty, the first child camera is used.")]
    public Camera eyeCamera;

    [Header("Ground")]
    [Tooltip("Raycast down each frame to follow the surface. If the ray hits " +
             "nothing - e.g. the road meshes have no colliders - the current " +
             "height is kept, so this is safe to leave on.")]
    public bool stickToGround = true;

    [Tooltip("How far above the root to start the ground ray.")]
    public float groundProbeHeight = 2f;

    [Tooltip("How far down to search for ground.")]
    public float groundProbeDistance = 6f;

    [Header("Help")]
    [Tooltip("Draw the controls and live speed on screen.")]
    public bool showOverlay = true;

    private float _yaw;
    private float _pitch;
    private Vector3 _velocity;
    private bool _cursorLocked;

    private void Reset()
    {
        // Sensible defaults when the component is added by hand.
        eyeCamera = GetComponentInChildren<Camera>();
    }

    private void Start()
    {
        if (eyeCamera == null) eyeCamera = GetComponentInChildren<Camera>();
        if (eyeCamera != null)
        {
            Vector3 local = eyeCamera.transform.localPosition;
            eyeCamera.transform.localPosition = new Vector3(local.x, eyeHeight, local.z);
        }

        _yaw = transform.eulerAngles.y;
        _pitch = 0f;
        SetCursorLocked(true);
        SnapToGround();
    }

    private void Update()
    {
        HandleCursor();
        if (_cursorLocked) HandleLook();
        HandleMove();
        if (stickToGround) FollowGround();
    }

    private void HandleCursor()
    {
        // Escape releases the mouse so the Game view can be left; clicking back
        // in recaptures it. Without this the cursor is trapped in play mode.
        if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(false);
        else if (!_cursorLocked && Input.GetMouseButtonDown(0)) SetCursorLocked(true);
    }

    private void SetCursorLocked(bool locked)
    {
        _cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void HandleLook()
    {
        float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);

        _yaw += mx;
        _pitch = Mathf.Clamp(_pitch + my, -maxPitch, maxPitch);

        // Yaw turns the whole body, because SUMO is told the root's heading and
        // a pedestrian's facing should follow where they look.
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

        // Pitch is camera-only: tilting the body would tilt the reported heading
        // and push the root off the ground plane.
        if (eyeCamera != null)
            eyeCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void HandleMove()
    {
        float f = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        float r = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);

        Vector3 wish = transform.right * r + transform.forward * f;
        wish.y = 0f;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        float target = walkSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) target = runSpeed;
        else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) target = slowSpeed;

        Vector3 desired = wish * target;
        _velocity = acceleration <= 0f
            ? desired
            : Vector3.Lerp(_velocity, desired, 1f - Mathf.Exp(-Time.deltaTime / acceleration));

        transform.position += _velocity * Time.deltaTime;
    }

    private void SnapToGround()
    {
        if (!stickToGround) return;
        if (TryGetGroundY(out float y))
        {
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, y, p.z);
        }
    }

    private void FollowGround()
    {
        if (!TryGetGroundY(out float y)) return;   // no collider: keep current height
        Vector3 p = transform.position;
        // Ease onto the surface so a kerb does not snap the camera.
        transform.position = new Vector3(p.x, Mathf.Lerp(p.y, y, 1f - Mathf.Exp(-12f * Time.deltaTime)), p.z);
    }

    private bool TryGetGroundY(out float groundY)
    {
        Vector3 origin = transform.position + Vector3.up * groundProbeHeight;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                            groundProbeHeight + groundProbeDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }
        groundY = 0f;
        return false;
    }

    /// <summary>Current ground speed in m/s, for HUDs and logging.</summary>
    public float CurrentSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;

    private void OnGUI()
    {
        if (!showOverlay) return;

        const float w = 330f, h = 96f;
        GUI.Box(new Rect(10, 10, w, h), GUIContent.none);

        var style = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
        GUI.Label(new Rect(20, 16, w - 20, h),
            "<b>Desktop Pedestrian (XR stand-in)</b>\n" +
            "WASD move   Mouse look   Shift run   Ctrl slow\n" +
            "Esc release cursor   Click recapture\n" +
            $"Speed  {CurrentSpeed:0.00} m/s     Pos  ({transform.position.x:0.0}, {transform.position.z:0.0})",
            style);
    }
}
