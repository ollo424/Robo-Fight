using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class SteeringMovementController : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float acceleration = 10f;
    public float stoppingDistance = 0.25f;
    public float separationRadius = 1.2f;
    public float separationWeight = 1.4f;
    public LayerMask mechaMask;
    public LayerMask obstacleMask = Physics2D.AllLayers;
    public float obstacleLookAhead = 0.9f;
    public float obstacleAvoidWeight = 1.8f;
    public bool flipByDirection = true;

    private Rigidbody2D rb;
    private Vector2 desiredVelocity;
    private Vector2 steeringTarget;
    private bool hasTarget;
    private Vector3 defaultScale;
    private float debugLogTimer;
    private float wallAvoidLogCooldown;
    private float speedMultiplier = 1f;

    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;

    public void SetSpeedMultiplier(float multiplier)
    {
        speedMultiplier = Mathf.Clamp(multiplier, 0.1f, 1f);
    }

    private void Awake()
    {
        float inspectorMoveSpeed = moveSpeed;
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        if (moveSpeed <= 0.01f) moveSpeed = 0.1f;
        #region agent log
        AgentDebugLogger.Log(
            "H27",
            "SteeringMovementController.cs:Awake",
            "Move speed initialized",
            "{\"name\":\"" + gameObject.name + "\",\"inspectorMoveSpeed\":" + inspectorMoveSpeed.ToString("0.###") + ",\"appliedMoveSpeed\":" + moveSpeed.ToString("0.###") + ",\"timeScale\":" + Time.timeScale.ToString("0.###") + "}"
        );
        #endregion
        defaultScale = transform.localScale;
    }

    private void FixedUpdate()
    {
        Vector2 targetVel = Vector2.zero;
        float distanceToTarget = -1f;
        float separationMag = 0f;
        float obstacleMag = 0f;

        if (hasTarget)
        {
            Vector2 toTarget = steeringTarget - rb.position;
            float distance = toTarget.magnitude;
            distanceToTarget = distance;
            if (distance > stoppingDistance)
            {
                Vector2 dir = toTarget.normalized;
                Vector2 separation = ComputeSeparationForce();
                Vector2 obstacleAvoid = ComputeObstacleAvoidance(dir);
                separationMag = separation.magnitude;
                obstacleMag = obstacleAvoid.magnitude;
                Vector2 finalDir = (dir + separation * separationWeight + obstacleAvoid * obstacleAvoidWeight).normalized;
                targetVel = finalDir * (moveSpeed * speedMultiplier);
            }
        }

        // Constant speed mode: directly use target velocity.
        desiredVelocity = targetVel;
        rb.linearVelocity = Vector2.ClampMagnitude(desiredVelocity, moveSpeed);

        if (flipByDirection && Mathf.Abs(desiredVelocity.x) > 0.05f)
        {
            float sign = desiredVelocity.x > 0f ? 1f : -1f;
            transform.localScale = new Vector3(Mathf.Abs(defaultScale.x) * sign, defaultScale.y, defaultScale.z);
        }

        debugLogTimer += Time.fixedDeltaTime;
        if (debugLogTimer >= 1f)
        {
            debugLogTimer = 0f;
            #region agent log
            AgentDebugLogger.Log(
                "H3",
                "SteeringMovementController.cs:FixedUpdate",
                "Movement tick",
                "{\"name\":\"" + gameObject.name + "\",\"simulated\":" + (rb != null && rb.simulated ? "true" : "false") + ",\"bodyType\":\"" + rb.bodyType + "\",\"hasTarget\":" + (hasTarget ? "true" : "false") + ",\"distanceToTarget\":" + distanceToTarget.ToString("0.###") + ",\"speed\":" + desiredVelocity.magnitude.ToString("0.###") + ",\"moveSpeed\":" + moveSpeed.ToString("0.###") + ",\"speedMultiplier\":" + speedMultiplier.ToString("0.###") + ",\"timeScale\":" + Time.timeScale.ToString("0.###") + ",\"targetX\":" + steeringTarget.x.ToString("0.###") + ",\"targetY\":" + steeringTarget.y.ToString("0.###") + "}"
            );
            #endregion

            if (distanceToTarget >= 0f && distanceToTarget <= 2f)
            {
                MechaCombatController combat = GetComponent<MechaCombatController>();
                #region agent log
                AgentDebugLogger.Log(
                    "H24",
                    "SteeringMovementController.cs:FixedUpdate",
                    "Close-range steering forces",
                    "{\"name\":\"" + gameObject.name + "\",\"class\":\"" + (combat != null ? combat.mechaClass.ToString() : "Unknown") + "\",\"distanceToTarget\":" + distanceToTarget.ToString("0.###") + ",\"separationMag\":" + separationMag.ToString("0.###") + ",\"obstacleMag\":" + obstacleMag.ToString("0.###") + "}"
                );
                #endregion
            }
        }
    }

    public void SetMoveTarget(Vector2 worldTarget)
    {
        steeringTarget = worldTarget;
        hasTarget = true;
    }

    public void ClearMoveTarget()
    {
        hasTarget = false;
    }

    private Vector2 ComputeSeparationForce()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(rb.position, separationRadius, mechaMask);
        if (hits.Length == 0) return Vector2.zero;

        Vector2 force = Vector2.zero;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null || hits[i].gameObject == gameObject) continue;
            Vector2 offset = rb.position - (Vector2)hits[i].transform.position;
            float dist = Mathf.Max(0.01f, offset.magnitude);
            force += offset.normalized / dist;
        }
        return force;
    }

    private Vector2 ComputeObstacleAvoidance(Vector2 desiredDir)
    {
        if (desiredDir.sqrMagnitude < 0.0001f) return Vector2.zero;

        RaycastHit2D hit = Physics2D.Raycast(rb.position, desiredDir, obstacleLookAhead, obstacleMask);
        if (!IsWallHit(hit)) return Vector2.zero;

        Vector2 tangent = Vector2.Perpendicular(hit.normal).normalized;
        float align = Vector2.Dot(tangent, desiredDir);
        Vector2 avoidDir = align < 0f ? -tangent : tangent;

        if (wallAvoidLogCooldown <= 0f)
        {
            wallAvoidLogCooldown = 0.5f;
            #region agent log
            AgentDebugLogger.Log(
                "H11",
                "SteeringMovementController.cs:ComputeObstacleAvoidance",
                "Wall avoidance applied",
                "{\"mecha\":\"" + gameObject.name + "\",\"wall\":\"" + hit.collider.gameObject.name + "\",\"hitDistance\":" + hit.distance.ToString("0.###") + ",\"avoidX\":" + avoidDir.x.ToString("0.###") + ",\"avoidY\":" + avoidDir.y.ToString("0.###") + "}"
            );
            #endregion
        }

        return avoidDir;
    }

    private bool IsWallHit(RaycastHit2D hit)
    {
        if (!hit.collider) return false;
        if (hit.collider.gameObject == gameObject) return false;
        return hit.collider.CompareTag("Wall");
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null || collision.collider == null) return;
        #region agent log
        AgentDebugLogger.Log(
            "H6",
            "SteeringMovementController.cs:OnCollisionEnter2D",
            "Collision detected",
            "{\"mecha\":\"" + gameObject.name + "\",\"other\":\"" + collision.collider.gameObject.name + "\",\"otherTag\":\"" + collision.collider.tag + "\"}"
        );
        #endregion
    }

    private void Update()
    {
        if (wallAvoidLogCooldown > 0f)
        {
            wallAvoidLogCooldown -= Time.deltaTime;
        }
    }
}
