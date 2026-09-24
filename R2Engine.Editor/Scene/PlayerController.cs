using System.Numerics;

namespace R2Engine.Editor.Scene;

public class PlayerController : Component
{
    public float MoveSpeed = 4.0f;
    public float Acceleration = 24.0f;
    public float Deceleration = 30.0f;
    public float AirControl = 0.35f;
    public float MaxSlopeAngle = 50.0f;
    public float StepHeight = 0.3f;
    public float GroundSnapDistance = 0.2f;
    public bool EnableKillFloor = true;
    public float KillFloorHeight = -100.0f;
    public float TurnSpeed = 120.0f;
    public float CameraPitchSpeed = 90.0f;
    public float CameraMinPitch = -75.0f;
    public float CameraMaxPitch = 60.0f;
    public bool InvertCameraPitch;
    public bool CameraCollision = true;
    public uint CameraCollisionMask = uint.MaxValue;
    public float CameraCollisionRadius = 0.2f;
    public float CameraCollisionClearance = 0.05f;
    public float CameraReturnSpeed = 6.0f;
    public float CameraTargetHeight = 1.0f;
    private Vector3 _cameraCorrection;
    private float _cameraDistance = -1;
    public bool InvertStrafe;
    public bool InvertTurn;
    public bool EnableSprinting = true;
    public bool EnableJumping = true;
    public float SprintMultiplier = 1.75f;
    public float JumpStrength = 5.0f;
    public float CoyoteTime = 0.12f;
    public float JumpBufferTime = 0.12f;
    public bool LaunchJumpFromAnimationEvent = true;
    public float JumpEventTimeout = 0.35f;
    public string MoveAction = "Move";
    public string TurnAction = "Turn";
    public string SprintAction = "Sprint";
    public string JumpAction = "Jump";
    public string JumpAnimationTrigger = "Jump";
    public string JumpLaunchEvent = "JumpLaunch";
    public string SpeedParameter = "Speed";
    public string MovingParameter = "Moving";
    public string MovingBackwardParameter = "MovingBackward";
    public string SprintingParameter = "Sprinting";
    public string GroundedParameter = "Grounded";
    public string VerticalSpeedParameter = "VerticalSpeed";
    public string IdleTimeParameter = "IdleTime";
    private Vector3 _horizontalVelocity;
    private float _idleTime;
    private float _timeSinceGrounded = float.MaxValue;
    private float _jumpBufferRemaining;
    private float _jumpEventWait;
    private bool _jumpLaunchPending;

    public void SuspendMovement()
    {
        _horizontalVelocity = Vector3.Zero;
        _jumpBufferRemaining = 0.0f;
        IRuntimeAnimator? animator = RuntimeAnimator();
        SetFloat(animator, SpeedParameter, 0.0f);
        SetBool(animator, MovingParameter, false);
        SetBool(animator, MovingBackwardParameter, false);
        SetBool(animator, SprintingParameter, false);
    }

