using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    
    [Header("Movement")]
    [SerializeField, Min(0)] private float movementSpeed = 8f;
    [SerializeField, Min(0)] private float rotationSpeed = 10f;
    [SerializeField, Min(0)] private float positionChangeSharpness = 9f;
    [SerializeField, Min(0)] private float minimumPositionChange = 0.01f;
    [SerializeField, Min(0)] private float minimumRotationChange = 0.5f;
    
    [Header("Surface Detection")]
    [SerializeField] private LayerMask surfaceLayer;
    [SerializeField, Min(0)] private float surfaceOffset = 0.2f;
    [SerializeField, Min(0)] private float probeRadius = 0.5f;
    [SerializeField, Range(30f, 360f)] private float arcAngle = 180f;
    [SerializeField, Min(2)] private int arcResolution = 12;
    [SerializeField] private Transform groundCheck;
    [SerializeField, Min(1)] private float groundCheckDistance = 10f;
    [SerializeField, Min(0)] private float groundCheckRadius = 0.3f;
    [SerializeField, Min(0)] private float groundCheckRecoverySpeed = 15f;
    
    [Header("Camera")]
    [SerializeField] private Transform playerCamera;

    private Rigidbody rb;
    private bool disableGravity = true; // <--- (rose) Disable this if you want to add jump, but always re-enable it the moment we touch ground
    // NOTE: If you add jump, make sure to always orient the player's transform.up to be Vector3.up in world space, so that the ground check works properly :)
    // Why? Because if we end up upside down or sideways, there's no guarantee we will find a surface. Maybe we can add a system that if you can't find ground for X seconds, we re-orient the player to stand up
    
    // The normal of the surface the bug is currently walking on. We assume UP, but if we choose to spawn the player sideways, you have to change this!
    private Vector3 currentSurfaceNormal = Vector3.up;
    
    private Vector3 moveInput;
    private Vector3 targetForward;
    private Vector3 lastMoveInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (disableGravity)
        {
            rb.useGravity = false; // change to true when jumping (if we add that), but always turn it back to false when on ground
        }

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        
        targetForward = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, currentSurfaceNormal).normalized;
    }

    private Vector3 GetMoveInput()
    {
        var kb = Keyboard.current; //<-------- Feel free to change input to use Unity's new input system for different controllers and whatnot

        if (kb == null)
        {
            return Vector2.zero;
        }

        float x = 0f, z = 0f;

        if (kb.wKey.isPressed) z += 1f;
        if (kb.sKey.isPressed) z -= 1f;
        if (kb.dKey.isPressed) x += 1f;
        if (kb.aKey.isPressed) x -= 1f;

        return new Vector3(x, 0f, z).normalized;
    }

    private void Update()
    {
        moveInput = GetMoveInput();
    }
    
    private void FixedUpdate()
    {
        bool isMoving = moveInput.sqrMagnitude > 0.001f;

        UpdateTargetFacingWithCamera(isMoving);
        ProceduralMovement(isMoving);
    }

    private void ProceduralMovement(bool isMoving)
    {
        // Check if we are trying to rotate while also going forward
        Quaternion headingRotation = Quaternion.LookRotation(targetForward, currentSurfaceNormal);
        float rotationLerp = 1f - Mathf.Exp(-rotationSpeed * Time.fixedDeltaTime);
        Quaternion newRotation = Quaternion.Slerp(rb.rotation, headingRotation, rotationLerp);
        Vector3 rotatedDirection = Vector3.ProjectOnPlane(newRotation * Vector3.forward, currentSurfaceNormal).normalized;
        
        // If we do have a rotation happening, then we use rotatedDirection over targetForward, otherwise, continue using targetForward
        if (rotatedDirection.sqrMagnitude < 0.001f)
        {
            rotatedDirection = targetForward;
        }

        // Calculate the target new position if we're moving (if not moving we still want to do ground checks and other potential safety checks)
        Vector3 targetPosition = rb.position;
        if (isMoving) 
        {
            targetPosition += rotatedDirection * movementSpeed * Time.fixedDeltaTime;
        }

        // ------ DETECT SURFACES ------
        RaycastHit arcHit = new RaycastHit();
        bool foundSurface = false;
        bool recoveredWithGroundCheck = false;

        if (isMoving)
        {
            // Check if there is a wall ahead
            foundSurface = PerformCurvedRaycast(targetPosition, rotatedDirection, currentSurfaceNormal, arcAngle, probeRadius, arcResolution, surfaceLayer, out arcHit);
            if (!foundSurface)
            {
                // Check if there is a surface ahead (such as in cases we are climbing a wall and reach the top)
                foundSurface = TryFindSurfaceAhead(rb.position, rotatedDirection, currentSurfaceNormal, probeRadius, surfaceLayer, out arcHit);
            }
        }

        // Ground check
        if (!foundSurface)
        {
            foundSurface = CheckForGround(out arcHit);
            recoveredWithGroundCheck = foundSurface;
        }

        // NO SURFACE FOUND! (stop movement, but something wrong is happening... TODO: Maybe should respawn player or something in this case)
        if (!foundSurface)
        {
            if (Quaternion.Angle(rb.rotation, newRotation) > minimumRotationChange)
            {
                rb.MoveRotation(newRotation);
            }

            return;
        }

        // For smoothe transition onto surfaces, SLERP that normal (use arcHit.normal directly if you don't want that smooth transition)
        float normalLerp = 1f - Mathf.Exp(-rotationSpeed * Time.fixedDeltaTime);
        Vector3 smoothedNormal = Vector3.Slerp(currentSurfaceNormal, arcHit.normal, normalLerp).normalized;
        Quaternion surfaceChange = Quaternion.FromToRotation(currentSurfaceNormal, smoothedNormal);
        Vector3 projectedForward = surfaceChange * targetForward;
        projectedForward = Vector3.ProjectOnPlane(projectedForward, smoothedNormal).normalized;
        if (projectedForward.sqrMagnitude > 0.001f)
        {
            targetForward = projectedForward;
        }

        currentSurfaceNormal = smoothedNormal;

        // Rotate
        Quaternion targetRotation = Quaternion.LookRotation(targetForward, currentSurfaceNormal);
        Quaternion finalRotation = Quaternion.Slerp(rb.rotation, targetRotation, rotationLerp);

        if (Quaternion.Angle(rb.rotation, finalRotation) > minimumRotationChange)
        {
            rb.MoveRotation(finalRotation);
        }

        // Move
        Vector3 movement = isMoving ? rotatedDirection * movementSpeed * Time.fixedDeltaTime : Vector3.zero;
        Vector3 surfacePosition = arcHit.point + smoothedNormal * surfaceOffset;
        Vector3 positionError = surfacePosition - (rb.position + movement);
        Vector3 surfaceCorrection = Vector3.Project(positionError, smoothedNormal);

        if (recoveredWithGroundCheck)
        {
            surfaceCorrection *= Mathf.Max(1f, groundCheckRecoverySpeed / positionChangeSharpness);
        }

        Vector3 finalPosition = rb.position + movement + surfaceCorrection;

        if (Vector3.Distance(rb.position, finalPosition) > minimumPositionChange)
        {
            rb.MovePosition(finalPosition);
        }
    }

    private void UpdateTargetFacingWithCamera(bool isMoving)
    {
        if (moveInput != lastMoveInput)
        {
            if (isMoving && playerCamera != null)
            {
                float dotProduct = Vector3.Dot(playerCamera.up, transform.up);

                Vector3 cameraForward;
                if (dotProduct >= 0)
                {
                    cameraForward = rb.position - playerCamera.position;
                }
                else
                {
                    cameraForward = playerCamera.position - rb.position;
                }

                cameraForward = Vector3.ProjectOnPlane(cameraForward, currentSurfaceNormal).normalized;
                Vector3 cameraRight = Vector3.Cross(currentSurfaceNormal, cameraForward).normalized;
                Vector3 desiredDirection = cameraForward * moveInput.z + cameraRight * moveInput.x;

                if (desiredDirection.sqrMagnitude > 0.001f)
                {
                    targetForward = desiredDirection.normalized;
                }
            }

            lastMoveInput = moveInput;
        }
    }
    
    private bool CheckForGround(out RaycastHit hit)
    {
        hit = new RaycastHit();

        if (groundCheck == null)
        {
            return false;
        }

        // Cast in the direction opposite to the current surface normal.
        Vector3 direction = -currentSurfaceNormal;
        Vector3 origin = groundCheck.position;

        bool foundSurface = Physics.SphereCast(origin, groundCheckRadius, direction, out hit, groundCheckDistance, surfaceLayer, QueryTriggerInteraction.Ignore);

        Debug.DrawRay(origin, direction * groundCheckDistance, foundSurface ? Color.green : Color.red);

        return foundSurface;
    }
    
    private bool TryFindSurfaceAhead(Vector3 position, Vector3 forward, Vector3 surfaceNormal, float radius, LayerMask layer, out RaycastHit hit)
    {
        // Look ahead and toward the direction the bug needs to climb.
        Vector3 origin = position + forward * radius + surfaceNormal * radius;

        // Cast back toward the current surface.
        Vector3 direction = -surfaceNormal;
        float distance = radius * 4f;

        bool foundSurface = Physics.SphereCast(origin, 0.1f, direction, out hit, distance, layer, QueryTriggerInteraction.Ignore);

        Debug.DrawRay(origin, direction * distance, foundSurface ? Color.cyan : Color.yellow, 0.1f );

        return foundSurface;
    }

    private bool PerformCurvedRaycast(Vector3 center, Vector3 forward, Vector3 up, float angle, 
        float radius, int resolution, LayerMask layer, out RaycastHit hit)
    {
        // Orient the arc toward the movement direction.
        Quaternion rotation = Quaternion.LookRotation(forward, up);
        
        rotation *= Quaternion.Euler(-angle/2, 0f, 0f);

        for (int i = 0; i < resolution; i++)
        {
            Vector3 A = center + rotation * Vector3.forward * radius;
            rotation *= Quaternion.Euler(angle/resolution, 0f, 0f);
            Vector3 B = center + rotation * Vector3.forward * radius;
            Vector3 AB = B - A;

            bool didHit = Physics.Raycast(A, AB.normalized, out hit, AB.magnitude * 1.001f, layer);
            
            // Visualize the raycast.
            Debug.DrawLine(A, didHit ? hit.point : B, didHit ? Color.green : Color.red);

            if (didHit)
            {
                return true;
            }
        }
        
        hit = new RaycastHit();
        return false;
    }
}
