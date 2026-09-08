using UnityEngine;

public class ExperimentLayoutGizmo : MonoBehaviour
{
    [SerializeField] Vector3 areaCenter = new Vector3(0f, 0.03f, 2.5f);
    [SerializeField] Vector2 areaSize = new Vector2(5.5f, 5f);

    [SerializeField] Vector3 startCenter = new Vector3(0f, 0.04f, 0f);
    [SerializeField] float startMarkerSideLength = 1.2f;
    [SerializeField] float startYawDegrees = -45f;

    [SerializeField] Vector3 oSpaceCenter = new Vector3(0f, 0.05f, 4f);
    [SerializeField] float oSpaceDiameter = 1.5f;
    [SerializeField] int oSpaceSegments = 64;

    [SerializeField] Color areaColor = new Color(0f, 0.9f, 1f, 0.8f);
    [SerializeField] Color startMarkerColor = new Color(1f, 0.85f, 0f, 0.95f);
    [SerializeField] Color oSpaceColor = new Color(1f, 0.35f, 0.85f, 0.95f);

    void OnDrawGizmos()
    {
        DrawExperimentArea();
        //DrawOSpace();
        //DrawStartMarker();
    }

    void DrawExperimentArea()
    {
        Gizmos.color = areaColor;

        float halfWidth = areaSize.x * 0.5f;
        float halfLength = areaSize.y * 0.5f;

        Vector3 p0 = areaCenter + new Vector3(-halfWidth, 0f, -halfLength);
        Vector3 p1 = areaCenter + new Vector3(halfWidth, 0f, -halfLength);
        Vector3 p2 = areaCenter + new Vector3(halfWidth, 0f, halfLength);
        Vector3 p3 = areaCenter + new Vector3(-halfWidth, 0f, halfLength);

        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);
    }

    void DrawStartMarker()
    {
        Gizmos.color = startMarkerColor;

        float triangleHeight = startMarkerSideLength * 0.8660254f;
        float halfWidth = startMarkerSideLength * 0.5f;
        Quaternion rotation = Quaternion.Euler(0f, startYawDegrees, 0f);

        Vector3 tip = startCenter + rotation * new Vector3(0f, 0f, triangleHeight * 2f / 3f);
        Vector3 rightBack = startCenter + rotation * new Vector3(halfWidth, 0f, -triangleHeight / 3f);
        Vector3 leftBack = startCenter + rotation * new Vector3(-halfWidth, 0f, -triangleHeight / 3f);

        Gizmos.DrawLine(tip, rightBack);
        Gizmos.DrawLine(rightBack, leftBack);
        Gizmos.DrawLine(leftBack, tip);
    }

    void DrawOSpace()
    {
        Gizmos.color = oSpaceColor;

        float radius = oSpaceDiameter * 0.5f;
        int segments = Mathf.Max(12, oSpaceSegments);
        Vector3 previous = oSpaceCenter + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 current = oSpaceCenter + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }
    }
}
