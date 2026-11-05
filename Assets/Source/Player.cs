using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    
    [Header("Movement")]
    [SerializeField] private float speed = 8f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float positionSharpness = 20f;
    
    [Header("Surface Detection")]
    [SerializeField] private LayerMask surfaceLayer;
    [SerializeField] private float surfaceOffset = 0.2f;
    [SerializeField] private float probeRadius = 0.5f;
    [SerializeField, Range(30f, 360f)] private float arcAngle = 180f;
    [SerializeField, Min(2)] private int arcResolution = 12;
    
    [Header("Physics")]
    [SerializeField] private bool disableGravity = true;
    
    private Rigidbody rb;
    
    // The normal of the surface the bug is currently walking on.
    private Vector3 currentSurfaceNormal = Vector3.up;
    
    private Vector3 moveInput;
    private Vector3 lastMoveInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        
        if (disableGravity)
            rb.useGravity = false;

        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private Vector3 GetMoveInput()
    {
        var kb = Keyboard.current;

        if (kb == null)
            return Vector2.zero;

        float x = 0f;
        float z = 0f;

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
        if (moveInput.sqrMagnitude < 0.001f)
        {
            lastMoveInput = moveInput;
            return;
        }
        
        // Calculate forward and right relative to the current surface.
        Vector3 forward = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, currentSurfaceNormal).normalized;
        
        // Fallback if forward is nearly parallel to the surface normal.
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.Cross( rb.rotation * Vector3.right, currentSurfaceNormal).normalized;
        }
        
        Vector3 right = Vector3.Cross(currentSurfaceNormal, forward).normalized;

        if (moveInput != lastMoveInput)
        {
            // Calculate desired facing direction from input.
            Vector3 desiredDirection = forward * moveInput.z + right * moveInput.x;

            desiredDirection.Normalize();
            
            rb.MoveRotation(Quaternion.LookRotation(desiredDirection, currentSurfaceNormal));
        }
        
        lastMoveInput = moveInput;

        if (moveInput.x > 0.001f || moveInput.x < -0.001f)
            return;

        // Calculate movement along the surface.
        Vector3 moveDirection = forward * moveInput.z + right * moveInput.x;
        moveDirection.Normalize();
        
        // Predict the next position.
        Vector3 targetPosition = rb.position + moveDirection * speed * Time.fixedDeltaTime;
        
        // First, try to find a surface ahead.
        RaycastHit arcHit;
        bool foundSurface = PerformCurvedRaycast(targetPosition, moveDirection, currentSurfaceNormal, arcAngle, probeRadius, arcResolution, surfaceLayer, out arcHit);
        
        // If the predictive probe doesn't find anything,
        // fall back to the curved raycast.
        if (!foundSurface)
        {
            foundSurface = TryFindSurfaceAhead(rb.position, moveDirection, currentSurfaceNormal, probeRadius, surfaceLayer, out arcHit);
        }
        else
        {
            Debug.Log(
                $"Predictive hit: {arcHit.collider.name}, " +
                $"Normal: {arcHit.normal}"
            );
        
        }

        if (!foundSurface)
            return;
        
        // Use the detected surface directly.
        Vector3 finalPosition = arcHit.point + arcHit.normal * surfaceOffset;

        // Smoothly align the body with the new surface.
        Quaternion normalRotation = Quaternion.FromToRotation(
            rb.rotation * Vector3.up,
            arcHit.normal
        );

        Quaternion targetRotation = normalRotation * rb.rotation;

        float rotationLerp = 1f - Mathf.Exp(-rotationSpeed * Time.fixedDeltaTime);
        float positionLerp = 1f - Mathf.Exp(-positionSharpness * Time.fixedDeltaTime);

        // Move and rotate the Rigidbody.
        rb.MovePosition(Vector3.Lerp(rb.position, finalPosition, positionLerp));
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationLerp));

        // Remember the latest surface normal.
        currentSurfaceNormal = arcHit.normal;
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
            Debug.DrawLine(
                A,
                didHit ? hit.point : B,
                didHit ? Color.green : Color.red
            );

            if (didHit)
            {
                return true;
            }
        }
        
        hit = new RaycastHit();
        return false;
    }
}
