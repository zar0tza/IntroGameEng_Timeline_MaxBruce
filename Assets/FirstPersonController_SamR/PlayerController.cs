using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// The main class responsible for character movement, looking, and state management.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Debug Info: Read Only")]

    // Current state of the character.
    public MovementState currentMovementState;
    // Magnitude of the CharacterController's velocity vector, useful for determining if the player is moving.
    public float characterVelocity;

    #region Toggle features
    [Header("Enable/Disable Controls & Features")]
    public bool debugLogEnabled = false;

    // Global toggles to quickly enable/disable major features of the controller.
    public bool moveEnabled = true;
    public bool lookEnabled = true;

    [SerializeField] private bool jumpEnabled = true;

    [SerializeField] private bool sprintEnabled = true;
    // If true, sprint key must be held down; if false, sprint is toggled on/off.
    [SerializeField] private bool holdToSprint = true;

    [SerializeField] private bool crouchEnabled = true;
    // If true, crouch key must be held down; if false, crouch is toggled on/off.
    [SerializeField] private bool holdToCrouch = true;
    #endregion

    #region Move Settings
    [Header("Move Settings")]
    [SerializeField] private float crouchMoveSpeed = 2.0f;
    [SerializeField] private float walkMoveSpeed = 4.0f;
    [SerializeField] private float sprintMoveSpeed = 7.0f;

    // Time in seconds for the character's movement speed to transition between states (e.g., walk to sprint).
    private float speedTransitionDuration = 0.25f;

    // Tracks the current interpolated speed, used for smooth acceleration/deceleration.
    [SerializeField] private float currentMoveSpeed;

    // Flags to track active input for sprint and crouch.
    [SerializeField] private bool sprintInput = false;
    [SerializeField] private bool crouchInput = false;

    // Stores the vertical velocity (y-axis) for gravity and jumping. Horizontal is handled separately.
    private Vector3 velocity; // This holds the vertical (Y) component of movement, managed by gravity/jump logic.
    #endregion  

    #region Look Settings
    [Header("Look Settings")]
    // Sensitivity values for mouse/joystick look input.
    public float horizontalLookSensitivity = 30;
    public float verticalLookSensitivity = 30;

    // Limits the vertical camera rotation (look up/down).
    public float LowerLookLimit = -60;
    public float upperLookLimit = 60;

    // Toggle for inverting the Y-axis look direction.
    public bool invertLookY { get; private set; } = false;
    #endregion

    #region Jump & Gravity Settings
    [Header("Jump & Gravity Settings")]
    // Tracks if the CharacterController is currently on the ground.
    [SerializeField] private bool isGrounded;

    // Rate of gravitational acceleration.
    [SerializeField] private float gravity = 30.0f;

    // Maximum height the character can jump in Unity meters
    [SerializeField] private float jumpHeight = 2.0f;

    // Time in seconds to prevent the player from immediately jumping again (input buffer/cooldown).
    private float jumpCooldownAmount = 0.2f;
    private float jumpCooldownTimer = 0f;

    // Flag set by input to request a jump, processed in ApplyJumpAndGravity.
    private bool jumpRequested = false;

    // Radius for the Physics.CheckSphere used to detect the ground. Slightly larger than the CC radius for stability.
    private float groundCheckRadius;

    // Toggle to switch between checking all layers (except 'Player') or only a specified 'Ground' layer.
    private bool groundCheckLayered = false;

    // World position of the sphere check for debugging/Gizmos.
    private Vector3 checkSpherePosition;
    #endregion

    #region Crouch Settings
    [Header("Crouch Settings")]
    // Time in seconds for the CharacterController's height/center to smoothly change.
    [SerializeField] private float crouchTransitionDuration = 0.2f;

    // Target height when crouching.
    [SerializeField] private float crouchingHeight = 1.0f;

    // Target center offset when crouching (lowers the capsule to maintain the bottom in place).
    [SerializeField] private Vector3 crouchingCenter = new Vector3(0, 0.5f, 0);

    // Target camera Y position when crouching.
    [SerializeField] private float crouchingCamY = 0.75f;

    // Default values, set at Awake, taken from the CharacterController's initial configuration.
    private float standingHeight;
    private Vector3 standingCenter;
    private float standingCamY;

    // Flag to indicate if the player is currently prevented from standing due to an overhead obstruction.
    private bool isObstructed = false;

    // Interpolation targets for the CharacterController properties and camera height.
    private float targetHeight;
    private Vector3 targetCenter;
    private float targetCamY; // Target Y position for camera root during crouch transition

    // Layer mask that excludes the "Player" layer itself to prevent self-collision/self-detection in checks.
    private int playerLayerMask;
    #endregion

    [Header("Spawn Position (Optional)")]
    // Optional position to teleport the player to (e.g., for respawning).
    public Transform spawnPosition; // Public variable to set a default or quick respawn point in the Inspector.

    // Input Variables (Read from InputManager events)
    private Vector2 moveInput; // Stores the raw horizontal (X) and vertical (Y) movement input.
    private Vector2 lookInput; // Stores the raw horizontal (X) and vertical (Y) look input.

    // Cached references
    private InputManager inputManager;
    private CharacterController characterController;
    private Transform cameraRoot;

    // Defines the discrete states the character can be in. Used to drive logic like speed, animations (if implemented), and available actions.   
    public enum MovementState
    {
        Idle,
        Walking,
        Sprinting,
        Crouching,
        Jumping,
        Falling
    }

    private void Awake()
    {

        inputManager = GetComponent<InputManager>();
        characterController = GetComponent<CharacterController>();
        cameraRoot = transform.Find("CameraRoot");


        #region Initialize Default values
        currentMovementState = MovementState.Idle;
        currentMoveSpeed = walkMoveSpeed; // Start at walk speed

        // Initialize crouch variables by storing the initial CharacterController values as 'standing' defaults.
        standingHeight = characterController.height;
        standingCenter = characterController.center;
        standingCamY = cameraRoot.localPosition.y;

        // Set initial targets to standing state.
        targetHeight = standingHeight;
        targetCenter = standingCenter;
        targetCamY = cameraRoot.localPosition.y;

        // set default state of bools
        crouchInput = false;
        sprintInput = false;

        // Set up the ground check sphere radius, slightly larger than the CC radius for stable detection.
        groundCheckRadius = characterController.radius + 0.05f;

        // Ensure the "Player" layer exists in the project (Editor-only) and assign this GameObject to it.
        EnsurePlayerLayerExistsAndAssign();

        // Set the layer mask to exclude the "Player" layer. This is used for general ground checks and obstruction checks.
        playerLayerMask = ~LayerMask.GetMask("Player");

        #endregion
    }

    private void Update()
    {
        // Handle all physics-related movement logic.
        HandlePlayerMovement();
    }

    private void LateUpdate()
    {
        // Handle camera movement (look), which should occur after all position updates for smooth tracking.
        HandlePlayerLook();
    }

    // The main update loop for physical character movement and state management.
    public void HandlePlayerMovement()
    {
        // Calculate the current horizontal movement speed magnitude.
        characterVelocity = characterController.velocity.magnitude;

        // Early exit if movement is globally disabled.
        if (moveEnabled == false) return;

        // Determine the current state (Idle, Walking, Crouching, etc.)
        DetermineMovementState();

        // Perform the ground detection check.
        GroundedCheck();

        // Manage the smooth transition between standing and crouching height/center.
        HandleCrouchTransition();

        // Calculate and apply the final movement vector using the CharacterController.
        ApplyFinalMovement();
    }

    // Determines the character's movement state based on input, grounding, and conditions.    
    private void DetermineMovementState()
    {
        // --- Air Check ---
        if (isGrounded == false)
        {
            // Check if the player is moving upwards (a jump is active) or downwards (falling).
            // A small tolerance (0.1f) is used to distinguish active jump/fall from near-zero vertical velocity.
            if (velocity.y > 0.1f)
            {
                currentMovementState = MovementState.Jumping;
            }
            else if (velocity.y < 0)
            {
                currentMovementState = MovementState.Falling;
            }
            // No need for an 'else' here, as the state will carry over from the moment they left the ground (Jumping/Falling).
        }

        // --- Ground Check ---
        else if (isGrounded == true)
        {
            // Crouch check: Priority over walk/sprint. Also forces crouching if obstructed.
            if (crouchInput == true || isObstructed == true)
            {
                currentMovementState = MovementState.Crouching;
            }

            // Sprint check: Only possible if not crouching.
            else if (sprintInput == true)
            {
                currentMovementState = MovementState.Sprinting;
            }

            // Walk check: Moving, but not sprinting or crouching.
            // A small tolerance (0.1f) checks for significant horizontal input. 
            else if (moveInput.magnitude > 0.1f)
            {
                currentMovementState = MovementState.Walking;
            }

            // Idle Check: No significant horizontal input.
            else if (moveInput.magnitude <= 0.1f)
            {
                currentMovementState = MovementState.Idle;
            }
        }
    }


    // Calculates the movement vector (horizontal and vertical) and applies it to the CharacterController.
    private void ApplyFinalMovement()
    {
        // Step 1: Get input direction (X and Y from input map to X and Z in world space).
        Vector3 moveInputDirection = new Vector3(moveInput.x, 0, moveInput.y);
        // Convert local input direction to world space, relative to the player's rotation.
        Vector3 worldMoveDirection = transform.TransformDirection(moveInputDirection);

        // Step 2: Determine movement speed based on the current state.
        float targetMoveSpeed;

        switch (currentMovementState)
        {
            case MovementState.Crouching: { targetMoveSpeed = crouchMoveSpeed; break; }
            case MovementState.Sprinting: { targetMoveSpeed = sprintMoveSpeed; break; }
            // Default to walking speed for Idle/Walking/Jumping/Falling states (allows air control).
            default: { targetMoveSpeed = walkMoveSpeed; break; }
        }

        // Step 3: Smoothly interpolate current speed towards the target speed for realistic acceleration/deceleration.
        // Uses an exponential decay (like a Spring-Damper) to smooth the transition.
        // '0.01f' determines the termination condition (how close to the target is enough).
        float lerpSpeed = 1f - Mathf.Pow(0.01f, Time.deltaTime / speedTransitionDuration);
        currentMoveSpeed = Mathf.Lerp(currentMoveSpeed, targetMoveSpeed, lerpSpeed);


        // Step 4: Handle horizontal movement.
        // Normalizing 'worldMoveDirection' prevents faster diagonal movement.
        Vector3 horizontalMovement = worldMoveDirection.normalized * currentMoveSpeed;

        // Step 5: Handle jumping and gravity, updating the vertical velocity (`velocity.y`).
        ApplyJumpAndGravity();

        // Step 6: Combine horizontal and vertical movement.
        Vector3 movement = horizontalMovement;
        movement.y = velocity.y; // Incorporate the calculated vertical velocity.

        // Step 7: Apply final movement to the CharacterController.
        characterController.Move(movement * Time.deltaTime);
    }

    // Handles the camera look rotation based on look input.
    public void HandlePlayerLook()
    {
        // Early exit if looking is globally disabled.
        if (lookEnabled == false) return;

        float lookX = lookInput.x * horizontalLookSensitivity * Time.deltaTime;
        float lookY = lookInput.y * verticalLookSensitivity * Time.deltaTime;

        // Invert vertical look if needed
        if (invertLookY)
        {
            lookY = -lookY;
        }

        // Rotate character (parent GameObject) on Y-axis for left/right look.
        transform.Rotate(Vector3.up * lookX);

        // Tilt cameraRoot on X-axis for up/down look (prevents character model from tilting).
        Vector3 currentAngles = cameraRoot.localEulerAngles;
        float newRotationX = currentAngles.x - lookY;

        // Convert the angle from [0, 360] to a signed angle [-180, 180] for easier clamping.
        newRotationX = (newRotationX > 180) ? newRotationX - 360 : newRotationX;
        // Clamp the vertical angle to the set limits.
        newRotationX = Mathf.Clamp(newRotationX, LowerLookLimit, upperLookLimit);

        // Apply the new rotation to the camera root.
        cameraRoot.localEulerAngles = new Vector3(newRotationX, 0, 0);

    }

    // Manages the vertical velocity for jumping and gravity application.    
    private void ApplyJumpAndGravity()
    {

        // Process jump if all conditions are met:
        // Note: The input handler sets jumpRequested, but we double-check grounding/cooldown here for safety.
        if (jumpRequested == true && isGrounded && jumpCooldownTimer <= 0f && jumpEnabled)
        {

            if (debugLogEnabled) Debug.Log("Applying Jump");
            // Calculate the initial upward velocity needed ($v_0 = \sqrt{2gh}$) to reach the desired jumpHeight.
            velocity.y = Mathf.Sqrt(2f * jumpHeight * gravity);

            // Reset the jump request flag so it only triggers once per button press.
            jumpRequested = false;

            // Start the full jump cooldown timer to prevent immediate re-jumping.
            jumpCooldownTimer = jumpCooldownAmount;
        }


        // Apply gravity based on the player's current state (grounded or in air).
        if (isGrounded && velocity.y < 0)
        {
            // If grounded and moving downwards, snap velocity to a small negative value (a "stick-to-ground" force).
            // This ensures the character stays grounded firmly and can reliably detect ground contact.
            velocity.y = -2f; // Standard CharacterController trick to force ground contact and prevent gravity accumulation.
        }
        else // If not grounded (in the air):
        {
            // Apply standard gravity, accelerating the vertical velocity downwards.
            velocity.y -= gravity * Time.deltaTime;
        }


        // Update jump cooldown timer.
        if (jumpCooldownTimer > 0)
        {
            jumpCooldownTimer -= Time.deltaTime;
        }

    }

    // Handles the smooth transition of the CharacterController's height and center (and camera position)
    // between standing and crouching states, including checking for overhead obstructions.
    private void HandleCrouchTransition()
    {
        // Determine if the player should actively crouch (input is true AND not actively airborne from a jump).
        bool shouldCrouch = crouchInput == true && currentMovementState != MovementState.Jumping && currentMovementState != MovementState.Falling;

        // Check if the character is currently below standing height (i.e., was already crouching).
        // Uses a small tolerance (0.05f) to account for floating point errors.
        bool wasAlreadyCrouching = characterController.height < (standingHeight - 0.05f);

        // Logic to maintain crouch state if the player walks off a ledge while crouching.
        if (isGrounded == false && wasAlreadyCrouching)
        {
            shouldCrouch = true; // Maintain crouch state if airborne.
        }

        if (shouldCrouch)
        {
            // Set targets to crouching values.
            targetHeight = crouchingHeight;
            targetCenter = crouchingCenter;
            targetCamY = crouchingCamY;
            isObstructed = false; // Intentionally crouching means no obstruction check is needed to initiate.
        }
        else
        {
            // Player is trying to stand up (or is idle/moving/sprinting and not actively crouching).
            float maxAllowedHeight = GetMaxAllowedHeight();

            // Check if there's enough space to stand up fully (or near full height).
            // Uses a small tolerance (0.05f) for the full standing height check. 
            if (maxAllowedHeight >= standingHeight - 0.05f)
            {
                // No obstruction, allow transition to full standing height/center/camera.
                targetHeight = standingHeight;
                targetCenter = standingCenter;
                targetCamY = standingCamY;
                isObstructed = false;
            }

            else
            {
                // Obstruction detected, limit height and smoothly interpolate center/camera to the max allowed height.
                targetHeight = Mathf.Min(standingHeight, maxAllowedHeight);
                // Calculate interpolation ratio based on how close the limited height is to standing height (0=crouch, 1=stand).
                float standRatio = Mathf.Clamp01((targetHeight - crouchingHeight) / (standingHeight - crouchingHeight));
                targetCenter = Vector3.Lerp(crouchingCenter, standingCenter, standRatio);
                targetCamY = Mathf.Lerp(crouchingCamY, standingCamY, standRatio);
                isObstructed = true; // Mark as obstructed to affect movement state.
            }
        }

        // Calculate lerp speed based on desired duration using the exponential smoothing formula.
        // '0.01f' determines the termination condition (how close to the target is enough).
        float lerpSpeed = 1f - Mathf.Pow(0.01f, Time.deltaTime / crouchTransitionDuration);

        // Smoothly transition CharacterController properties.
        characterController.height = Mathf.Lerp(characterController.height, targetHeight, lerpSpeed);
        characterController.center = Vector3.Lerp(characterController.center, targetCenter, lerpSpeed);

        // Smoothly transition camera root position. Only Y-axis is adjusted.
        Vector3 currentCamPos = cameraRoot.localPosition;
        cameraRoot.localPosition = new Vector3(currentCamPos.x, Mathf.Lerp(currentCamPos.y, targetCamY, lerpSpeed), currentCamPos.z);

    }

    #region Helper Methods

