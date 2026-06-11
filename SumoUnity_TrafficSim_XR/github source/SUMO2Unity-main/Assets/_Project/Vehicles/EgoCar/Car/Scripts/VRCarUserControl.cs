using UnityEngine;
using UnityEngine.InputSystem;
using UnityStandardAssets.Vehicles.Car; // Allows us to talk to the existing CarController

[RequireComponent(typeof(CarController))]
public class VRCarUserControl : MonoBehaviour
{
    private CarController m_Car; // Reference to the physics script already on your car

    [Header("VR Input Actions")]
    public InputActionReference accelerateInput;
    public InputActionReference brakeInput;
    public InputActionReference steerInput;

    private void Awake()
    {
        // Automatically grab the CarController sitting on the same GameObject
        m_Car = GetComponent<CarController>();
    }

    private void FixedUpdate()
    {
        // 1. Read the trigger values (0.0 to 1.0)
        float accelValue = accelerateInput.action.ReadValue<float>();
        float brakeValue = brakeInput.action.ReadValue<float>();

        // 2. Read the joystick X axis (-1.0 to 1.0) for left/right
        float steerValue = steerInput.action.ReadValue<Vector2>().x;

        // 3. Combine triggers: Right Trigger moves forward (+), Left moves backward/brakes (-)
        float driveInput = accelValue - brakeValue;

        // 4. Feed these directly into the existing physics!
        // The Move function requires: (steering, accel, footbrake, handbrake)
        m_Car.Move(steerValue, driveInput, driveInput, 0f);
    }
}