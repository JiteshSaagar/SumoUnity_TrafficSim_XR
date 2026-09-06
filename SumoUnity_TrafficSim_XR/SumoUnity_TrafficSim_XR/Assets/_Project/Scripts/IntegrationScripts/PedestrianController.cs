using UnityEngine;

/// <summary>
/// Drives one SUMO-simulated pedestrian in Unity.
///
/// This is the pedestrian analogue of <see cref="VehicleController"/>, but
/// deliberately much simpler. A car needs a Rigidbody, local-axis velocities and
/// angular-velocity blending because it is a physical body whose heading and
/// travel direction differ. A pedestrian always walks where it faces, never
/// slides, and never needs to collide with anything - SUMO already resolved all
/// of that. Driving it through physics would only fight the incoming positions.
///
/// Positions arrive at the SUMO step rate (10 Hz by default) but must be drawn
/// at frame rate, so both position and heading are smoothed exponentially. The
/// smoothing is frame-rate independent, which matters in VR where frame times
/// vary far more than they do on a flat screen.
/// </summary>
public class PedestrianController : MonoBehaviour
{
    [Tooltip("Higher is snappier and more accurate to SUMO; lower is smoother " +
             "but lags behind. 12 tracks a 10 Hz feed closely without visible " +
             "stepping.")]
    public float positionSmoothing = 12f;

    [Tooltip("Turn-rate smoothing. Pedestrians change heading much faster than " +
             "cars, so this is higher than the vehicle equivalent.")]
    public float rotationSmoothing = 10f;

    [Tooltip("Beyond this distance the pedestrian is teleported instead of " +
             "interpolated. Catches respawns and SUMO re-inserting a person, " +
             "which would otherwise show as a long slide across the map.")]
    public float snapDistance = 6f;

    [Tooltip("Lifts the model so its feet sit on the surface. SUMO reports the " +
             "ground point, so this only compensates for a prefab whose pivot " +
             "is not at the feet.")]
    public float yOffset = 0f;

    [Tooltip("Animator float driven by walking speed in m/s. Leave empty to " +
             "skip animation entirely (e.g. a capsule placeholder).")]
    public string speedParameter = "Speed";

    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private float _speed;

    private Animator _animator;
    private int _speedHash;
    private bool _hasSpeedParam;
    private bool _initialised;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        if (_animator != null && !string.IsNullOrEmpty(speedParameter))
        {
            _speedHash = Animator.StringToHash(speedParameter);
            // Setting a parameter the controller does not declare logs a warning
            // every frame, which would drown the console at 100 pedestrians.
            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == _speedHash && p.type == AnimatorControllerParameterType.Float)
                {
                    _hasSpeedParam = true;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Called by <see cref="SimulationController"/> each time a persons message
    /// arrives. <paramref name="speed"/> is SUMO's walking speed in m/s.
    /// </summary>
    public void UpdateTarget(Vector3 pos, Quaternion rot, float speed)
    {
        _targetPos = pos + Vector3.up * yOffset;
        _targetRot = rot;
        _speed = speed;

        if (!_initialised)
        {
            // First message: place it exactly, so a pedestrian never appears to
            // slide in from wherever the prefab happened to be instantiated.
            transform.SetPositionAndRotation(_targetPos, _targetRot);
            _initialised = true;
        }
    }

    private void Update()
    {
        if (!_initialised) return;

        if ((transform.position - _targetPos).sqrMagnitude > snapDistance * snapDistance)
        {
            transform.SetPositionAndRotation(_targetPos, _targetRot);
        }
        else
        {
            // 1 - exp(-k*dt) is the frame-rate independent form of a lerp; a raw
            // Lerp(a, b, k*dt) would smooth differently at 60 and 90 fps.
            float tPos = 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime);
            float tRot = 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, _targetPos, tPos);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, tRot);
        }

        if (_hasSpeedParam)
            _animator.SetFloat(_speedHash, _speed, 0.1f, Time.deltaTime);
    }
}