#if UNITY_EDITOR
    // This runs when the editor loads scripts or recompiles — ensures the layer is created in edit mode (persists).
    [InitializeOnLoadMethod]
    private static void CreatePlayerLayerIfMissingOnEditorLoad()
    {
        const string playerLayerName = "Player";

        if (LayerMask.NameToLayer(playerLayerName) != -1) return;

        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning("Unable to access TagManager.asset to add layers. Please add the 'Player' layer manually via Project Settings > Tags and Layers.");
            return;
        }

        var tagManagerAsset = assets[0];
        var tagManager = new SerializedObject(tagManagerAsset);
        var layersProp = tagManager.FindProperty("layers");
        bool added = false;

        for (int i = 8; i <= 31; i++)
        {
            var layerProp = layersProp.GetArrayElementAtIndex(i);
            if (layerProp != null && string.IsNullOrEmpty(layerProp.stringValue))
            {
                layerProp.stringValue = playerLayerName;
                tagManager.ApplyModifiedProperties();
                EditorUtility.SetDirty(tagManagerAsset);
                AssetDatabase.SaveAssets();
                added = true;
                Debug.Log($"Added layer '{playerLayerName}' at index {i} (Editor).");
                break;
            }
        }

        if (!added)
        {
            Debug.LogWarning($"Could not add layer '{playerLayerName}': no empty user layer slots (8-31). Please add it manually via Project Settings > Tags and Layers.");
        }
    }
