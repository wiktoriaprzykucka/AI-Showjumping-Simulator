// using UnityEngine;
// version with jump point

// public class HorseController : MonoBehaviour
// {
//     [Header("References")]
//     public Transform startPoint;
//     public Transform targetPoint;
//     // public Transform jumpPoint;

//     [Header("Movement Settings")]
//     public float moveSpeed = 5f;
//     public bool isMoving = false;

//     [Header("Jump Settings")]
//     public float jumpHeight = 2f;
//     public float jumpDistance = 4f;
//     public float jumpTriggerDistance = 3f;

//     private Vector3 moveDirection;
//     private bool isJumping = false;
//     private bool hasJumped = false;

//     private Vector3 jumpStartPos;
//     private Vector3 jumpEndPos;
//     private float jumpProgress = 0f;
//     private float jumpLength = 0f;

//     void Start()
//     {
//         ResetHorse();
//     }

//     void Update()
//     {
//         HandleInput();

//         if (isJumping)
//         {
//             HandleJump();
//         }
//         else if (isMoving)
//         {
//             MoveHorse();

//             if (jumpPoint != null && !hasJumped)
//             {
//                 float distanceToJumpPoint = Vector3.Distance(
//                     new Vector3(transform.position.x, 0f, transform.position.z),
//                     new Vector3(jumpPoint.position.x, 0f, jumpPoint.position.z)
//                 );

//                 Debug.Log("Distance to jump point: " + distanceToJumpPoint);

//                 if (distanceToJumpPoint <= jumpTriggerDistance)
//                 {
//                     Debug.Log("Jump condition met");
//                     StartJump();
//                 }
//             }
//             else if (jumpPoint == null)
//             {
//                 Debug.LogWarning("JumpPoint is not assigned!");
//             }
//         }
//     }

//     void HandleInput()
//     {
//         if (Input.GetKeyDown(KeyCode.Space))
//         {
//             StartMovement();
//         }

//         if (Input.GetKeyDown(KeyCode.R))
//         {
//             ResetHorse();
//         }
//     }

//     void StartMovement()
//     {
//         if (startPoint == null || targetPoint == null) return;

//         moveDirection = (targetPoint.position - transform.position).normalized;
//         moveDirection.y = 0f;

//         if (moveDirection != Vector3.zero)
//         {
//             transform.forward = moveDirection;
//             isMoving = true;
//         }
//     }

//     void MoveHorse()
//     {
//         if (targetPoint == null) return;

//         Vector3 targetPos = new Vector3(
//             targetPoint.position.x,
//             transform.position.y,
//             targetPoint.position.z
//         );

//         transform.position = Vector3.MoveTowards(
//             transform.position,
//             targetPos,
//             moveSpeed * Time.deltaTime
//         );

//         Vector3 lookDir = (targetPos - transform.position).normalized;
//         lookDir.y = 0f;

//         if (lookDir != Vector3.zero)
//         {
//             transform.forward = lookDir;
//         }

//         float distanceToTarget = Vector3.Distance(transform.position, targetPos);

//         if (distanceToTarget < 0.05f)
//         {
//             isMoving = false;
//         }
//     }

//     void StartJump()
//     {
//         if (isJumping) return;

//         isJumping = true;
//         hasJumped = true;
//         jumpProgress = 0f;

//         jumpStartPos = transform.position;
//         jumpEndPos = jumpStartPos + transform.forward * jumpDistance;
//         jumpLength = Vector3.Distance(jumpStartPos, jumpEndPos);

//         Debug.Log("Jump started");
//     }

//     void HandleJump()
//     {
//         if (jumpLength <= 0f)
//         {
//             isJumping = false;
//             return;
//         }

//         jumpProgress += (moveSpeed / jumpLength) * Time.deltaTime;

//         Vector3 flatPos = Vector3.Lerp(jumpStartPos, jumpEndPos, jumpProgress);
//         float arc = 4f * jumpHeight * jumpProgress * (1f - jumpProgress);

//         transform.position = new Vector3(
//             flatPos.x,
//             jumpStartPos.y + arc,
//             flatPos.z
//         );

//         if (jumpProgress >= 1f)
//         {
//             transform.position = new Vector3(
//                 jumpEndPos.x,
//                 jumpStartPos.y,
//                 jumpEndPos.z
//             );

//             isJumping = false;
//             isMoving = true; // continue moving after landing
//             Debug.Log("Jump finished");
//         }
//     }

//     public void ResetHorse()
//     {
//         if (startPoint == null) return;

//         transform.position = startPoint.position;
//         isMoving = false;
//         isJumping = false;
//         hasJumped = false;
//         jumpProgress = 0f;

//         if (targetPoint != null)
//         {
//             Vector3 lookDir = (targetPoint.position - transform.position).normalized;
//             lookDir.y = 0f;

//             if (lookDir != Vector3.zero)
//             {
//                 transform.forward = lookDir;
//             }
//         }
//     }
// }


using UnityEngine;

public class HorseController : MonoBehaviour
{
    [Header("References")]
    public Transform startPoint;
    public Transform targetPoint;
    public ObstacleInfo obstacleInfo;

    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public bool isMoving = false;

    [Header("Jump Settings")]
    public float jumpHeight = 2f;
    public float jumpDistance = 4f;

    [Header("Approach Rules")]
    public float maxApproachAngle = 35f;
    public float maxCenterOffset = 1.5f;

    private Vector3 moveDirection;
    private bool isJumping = false;
    private bool hasJumped = false;

