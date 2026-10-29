using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person controller: camera-relative walk/run, jump, dodge roll.
/// Built on CharacterController + a tiny two-state machine (Locomotion / Dodging).
/// Animator parameters are optional: assign an Animator later and everything just works.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // ---- State machine ---------------------------------------------------
    // Only two states for now. Combat later adds Attacking, Stunned, etc.
    private enum State { Locomotion, Dodging }
    private State state = State.Locomotion;

    // ---- Input (assigned in the Inspector from your Input Actions asset) --
    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;   // Vector2
    [SerializeField] private InputActionReference sprintAction; // Button (held)
    [SerializeField] private InputActionReference jumpAction;   // Button
    [SerializeField] private InputActionReference dodgeAction;  // Button

    // ---- References -------------------------------------------------------
    [Header("References")]
    [SerializeField] private Transform cameraTransform; // the Main Camera (Cinemachine drives it)
    [SerializeField] private Animator animator;         // optional, leave empty for graybox
    [SerializeField] private Transform visuals;

    // ---- Tuning -----------------------------------------------------------
    [Header("Locomotion")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float acceleration = 25f;      // units/sec^2 toward target speed
    [SerializeField] private float rotationSmoothTime = 0.08f;

    [Header("Jump & Gravity")]
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -25f;          // stronger than real life = snappier jump
    [SerializeField] private float groundedStickForce = -2f; // keeps the controller glued to slopes
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Dodge")]
    [SerializeField] private float dodgeSpeed = 9f;
    [SerializeField] private float dodgeDuration = 0.4f;
    [SerializeField] private float dodgeCooldown = 0.6f;
    [SerializeField]
    private AnimationCurve dodgeSpeedCurve =
        AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f);         // fast start, slows toward the end

    // ---- Animator parameter names (hashed once, cheaper than strings) ----
    // Create parameters with these exact names in your Animator Controller later.
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");                 // float 0..1 (walk .5, run 1)
    private static readonly int AnimGrounded = Animator.StringToHash("Grounded");           // bool
    private static readonly int AnimVerticalVel = Animator.StringToHash("VerticalVelocity"); // float
    private static readonly int AnimJump = Animator.StringToHash("Jump");                   // trigger
    private static readonly int AnimDodge = Animator.StringToHash("Dodge");                 // trigger

    // ---- Runtime fields ---------------------------------------------------
    private CharacterController controller;

    private Vector3 moveDirection;      // last direction we were heading (world space, flat)
    private float currentSpeed;         // smoothed horizontal speed
    private float verticalVelocity;     // y velocity, handled separately from horizontal
    private float rotationVelocity;     // scratch value required by SmoothDampAngle
    private bool isGrounded;

    private Vector3 dodgeDirection;
    private float dodgeTimer;
    private float dodgeCooldownTimer;

    private float coyoteTimer;
    private float jumpBufferTimer;

    /// <summary>
    /// Mini games / dialogue / cutscenes flip this off. The player keeps obeying gravity
    /// but ignores input. (Mini games also pause with Time.timeScale = 0, this is the belt-and-braces.)
    /// </summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>Combat will read this later: true while the roll should make the player untouchable.</summary>
    public bool IsDodging => state == State.Dodging;

    // ---- Unity lifecycle --------------------------------------------------
    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform; // sensible fallback
    }

    // Input Actions must be enabled to produce values. Doing it here ties it to the component's lifetime.
    private void OnEnable()
    {
        Enable(moveAction); Enable(sprintAction); Enable(jumpAction); Enable(dodgeAction);
    }

    private void OnDisable()
    {
        Disable(moveAction); Disable(sprintAction); Disable(jumpAction); Disable(dodgeAction);
    }

    private static void Enable(InputActionReference r) { if (r != null) r.action.Enable(); }
    private static void Disable(InputActionReference r) { if (r != null) r.action.Disable(); }

    private void Update()
    {
        dodgeCooldownTimer -= Time.deltaTime;

        switch (state)
        {
            case State.Locomotion: UpdateLocomotion(); break;
            case State.Dodging: UpdateDodge(); break;
        }
    }

    // ---- Locomotion state -------------------------------------------------
    private void UpdateLocomotion()
    {
        // 1) Read input (zeroed out when input is locked).
        Vector2 input = InputEnabled && moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
        bool wantsSprint = InputEnabled && sprintAction != null && sprintAction.action.IsPressed();

        // 2) Turn 2D stick/WASD input into a world direction relative to the camera.
        //    We flatten the camera's forward/right onto the ground so looking up/down doesn't slow us.
        Vector3 camForward = cameraTransform.forward; camForward.y = 0f; camForward.Normalize();
        Vector3 camRight = cameraTransform.right; camRight.y = 0f; camRight.Normalize();
        Vector3 desiredDir = camForward * input.y + camRight * input.x;
        float inputMagnitude = Mathf.Clamp01(desiredDir.magnitude); // diagonals don't exceed 1; sticks give analog speed

        bool hasInput = inputMagnitude > 0.01f;
        if (hasInput) moveDirection = desiredDir.normalized;

        // 3) Smooth speed. Accelerate toward the target, decelerate to 0 when there's no input.
        float targetSpeed = (wantsSprint ? runSpeed : walkSpeed) * inputMagnitude;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);

        // 4) Rotate the character toward the movement direction (only while the player is steering).
        if (hasInput)
        {
            float targetYaw = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
            float yaw = Mathf.SmoothDampAngle(visuals.eulerAngles.y, targetYaw, ref rotationVelocity, rotationSmoothTime);
            visuals.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // 5) Jump. Physics: v = sqrt(h * -2 * g) gives exactly 'jumpHeight' at apex.
        // Coyote: refill while grounded, drain while airborne.
        if (isGrounded) coyoteTimer = coyoteTime;
        else coyoteTimer -= Time.deltaTime;

        // Buffer: remember the press for a short window.
        if (InputEnabled && jumpAction != null && jumpAction.action.WasPressedThisFrame())
            jumpBufferTimer = jumpBufferTime;
        else
            jumpBufferTimer -= Time.deltaTime;

        // Jump only when a recent press meets a recent ground contact.
        if (jumpBufferTimer > 0f && coyoteTimer > 0f)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpBufferTimer = 0f; // consume the press
            coyoteTimer = 0f;     // consume the grace window, so no double jump
            Trigger(AnimJump);
        }

        // 6) Dodge. Needs ground contact and an expired cooldown.
        if (InputEnabled && isGrounded && dodgeCooldownTimer <= 0f
            && dodgeAction != null && dodgeAction.action.WasPressedThisFrame())
        {
            StartDodge(hasInput ? moveDirection : transform.forward);
            return; // dodge state takes over next frame; skip applying movement this frame
        }

        // 7) Gravity + move.
        ApplyGravity();
        Vector3 velocity = moveDirection * currentSpeed + Vector3.up * verticalVelocity;
        MoveController(velocity);

        UpdateAnimator(runSpeed > 0f ? currentSpeed / runSpeed : 0f);
        Debug.DrawRay(transform.position + Vector3.up, camForward * 3f, Color.blue);
    }

    // ---- Dodge state ------------------------------------------------------
    private void StartDodge(Vector3 direction)
    {
        state = State.Dodging;
        dodgeDirection = direction.normalized;
        dodgeTimer = 0f;
        transform.rotation = Quaternion.LookRotation(dodgeDirection); // face the roll direction instantly
        Trigger(AnimDodge);
    }

    private void UpdateDodge()
    {
        dodgeTimer += Time.deltaTime;
        float t = Mathf.Clamp01(dodgeTimer / dodgeDuration);

        // The curve gives a punchy start and a soft end instead of a constant-speed slide.
        float speed = dodgeSpeed * dodgeSpeedCurve.Evaluate(t);

        ApplyGravity();
        MoveController(dodgeDirection * speed + Vector3.up * verticalVelocity);
        UpdateAnimator(1f);

        if (dodgeTimer >= dodgeDuration)
        {
            state = State.Locomotion;
            dodgeCooldownTimer = dodgeCooldown;
            moveDirection = dodgeDirection;
            currentSpeed = walkSpeed; // avoids a speed pop when control returns
        }
    }

    // ---- Shared helpers ---------------------------------------------------
    private void ApplyGravity()
    {
        if (isGrounded && verticalVelocity < 0f)
            verticalVelocity = groundedStickForce; // small constant push down keeps isGrounded reliable
        else
            verticalVelocity += gravity * Time.deltaTime;
    }

    private void MoveController(Vector3 velocity)
    {
        controller.Move(velocity * Time.deltaTime);
        isGrounded = controller.isGrounded; // only valid right after Move()
    }

    // ---- Animator hooks (all no-ops when no Animator is assigned) --------
    private void UpdateAnimator(float normalizedSpeed)
    {
        if (animator == null) return;
        animator.SetFloat(AnimSpeed, normalizedSpeed, 0.1f, Time.deltaTime); // damped to avoid blend jitter
        animator.SetBool(AnimGrounded, isGrounded);
        animator.SetFloat(AnimVerticalVel, verticalVelocity);
    }

    private void Trigger(int hash)
    {
        if (animator != null) animator.SetTrigger(hash);
    }
}