#endif

    // This is only here to solve an issue with Importing the package into a new project
    // The "Player" layer is not brought over with the package.
    // So we check to see if "Player" layer exists, and if not, we create it.
    private void EnsurePlayerLayerExistsAndAssign()
    {
        const string playerLayerName = "Player";

#if UNITY_EDITOR
        // If the layer already exists assign it immediately.
        int existing = LayerMask.NameToLayer(playerLayerName);
        if (existing != -1)
        {
            gameObject.layer = existing;
            return;
        }

        // We're running in the Editor but the layer wasn't present at Awake.
        // Prefer that layer creation happen in edit mode (the static initializer above).
        // If we are in play mode, avoid editing ProjectSettings (would be applied only during play and often reverted).
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning($"'{playerLayerName}' layer not found. It will be created in the Editor (outside Play Mode). Please stop play mode to persist the layer.");
            return;
        }

        // If not playing (i.e. Awake called in edit mode for some reason), attempt to add immediately.
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning("Unable to access TagManager.asset to add layers. Please add the 'Player' layer manually via Project Settings > Tags and Layers.");
            return;
        }

        var tagManagerAsset = assets[0];
        var tagManager = new SerializedObject(tagManagerAsset);
        var layersProp = tagManager.FindProperty("layers");
        bool added = false;

        for (int i = 8; i <= 31; i++)
        {
            var layerProp = layersProp.GetArrayElementAtIndex(i);
            if (layerProp != null && string.IsNullOrEmpty(layerProp.stringValue))
            {
                layerProp.stringValue = playerLayerName;
                tagManager.ApplyModifiedProperties();
                EditorUtility.SetDirty(tagManagerAsset);
                AssetDatabase.SaveAssets();
                added = true;
                if (debugLogEnabled) Debug.Log($"Added layer '{playerLayerName}' at index {i}.");
                break;
            }
        }

        if (!added)
        {
            Debug.LogWarning($"Could not add layer '{playerLayerName}': no empty user layer slots (8-31). Please add it manually via Project Settings > Tags and Layers.");
        }

        // Assign if it exists now.
        int layerIndexEditor = LayerMask.NameToLayer(playerLayerName);
        if (layerIndexEditor != -1)
        {
            gameObject.layer = layerIndexEditor;
        }