    private Vector3 jumpStartPos;
    private Vector3 jumpEndPos;
    private float jumpProgress = 0f;
    private float jumpLength = 0f;

    void Start()
    {
        ResetHorse();
    }

    void Update()
    {
        HandleInput();

        if (isJumping)
        {
            HandleJump();
        }
        else if (isMoving)
        {
            MoveHorse();

            if (obstacleInfo != null && !hasJumped)
            {
                if (CanJumpObstacle())
                {
                    Debug.Log("Jump condition met");
                    StartJump();
                }
            }
            else if (obstacleInfo == null)
            {
                Debug.LogWarning("ObstacleInfo is not assigned!");
            }
        }
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            StartMovement();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetHorse();
        }
    }

    void StartMovement()
    {
        if (startPoint == null || targetPoint == null) return;

        moveDirection = (targetPoint.position - transform.position).normalized;
        moveDirection.y = 0f;

        if (moveDirection != Vector3.zero)
        {
            transform.forward = moveDirection;
            isMoving = true;
        }
    }

    void MoveHorse()
    {
        if (targetPoint == null) return;

        Vector3 targetPos = new Vector3(
            targetPoint.position.x,
            transform.position.y,
            targetPoint.position.z
        );

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPos,
            moveSpeed * Time.deltaTime
        );

        Vector3 lookDir = (targetPos - transform.position).normalized;
        lookDir.y = 0f;

        if (lookDir != Vector3.zero)
        {
            transform.forward = lookDir;
        }

        float distanceToTarget = Vector3.Distance(transform.position, targetPos);

        if (distanceToTarget < 0.05f)
        {
            isMoving = false;
        }
    }

    bool CanJumpObstacle()
    {
        Vector3 obstacleCenter = obstacleInfo.GetCenter();
        Vector3 obstacleForward = obstacleInfo.GetForward();

        Vector3 horsePosFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 obstacleCenterFlat = new Vector3(obstacleCenter.x, 0f, obstacleCenter.z);

        Vector3 toObstacle = (obstacleCenterFlat - horsePosFlat).normalized;
        Vector3 horseForwardFlat = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        Vector3 obstacleForwardFlat = new Vector3(obstacleForward.x, 0f, obstacleForward.z).normalized;

        float distanceToObstacle = Vector3.Distance(horsePosFlat, obstacleCenterFlat);

        // 1. Correct takeoff distance
        bool distanceOk =
            distanceToObstacle >= obstacleInfo.minTakeoffDistance &&
            distanceToObstacle <= obstacleInfo.maxTakeoffDistance;

        // 2. Horse facing toward obstacle
        float horseToObstacleDot = Vector3.Dot(horseForwardFlat, toObstacle);
        bool facingObstacle = horseToObstacleDot > 0.8f;

        // 3. Horse approaching from correct side
        float sideDot = Vector3.Dot(horseForwardFlat, obstacleForwardFlat);
        bool correctDirection = sideDot > 0.8f;

        // 4. Horse reasonably centered relative to obstacle
        Vector3 obstacleRight = Vector3.Cross(Vector3.up, obstacleForwardFlat).normalized;
        float lateralOffset = Mathf.Abs(Vector3.Dot(horsePosFlat - obstacleCenterFlat, obstacleRight));
        bool centeredEnough = lateralOffset <= maxCenterOffset;

        // 5. Approach angle check
        float angle = Vector3.Angle(horseForwardFlat, obstacleForwardFlat);
        bool angleOk = angle <= maxApproachAngle;

        Debug.Log(
            $"distance={distanceToObstacle:F2}, " +
            $"distanceOk={distanceOk}, " +
            $"facingObstacle={facingObstacle}, " +
            $"correctDirection={correctDirection}, " +
            $"centeredEnough={centeredEnough}, " +
            $"angle={angle:F2}, angleOk={angleOk}"
        );

        return distanceOk && facingObstacle && correctDirection && centeredEnough && angleOk;
    }

    void StartJump()
    {
        if (isJumping) return;

        isJumping = true;
        hasJumped = true;
        jumpProgress = 0f;

        jumpStartPos = transform.position;
        jumpEndPos = jumpStartPos + transform.forward * jumpDistance;
        jumpLength = Vector3.Distance(jumpStartPos, jumpEndPos);

        Debug.Log("Jump started");
    }

    void HandleJump()
    {
        if (jumpLength <= 0f)
        {
            isJumping = false;
            return;
        }

        jumpProgress += (moveSpeed / jumpLength) * Time.deltaTime;

        Vector3 flatPos = Vector3.Lerp(jumpStartPos, jumpEndPos, jumpProgress);
        float arc = 4f * jumpHeight * jumpProgress * (1f - jumpProgress);

        transform.position = new Vector3(
            flatPos.x,
            jumpStartPos.y + arc,
            flatPos.z
        );

        if (jumpProgress >= 1f)
        {
            transform.position = new Vector3(
                jumpEndPos.x,
                jumpStartPos.y,
                jumpEndPos.z
            );

            isJumping = false;
            isMoving = true;
            Debug.Log("Jump finished");
        }
    }

    public void ResetHorse()
    {
        if (startPoint == null) return;

        transform.position = startPoint.position;
        isMoving = false;
        isJumping = false;
        hasJumped = false;
        jumpProgress = 0f;

        if (targetPoint != null)
        {
            Vector3 lookDir = (targetPoint.position - transform.position).normalized;
            lookDir.y = 0f;

            if (lookDir != Vector3.zero)
            {
                transform.forward = lookDir;
            }
        }
    }
}