    public void Update(float deltaTime, CollisionSystem collisionSystem, Scene scene)
    {
        if (EnableKillFloor && GameObject.Transform.WorldPosition.Y < KillFloorHeight)
        {
            SceneManager.RestartScene();
            return;
        }
        IRuntimeAnimator? runtimeAnimator = RuntimeAnimator();
        bool movementLocked = runtimeAnimator?.MovementLocked ?? false;
        float turn = RuntimeInput.GetAxis(TurnAction);

        if (InvertTurn)
            turn = -turn;

        const float degreesToRadians = MathF.PI / 180.0f;
        Transform? camera = scene.GameObjects
            .FirstOrDefault(obj => obj.IsActiveInHierarchy && obj.GetComponent<Camera>()?.IsPrimary == true)?.Transform;
        Vector3 playerPosition = GameObject.Transform.WorldPosition;
        Vector3 cameraPosition = (camera?.WorldPosition ?? Vector3.Zero) + _cameraCorrection;
        Vector3 cameraRotation = camera?.WorldRotation ?? Vector3.Zero;
        Vector3 cameraScale = camera?.WorldScale ?? Vector3.One;
        if (camera != null)
        {
            float orbit = turn * TurnSpeed * deltaTime;
            cameraPosition = playerPosition + Vector3.Transform(cameraPosition - playerPosition,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, orbit * degreesToRadians));
            cameraRotation.Y += orbit;
            float pitchInput = RuntimeInput.GetCameraPitch();
            if (InvertCameraPitch) pitchInput = -pitchInput;
            if (MathF.Abs(pitchInput) > 0.0001f)
            {
                float minPitch = Math.Clamp(CameraMinPitch, -85, 85);
                float maxPitch = Math.Clamp(CameraMaxPitch, minPitch, 85);
                float pitch = Math.Clamp(cameraRotation.X + pitchInput * CameraPitchSpeed * deltaTime,
                    minPitch, maxPitch);
                float yaw = cameraRotation.Y * degreesToRadians;
                Vector3 axis = new(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
                cameraPosition = playerPosition + Vector3.Transform(cameraPosition - playerPosition,
                    Quaternion.CreateFromAxisAngle(axis, (pitch - cameraRotation.X) * degreesToRadians));
                cameraRotation.X = pitch;
            }
        }
        else if (!movementLocked)
            GameObject.Transform.Rotation.Y += turn * TurnSpeed * deltaTime;

        Vector3 worldRotation = GameObject.Transform.WorldRotation;
        Quaternion orientation = Quaternion.CreateFromYawPitchRoll(
            worldRotation.Y * degreesToRadians,
            worldRotation.X * degreesToRadians,
            worldRotation.Z * degreesToRadians);

        // R2Engine uses the local blue +Z axis as forward and
        // the local red +X axis as right.
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, orientation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, orientation);
        if (camera != null)
        {
            // Cameras look down -Z; movement stays on the horizontal plane.
            float yaw = cameraRotation.Y * degreesToRadians;
            forward = new Vector3(-MathF.Sin(yaw), 0, -MathF.Cos(yaw));
            right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
        }

        Vector2 moveInput = RuntimeInput.GetVector2(MoveAction);
        float inputMagnitude = Math.Clamp(moveInput.Length(), 0.0f, 1.0f);
        Vector3 movement = Vector3.Zero;
        movement += forward * moveInput.Y;
        float strafe = moveInput.X;
        if (InvertStrafe) strafe = -strafe;
        movement += right * strafe;
        if (!movementLocked && camera != null && movement.LengthSquared() > 0.000001f)
        {
            float yaw = MathF.Atan2(movement.X, movement.Z) / degreesToRadians;
            GameObject.Transform.Rotation.Y = yaw - (GameObject.Parent?.Transform.WorldRotation.Y ?? 0);
        }

        Rigidbody? rigidbody = GameObject.GetComponent<Rigidbody>();
        bool grounded = rigidbody?.IsGrounded ?? true;
        _timeSinceGrounded = grounded ? 0.0f : _timeSinceGrounded + deltaTime;
        _idleTime = grounded && inputMagnitude <= 0.0001f ? _idleTime + deltaTime : 0.0f;

        bool sprinting = EnableSprinting && RuntimeInput.IsActionDown(SprintAction) && inputMagnitude > 0.0001f;
        Vector3 desiredVelocity = !movementLocked && movement.LengthSquared() > 0.000001f
            ? Vector3.Normalize(movement) * MoveSpeed * inputMagnitude * (sprinting ? SprintMultiplier : 1.0f)
            : Vector3.Zero;
        float response = desiredVelocity.LengthSquared() > 0.000001f ? Acceleration : Deceleration;
        if (!grounded)
            response *= Math.Clamp(AirControl, 0.0f, 1.0f);
        _horizontalVelocity = movementLocked
            ? Vector3.Zero
            : MoveTowards(_horizontalVelocity, desiredVelocity, response * deltaTime);
        if (_horizontalVelocity.LengthSquared() > 0.000001f)
            collisionSystem.MoveCharacter(
                GameObject,
                _horizontalVelocity * deltaTime,
                scene,
                grounded,
                StepHeight,
                MaxSlopeAngle,
                GroundSnapDistance);