#else
        // In builds we cannot modify project layers. If the layer exists in the build, assign it.
        int layerIndex = LayerMask.NameToLayer(playerLayerName);
        if (layerIndex != -1)
        {
            gameObject.layer = layerIndex;
        }
#endif
    }

    // Performs a sphere-cast at the bottom of the CharacterController to determine if the player is grounded.
    private void GroundedCheck()
    {
        // Store the state from the previous frame.
        bool previouslyGrounded = isGrounded;

        // Compute the stable center for the ground check sphere at the bottom of the capsule.
        // It's placed at the bottom point of the CC capsule (position + center - half height + radius).
        Vector3 bottomCenter = transform.position + characterController.center - Vector3.up * (characterController.height * 0.5f - characterController.radius);
        checkSpherePosition = bottomCenter;

        bool rawGrounded;

        // Perform the sphere check, ignoring triggers.
        if (groundCheckLayered == false)
        {
            // Check against everything EXCEPT the player layer.
            rawGrounded = Physics.CheckSphere(checkSpherePosition, groundCheckRadius, playerLayerMask, QueryTriggerInteraction.Ignore);
        }
        else
        {
            // Check only against the specific "Ground" layer (if toggle is enabled).
            rawGrounded = Physics.CheckSphere(checkSpherePosition, groundCheckRadius, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore);
        }

        // Update the current grounded state.
        isGrounded = rawGrounded;

        // Detect transitions for logging or potential event triggers (e.g., footstep sounds, landing impact).
        if (isGrounded == false && previouslyGrounded == true)
        {
            if (debugLogEnabled) Debug.Log("Player just left ground");
        }

        if (isGrounded == true && previouslyGrounded == false)
        {
            if (debugLogEnabled) Debug.Log($"Player just landed at Y position: {transform.position.y}");
        }

    }

    // Checks for an overhead obstruction when attempting to stand up from a crouch.
    // Returns The maximum height the character can currently stand at.
    private float GetMaxAllowedHeight()
    {
        RaycastHit hit;
        // Check a distance slightly greater than the standing height for robustness.
        float maxCheckDistance = standingHeight + 0.15f;

        // Raycast upwards from the base of the character (feet level).
        if (Physics.Raycast(transform.position, Vector3.up, out hit, maxCheckDistance, playerLayerMask, QueryTriggerInteraction.Ignore))
        {
            // Obstruction detected.
            // Calculate the maximum height by using the distance to the hit point.
            // The hit distance is the distance from the *base* of the character to the ceiling.
            float maxHeight = hit.distance - 0.1f; // A small offset (0.1f) is subtracted to prevent clipping into the obstruction.

            // Ensure the calculated max height is not less than the required crouching height.
            maxHeight = Mathf.Max(maxHeight, crouchingHeight);

            if (debugLogEnabled) Debug.Log($"Overhead obstruction detected. Max allowed height: {maxHeight:F2}");
            return maxHeight;
        }

        // No obstruction found.
        return standingHeight;
    }

    // Optional feature
    // Immediately moves the player to a new position, typically used for respawning or scene transitions.
    // The CharacterController must be disabled and re-enabled to allow teleportation.
    public void MovePlayerToSpawnPosition()
    {
        if (spawnPosition == null)
        {
            Debug.LogWarning("spawnPosition is null. Using default spawn position of 0.0.0");
            spawnPosition.position = Vector3.zero;
        }

        if (debugLogEnabled) Debug.Log("Moving player to Spawn Position");

        characterController.enabled = false;
        transform.position = spawnPosition.position;
        transform.rotation = spawnPosition.rotation;
        characterController.enabled = true;

        velocity = Vector3.zero; // Reset vertical velocity on teleport.
    }

    #endregion

    #region Input Methods

    // Receives continuous 2D movement input (WASD/Joystick).
    void SetMoveInput(Vector2 inputVector)
    {
        moveInput = new Vector2(inputVector.x, inputVector.y);
    }

    // Receives continuous 2D look input (Mouse/Joystick).
    void SetLookInput(Vector2 inputVector)
    {
        lookInput = new Vector2(inputVector.x, inputVector.y);
    }

    // Handles the jump action when input is received.
    void HandleJumpInput(InputAction.CallbackContext context)
    {
        // Early exit if jump is disabled or if player is currently crouching (prevent jumping while fully crouched).
        if (jumpEnabled == false || crouchInput == true) return;

        // Check for 'started' or 'performed' to capture the button press event reliably.
        if (context.started || context.performed)
        {
            if (debugLogEnabled) Debug.Log("Jump Input Triggered");

            // Only allow the jump request if grounded and outside of the cooldown period.
            if (isGrounded && jumpCooldownTimer <= 0f)
            {
                jumpRequested = true;

                // Immediately set a short cooldown to prevent multiple jump requests from a single input event
                // and to establish a basic input buffer.
                jumpCooldownTimer = 0.1f;
            }
        }
        // Note: No 'context.canceled' check needed as jump is a one-shot action. 
    }

    // Handles the crouch input based on hold-to-crouch or toggle-crouch setting.    
    void HandleCrouchInput(InputAction.CallbackContext context)
    {
        // Early exit if crouch is disabled.
        if (crouchEnabled == false) return;

        if (context.started) // Button pressed down
        {
            if (holdToCrouch == true)
            {
                crouchInput = true; // Start crouching while held
            }

            // Toggle mode: Invert the state on press.
            else if (holdToCrouch == false)
            {
                crouchInput = !crouchInput;
            }
        }

        else if (context.canceled) // Button released
        {
            // Only respond to release if in hold mode.
            if (holdToCrouch == true)
            {
                // This will only stand up if the player is NOT currently obstructed. The `HandleCrouchTransition` logic handles obstruction.
                crouchInput = false;
            }
            // In toggle mode, the state remains until the next press.
        }
    }


    // Handles the sprint input based on hold-to-sprint or toggle-sprint setting.
    void HandleSprintInput(InputAction.CallbackContext context)
    {
        // Early exit if sprint is disabled.
        if (sprintEnabled == false) return;

        if (context.started) // Button pressed down
        {
            if (holdToSprint == true)
            {
                sprintInput = true; // Start sprinting while held
            }

            // Toggle mode: Invert the state on press.
            else if (holdToSprint == false)
            {
                sprintInput = !sprintInput;
            }
        }

        else if (context.canceled) // Button released
        {
            // Only respond to release if in hold mode.
            if (holdToSprint == true)
            {
                sprintInput = false;
            }
            // In toggle mode, the state remains until the next press.
        }

    }

    #endregion



    // Subscribes to the InputManager's events when the script is enabled.
    // This pattern decouples input handling from the controller logic.
    void OnEnable()
    {
        // Ensure inputManager is not null before subscribing.
        if (inputManager == null) inputManager = GetComponent<InputManager>(); // Defensive check/re-initialization.
        if (inputManager == null)
        {
            Debug.LogError("InputManager component not found on PlayerController GameObject.");
            return; // Exit if still null.
        }


        inputManager.MoveInputEvent += SetMoveInput;
        inputManager.LookInputEvent += SetLookInput;

        inputManager.JumpInputEvent += HandleJumpInput;
        inputManager.CrouchInputEvent += HandleCrouchInput;
        inputManager.SprintInputEvent += HandleSprintInput;

    }

    // Unsubscribes from the InputManager's events when the script is destroyed.
    // Essential for preventing memory leaks (stale references) when GameObjects are destroyed.
    void OnDestroy()
    {
        // Ensure inputManager is not null before unsubscribing.
        if (inputManager == null) return;

        inputManager.MoveInputEvent -= SetMoveInput;
        inputManager.LookInputEvent -= SetLookInput;

        inputManager.JumpInputEvent -= HandleJumpInput;
        inputManager.CrouchInputEvent -= HandleCrouchInput;
        inputManager.SprintInputEvent -= HandleSprintInput;
    }

}