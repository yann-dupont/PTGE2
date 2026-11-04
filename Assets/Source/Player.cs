using UnityEngine;

public class Player : MonoBehaviour
{
    public LayerMask groundLayer;
    public float speed;
    
    private void Update()
    {
        float arcAngle = 270;
        float arcRadius = speed * Time.deltaTime;
        int arcResolution = 6;

        if (PerformCurvedRaycast(transform.position, transform.rotation, arcAngle, arcRadius, arcResolution,
                groundLayer, out RaycastHit hit))
        {
            transform.position = hit.point;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * transform.rotation;
        }
    }

    private bool PerformCurvedRaycast(Vector3 center, Quaternion rotation, float angle, 
        float radius, int resolution, LayerMask layer, out RaycastHit hit)
    {
        rotation = Quaternion.Euler(-angle/2, 0f, 0f);

        for (int i = 0; i <resolution; i++)
        {
            Vector3 A = center + rotation * Vector3.forward * radius;
            rotation *= Quaternion.Euler(angle/resolution, 0f, 0f);
            Vector3 B = center + rotation * Vector3.forward * radius;
            Vector3 AB = B - A;

            if (Physics.Raycast(A, AB, out hit, AB.magnitude * 1.001f, layer))
            {
                return true;
            }
        }
        
        hit = new RaycastHit();
        return false;
    }
}