        if (camera != null)
        {
            Vector3 desired = cameraPosition + GameObject.Transform.WorldPosition - playerPosition;
            Vector3 resolved = desired;
            if (CameraCollision)
            {
                Vector3 pivot = GameObject.Transform.WorldPosition + Vector3.UnitY * CameraTargetHeight;
                Vector3 offset = desired - pivot;
                float distance = offset.Length();
                float safe = collisionSystem.CameraSweep(scene, GameObject, camera.GameObject,
                    pivot, desired, Math.Max(0.01f, CameraCollisionRadius) + Math.Max(0, CameraCollisionClearance),
                    CameraCollisionMask);
                float retraction = distance - safe;
                _cameraDistance = Math.Max(retraction, Math.Max(0, _cameraDistance) - Math.Max(0, CameraReturnSpeed) * deltaTime);
                if (distance > 0.00001f) resolved = pivot + offset * (Math.Max(0, distance - _cameraDistance) / distance);
            }
            else _cameraDistance = -1;
            _cameraCorrection = desired - resolved;
            // Counter the child camera's inherited yaw, but keep its hierarchy
            // so gravity, save/load and scene transitions still follow Mimi.
            camera.SetWorldTransform(resolved,
                cameraRotation, cameraScale);
        }

        if (EnableJumping && RuntimeInput.WasActionPressed(JumpAction))
            _jumpBufferRemaining = Math.Max(0.0f, JumpBufferTime);
        else
            _jumpBufferRemaining = Math.Max(0.0f, _jumpBufferRemaining - deltaTime);

        bool canJump = rigidbody != null && _timeSinceGrounded <= Math.Max(0.0f, CoyoteTime);
        if (!_jumpLaunchPending && _jumpBufferRemaining > 0.0f && canJump)
        {
            _jumpBufferRemaining = 0.0f;
            _timeSinceGrounded = float.MaxValue;
            IRuntimeAnimator? jumpAnimator = RuntimeAnimator();
            if (jumpAnimator != null && !string.IsNullOrWhiteSpace(JumpAnimationTrigger))
                jumpAnimator.SetTrigger(JumpAnimationTrigger);
            if (LaunchJumpFromAnimationEvent && !string.IsNullOrWhiteSpace(JumpLaunchEvent))
            {
                _jumpLaunchPending = true;
                _jumpEventWait = 0.0f;
            }
            else
                LaunchJump();
        }

        if (_jumpLaunchPending)
        {
            _jumpEventWait += deltaTime;
            if (_jumpEventWait >= Math.Max(0.0f, JumpEventTimeout))
                LaunchJump();
        }

        IRuntimeAnimator? animator = runtimeAnimator;
        SetFloat(animator, SpeedParameter, MoveSpeed <= 0.0001f ? 0.0f : _horizontalVelocity.Length() / MoveSpeed);
        SetFloat(animator, VerticalSpeedParameter, rigidbody?.Velocity.Y ?? 0.0f);
        SetFloat(animator, IdleTimeParameter, _idleTime);
        SetBool(animator, MovingParameter, inputMagnitude > 0.0001f);
        SetBool(animator, MovingBackwardParameter, camera == null && moveInput.Y < -0.0001f);
        SetBool(animator, SprintingParameter, sprinting);
        SetBool(animator, GroundedParameter, grounded);
    }

    internal void NotifyAnimationEvent(string eventName)
    {
        if (!_jumpLaunchPending ||
            !string.Equals(eventName.Trim(), JumpLaunchEvent.Trim(), StringComparison.OrdinalIgnoreCase))
            return;

        LaunchJump();
    }

    private void LaunchJump()
    {
        Rigidbody? rigidbody = GameObject.GetComponent<Rigidbody>();
        if (rigidbody != null)
            rigidbody.Velocity.Y = JumpStrength;
        _jumpLaunchPending = false;
        _jumpEventWait = 0.0f;
    }

    private static Vector3 MoveTowards(Vector3 current, Vector3 target, float maximumDelta)
    {
        Vector3 difference = target - current;
        float distance = difference.Length();
        return distance <= maximumDelta || distance <= 0.000001f
            ? target
            : current + difference / distance * maximumDelta;
    }

    private IRuntimeAnimator? RuntimeAnimator() =>
        GameObject.Components.OfType<IRuntimeAnimator>().FirstOrDefault();

    private static void SetFloat(IRuntimeAnimator? animator, string name, float value)
    {
        if (animator != null && !string.IsNullOrWhiteSpace(name)) animator.SetFloat(name, value);
    }

    private static void SetBool(IRuntimeAnimator? animator, string name, bool value)
    {
        if (animator != null && !string.IsNullOrWhiteSpace(name)) animator.SetBool(name, value);
    }
}
