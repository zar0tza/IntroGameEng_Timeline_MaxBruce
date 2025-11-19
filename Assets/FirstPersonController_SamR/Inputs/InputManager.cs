using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class InputManager : MonoBehaviour, Inputs.IPlayerActions
{

    private Inputs inputs;

    void Awake()
    {
        // Initialize the Input System
        try
        {
            inputs = new Inputs();
            inputs.Player.SetCallbacks(this); // Set the callbacks for the Player action map
            inputs.Player.Enable(); // Enables the "Player" action map
        }
        catch (Exception exception)
        {
            Debug.LogError("Error initializing InputManager: " + exception.Message);
        }
    }

    #region Input Events

    // Events that are triggered when input activity is detected

    public event Action<Vector2> MoveInputEvent;
    public event Action<Vector2> LookInputEvent;

    public event Action<InputAction.CallbackContext> JumpInputEvent;
    public event Action<InputAction.CallbackContext> CrouchInputEvent;
    public event Action<InputAction.CallbackContext> SprintInputEvent;

    #endregion


    #region Input Callbacks

    // Handles input action callbacks and dispatches input data to listeners.

    public void OnMove(InputAction.CallbackContext context)
    {
        MoveInputEvent?.Invoke(context.ReadValue<Vector2>());
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        LookInputEvent?.Invoke(context.ReadValue<Vector2>());
    }


    public void OnJump(InputAction.CallbackContext context)
    {
        JumpInputEvent?.Invoke(context);

        /* old version without passing context
        if(context.started) {JumpStartedInputEvent?.Invoke();}
        if(context.performed) {JumpPerformedInputEvent?.Invoke(); }
        if(context.canceled) {JumpCanceledInputEvent?.Invoke(); }
        */
    }

    public void OnCrouch(InputAction.CallbackContext context)
    {
        CrouchInputEvent?.Invoke(context);
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        SprintInputEvent?.Invoke(context);
    }


    #endregion

    void OnEnable()
    {
        if (inputs != null)
        {
            inputs.Player.Enable();
        }
    }

    void OnDestroy()
    {
        if (inputs != null)
        {
            inputs.Player.Disable();
        }
    }
}

