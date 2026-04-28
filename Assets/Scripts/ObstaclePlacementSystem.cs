using UnityEngine;

public class ObstaclePlacementSystem : MonoBehaviour
{
    [Header("References")]
    public Camera sceneCamera;
    public GameObject obstaclePrefab;

    [Header("Placement")]
    public LayerMask groundLayer;
    public float rotationSpeed = 90f;

    private GameObject selectedObstacle;
    private Renderer lastRenderer;

    void Update()
    {
        if (sceneCamera == null || obstaclePrefab == null) return;

        HandlePlacement();
        HandleSelection();
        HandleMovement();
        HandleRotation();
        HandleDelete();
    }

    void HandlePlacement()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            Debug.Log("P pressed");

            Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
            {
                Debug.Log("Ground hit at: " + hit.point);

                GameObject newObstacle = Instantiate(obstaclePrefab, hit.point, Quaternion.identity);

                float yOffset = CalculateYOffset(newObstacle);
                newObstacle.transform.position = hit.point + new Vector3(0f, yOffset, 0f);

                selectedObstacle = newObstacle;
            }
            else
            {
                Debug.Log("No ground hit");
            }
        }
    }

    void HandleSelection()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Transform root = hit.collider.transform.root;

                if (root.CompareTag("Obstacle"))
                {
                    if (lastRenderer != null)
                        lastRenderer.material.color = Color.white;

                    selectedObstacle = root.gameObject;

                    lastRenderer = selectedObstacle.GetComponentInChildren<Renderer>();
                    if (lastRenderer != null)
                        lastRenderer.material.color = Color.green;
                }
            }
        }
    }

    void HandleMovement()
    {
        if (selectedObstacle == null) return;

        if (Input.GetMouseButton(1))
        {
            Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
            {
                float yOffset = CalculateYOffset(selectedObstacle);
                selectedObstacle.transform.position = hit.point + new Vector3(0f, yOffset, 0f);
            }
        }
    }

    void HandleRotation()
    {
        if (selectedObstacle == null) return;

        if (Input.GetKey(KeyCode.Z))
        {
            selectedObstacle.transform.Rotate(Vector3.up, -rotationSpeed * Time.deltaTime);
        }

        if (Input.GetKey(KeyCode.X))
        {
            selectedObstacle.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        }
    }

    void HandleDelete()
    {
        if (selectedObstacle == null) return;

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            Destroy(selectedObstacle);
            selectedObstacle = null;
            lastRenderer = null;
        }
    }

    float CalculateYOffset(GameObject obj)
    {
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();

        if (colliders.Length == 0)
            return 0f;

        Bounds combinedBounds = colliders[0].bounds;

        for (int i = 1; i < colliders.Length; i++)
        {
            combinedBounds.Encapsulate(colliders[i].bounds);
        }

        float lowestPoint = combinedBounds.min.y;
        float objectPivotY = obj.transform.position.y;

        return objectPivotY - lowestPoint;
    }
}