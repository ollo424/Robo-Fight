using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class RobotController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2f;
    public float wallCheckDistance = 1f;
    public float turnLerpSpeed = 12f;
    public float turnStopAngleThreshold = 2f;

    [Header("Awareness")]
    public int visionRangeCells = 5;
    public int softChaseDistanceCells = 10;
    public int keepDistanceMinCells = 6;
    public int keepDistanceMaxCells = 8;
    public LayerMask wallMask;
    public LayerMask robotMask;
    public LayerMask powerUpMask;

    private float speedMultiplier = 1f;
    private RobotCombat combat;
    private MazeGenerator maze;
    private Rigidbody2D rb;
    private Vector2Int currentDirection = Vector2Int.up;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float cellStep = 1f;
    private bool invertVisualForward;
    private readonly Queue<Vector2Int> plannedPath = new Queue<Vector2Int>();
    private Transform dashChaseTarget;
    private Vector2Int previousCell;
    private bool hasPreviousCell;
    private bool dashControlActive;
    private Vector2 dashDirection = Vector2.up;
    private Quaternion dashLockedRotation;
    private bool dashBlocked;

    public Vector2 ForwardDirection => currentDirection;
    public bool IsTurning => Quaternion.Angle(transform.rotation, targetRotation) > turnStopAngleThreshold;
    public bool IsDashBlocked => dashBlocked;

    private void Awake()
    {
        combat = GetComponent<RobotCombat>();
        rb = GetComponent<Rigidbody2D>();
        ConfigurePhysicsBody();
    }

    private void Start()
    {
        maze = GameManager.Instance != null ? GameManager.Instance.mazeGenerator : FindAnyObjectByType<MazeGenerator>();
        if (maze != null)
        {
            cellStep = Mathf.Max(0.01f, maze.cellSize);
            transform.position = maze.CellToWorld(maze.WorldToCell(transform.position));
        }
        if (combat != null && combat.robotType == RobotType.Bomber)
        {
            // Bomber prefab is visually reversed compared to other robots.
            invertVisualForward = true;
        }
        previousCell = GetCurrentCell();
        hasPreviousCell = false;
        targetPosition = transform.position;
        targetRotation = CalculateTargetRotation();
        transform.rotation = targetRotation;
    }

    private void Update()
    {
        if (combat != null && !combat.IsAlive) return;

        if (dashControlActive)
        {
            transform.rotation = dashLockedRotation;
            float dashStep = moveSpeed * speedMultiplier * Time.deltaTime;
            Vector2 pos = transform.position;
            RaycastHit2D wallHit = Physics2D.Raycast(pos, dashDirection, dashStep + 0.05f, wallMask);
            if (wallHit.collider == null)
            {
                transform.position += (Vector3)(dashDirection * dashStep);
            }
            else
            {
                dashBlocked = true;
            }
            return;
        }

        float lerpT = Mathf.Clamp01(turnLerpSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, lerpT);
        float remainingTurn = Quaternion.Angle(transform.rotation, targetRotation);

        // Do not move while still turning.
        if (remainingTurn > turnStopAngleThreshold)
        {
            return;
        }

        if ((transform.position - targetPosition).sqrMagnitude > 0.0001f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                moveSpeed * speedMultiplier * Time.deltaTime
            );
            return;
        }

        transform.position = targetPosition;
        DecideNextGridStep();
    }

    public void SetSpeedMultiplier(float value)
    {
        speedMultiplier = Mathf.Max(0.1f, value);
    }

    public void SetSpeedMultiplierFor(float value, float duration)
    {
        StartCoroutine(SpeedRoutine(value, duration));
    }

    private IEnumerator SpeedRoutine(float value, float duration)
    {
        float previous = speedMultiplier;
        speedMultiplier = Mathf.Max(0.1f, value);
        yield return new WaitForSeconds(duration);
        speedMultiplier = previous;
    }

    private void DecideNextGridStep()
    {
        Vector2Int currentCell = GetCurrentCell();

        if (combat != null && combat.robotType == RobotType.Dasher && combat.IsDashing && dashChaseTarget != null)
        {
            Vector2Int chaseCell = maze.WorldToCell(dashChaseTarget.position);
            if (TryBuildPath(currentCell, chaseCell, out List<Vector2Int> dashPath))
            {
                SetPath(dashPath);
            }
        }
        else if (plannedPath.Count == 0)
        {
            PlanPathFromPerception(currentCell);
        }

        if (!TryStepAlongPath(currentCell))
        {
            TakeRandomLocalStep(currentCell);
        }
    }

    private void PlanPathFromPerception(Vector2Int currentCell)
    {
        bool pathSet = false;

        // Gunner/Bomber try to keep enemy distance in a soft 6-8 cell band.
        if (combat != null && (combat.robotType == RobotType.Gunner || combat.robotType == RobotType.Bomber))
        {
            Vector2Int nearestEnemyCell;
            int enemyDistance;
            if (TryFindNearestEnemyCellGlobal(currentCell, out nearestEnemyCell, out enemyDistance))
            {
                int minBand = Mathf.Max(1, keepDistanceMinCells);
                int maxBand = Mathf.Max(minBand, keepDistanceMaxCells);

                if (enemyDistance < minBand)
                {
                    Vector2Int fleeCell = FindFleeCell(currentCell, nearestEnemyCell);
                    if (TryBuildPath(currentCell, fleeCell, out List<Vector2Int> fleeBandPath))
                    {
                        SetPath(fleeBandPath);
                        pathSet = true;
                    }
                }
                else if (enemyDistance > maxBand)
                {
                    if (TryBuildPath(currentCell, nearestEnemyCell, out List<Vector2Int> approachBandPath))
                    {
                        SetPath(TrimPathToBand(approachBandPath, enemyDistance, maxBand));
                        pathSet = true;
                    }
                }
                else if (combat.robotType == RobotType.Gunner)
                {
                    // In ideal range, Gunner should face enemy instead of drifting by.
                    if (TryFaceCell(currentCell, nearestEnemyCell))
                    {
                        pathSet = true;
                    }
                }
            }
        }

        Vector2Int enemyCell;
        if (!pathSet && TryFindEnemyInPlusSight(currentCell, out enemyCell))
        {
            if (combat != null && (combat.robotType == RobotType.Bomber || combat.robotType == RobotType.Gunner))
            {
                Vector2Int fleeCell = FindFleeCell(currentCell, enemyCell);
                if (TryBuildPath(currentCell, fleeCell, out List<Vector2Int> fleePath))
                {
                    SetPath(fleePath);
                    pathSet = true;
                }
            }
            else
            {
                if (TryBuildPath(currentCell, enemyCell, out List<Vector2Int> chasePath))
                {
                    SetPath(chasePath);
                    pathSet = true;
                }
            }
        }

        if (!pathSet)
        {
            Vector2Int powerCell;
            if (TryFindPowerUpInPlusSight(currentCell, out powerCell))
            {
                if (TryBuildPath(currentCell, powerCell, out List<Vector2Int> powerPath))
                {
                    SetPath(powerPath);
                    pathSet = true;
                }
            }
        }

        // Soft pathfinding: if everyone is too far, move closer to nearest enemy.
        if (!pathSet)
        {
            Vector2Int nearestEnemyCell;
            int enemyDistance;
            if (TryFindNearestEnemyCellGlobal(currentCell, out nearestEnemyCell, out enemyDistance))
            {
                if (enemyDistance >= Mathf.Max(2, softChaseDistanceCells))
                {
                    if (TryBuildPath(currentCell, nearestEnemyCell, out List<Vector2Int> convergePath))
                    {
                        SetPath(convergePath);
                    }
                }
            }
        }
    }

    private bool TryStepAlongPath(Vector2Int currentCell)
    {
        while (plannedPath.Count > 0 && plannedPath.Peek() == currentCell)
        {
            plannedPath.Dequeue();
        }
        if (plannedPath.Count == 0) return false;

        Vector2Int nextCell = plannedPath.Peek();
        if (maze != null && !maze.IsWalkableCell(nextCell))
        {
            plannedPath.Clear();
            return false;
        }

        if (hasPreviousCell && nextCell == previousCell)
        {
            Vector2Int afterNext = plannedPath.Count > 1 ? GetNthPathCell(1) : nextCell;
            if (afterNext != nextCell)
            {
                plannedPath.Clear();
                return false;
            }
        }
        plannedPath.Dequeue();
        StepToCell(currentCell, nextCell);
        return true;
    }

    private void TakeRandomLocalStep(Vector2Int currentCell)
    {
        Vector2Int forward = currentDirection;
        Vector2Int left = new Vector2Int(-currentDirection.y, currentDirection.x);
        Vector2Int right = new Vector2Int(currentDirection.y, -currentDirection.x);

        List<Vector2Int> candidates = new List<Vector2Int>();
        if (IsWalkableNeighbor(currentCell, forward)) candidates.Add(currentCell + forward);
        if (IsWalkableNeighbor(currentCell, left)) candidates.Add(currentCell + left);
        if (IsWalkableNeighbor(currentCell, right)) candidates.Add(currentCell + right);

        if (candidates.Count == 0) return;

        if (hasPreviousCell && candidates.Count > 1)
        {
            candidates.RemoveAll(c => c == previousCell);
            if (candidates.Count == 0) candidates.Add(previousCell);
        }

        Vector2Int pick = candidates[Random.Range(0, candidates.Count)];
        StepToCell(currentCell, pick);
    }

    private bool TryFindEnemyInPlusSight(Vector2Int originCell, out Vector2Int enemyCell)
    {
        enemyCell = originCell;
        float bestDistance = float.MaxValue;
        bool foundEnemy = false;

        Vector2Int[] sightDirs =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        for (int d = 0; d < sightDirs.Length; d++)
        {
            for (int step = 1; step <= Mathf.Max(1, visionRangeCells); step++)
            {
                Vector2Int scanCell = originCell + sightDirs[d] * step;
                if (maze != null && !maze.IsWalkableCell(scanCell)) break;

                Vector3 cellWorld = maze.CellToWorld(scanCell);

                Collider2D[] robots = Physics2D.OverlapCircleAll(cellWorld, 0.35f, robotMask);
                for (int i = 0; i < robots.Length; i++)
                {
                    if (robots[i] == null || robots[i].gameObject == gameObject) continue;
                    RobotCombat rc = robots[i].GetComponent<RobotCombat>();
                    if (rc == null || !rc.IsAlive) continue;

                    float distance = Vector2Int.Distance(originCell, scanCell);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        enemyCell = scanCell;
                        foundEnemy = true;
                    }
                }
            }
        }

        return foundEnemy;
    }

    private bool TryFindPowerUpInPlusSight(Vector2Int originCell, out Vector2Int powerCell)
    {
        powerCell = originCell;

        Vector2Int[] sightDirs =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        float bestDistance = float.MaxValue;
        bool found = false;

        for (int d = 0; d < sightDirs.Length; d++)
        {
            for (int step = 1; step <= Mathf.Max(1, visionRangeCells); step++)
            {
                Vector2Int scanCell = originCell + sightDirs[d] * step;
                if (maze != null && !maze.IsWalkableCell(scanCell)) break;

                Vector3 cellWorld = maze.CellToWorld(scanCell);

                Collider2D[] powerUps = Physics2D.OverlapCircleAll(cellWorld, 0.35f, powerUpMask);
                for (int i = 0; i < powerUps.Length; i++)
                {
                    PowerUpPickup pickup = powerUps[i].GetComponent<PowerUpPickup>();
                    if (pickup == null) continue;

                    float distance = Vector2Int.Distance(originCell, scanCell);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        powerCell = scanCell;
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    private void StepToCell(Vector2Int fromCell, Vector2Int toCell)
    {
        Vector2Int dir = toCell - fromCell;
        if (Mathf.Abs(dir.x) + Mathf.Abs(dir.y) != 1) return;
        previousCell = fromCell;
        hasPreviousCell = true;
        currentDirection = dir;
        targetRotation = CalculateTargetRotation();
        targetPosition = maze != null ? maze.CellToWorld(toCell) : transform.position + new Vector3(dir.x, dir.y, 0f) * cellStep;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        float drawStep = Mathf.Max(0.01f, cellStep);
        int drawRange = Mathf.Max(1, visionRangeCells);
        Vector3 origin = transform.position;

        Gizmos.DrawLine(origin, origin + Vector3.up * drawStep * drawRange);
        Gizmos.DrawLine(origin, origin + Vector3.down * drawStep * drawRange);
        Gizmos.DrawLine(origin, origin + Vector3.left * drawStep * drawRange);
        Gizmos.DrawLine(origin, origin + Vector3.right * drawStep * drawRange);
    }

    private void ConfigurePhysicsBody()
    {
        if (rb == null) return;

        // Robots are grid-driven by code, so physics forces must stay disabled.
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    private Quaternion CalculateTargetRotation()
    {
        float angle = Mathf.Atan2(currentDirection.y, currentDirection.x) * Mathf.Rad2Deg;
        if (invertVisualForward) angle += 180f;
        return Quaternion.Euler(0f, 0f, angle);
    }

    public void SetDashChaseTarget(Transform target)
    {
        dashChaseTarget = target;
    }

    public void ClearDashChaseTarget()
    {
        dashChaseTarget = null;
    }

    public void BeginStraightDash(Vector2 direction)
    {
        Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : ForwardDirection.normalized;
        if (dir.sqrMagnitude <= 0.0001f) dir = Vector2.up;
        dashDirection = dir;
        dashControlActive = true;
        dashBlocked = false;
        dashLockedRotation = transform.rotation;
        targetPosition = transform.position;
        plannedPath.Clear();
    }

    public void EndStraightDash()
    {
        dashControlActive = false;
        dashBlocked = false;
        if (maze != null)
        {
            Vector2Int cell = maze.WorldToCell(transform.position);
            transform.position = maze.CellToWorld(cell);
        }

        targetPosition = transform.position;
    }

    private Vector2Int GetCurrentCell()
    {
        if (maze != null) return maze.WorldToCell(transform.position);
        return new Vector2Int(Mathf.RoundToInt(transform.position.x / cellStep), Mathf.RoundToInt(transform.position.y / cellStep));
    }

    private bool IsWalkableNeighbor(Vector2Int from, Vector2Int delta)
    {
        if (delta == Vector2Int.zero) return false;
        Vector2Int next = from + delta;
        if (maze == null) return true;
        return maze.IsWalkableCell(next);
    }

    private void SetPath(List<Vector2Int> path)
    {
        plannedPath.Clear();
        if (path == null) return;
        for (int i = 0; i < path.Count; i++)
        {
            plannedPath.Enqueue(path[i]);
        }
    }

    private bool TryBuildPath(Vector2Int start, Vector2Int goal, out List<Vector2Int> path)
    {
        path = new List<Vector2Int>();
        if (maze == null) return false;
        if (!maze.IsWalkableCell(start) || !maze.IsWalkableCell(goal)) return false;
        if (start == goal) return false;

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        frontier.Enqueue(start);
        cameFrom[start] = start;

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        bool found = false;

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            if (current == goal)
            {
                found = true;
                break;
            }

            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2Int next = current + dirs[i];
                if (!maze.IsWalkableCell(next)) continue;
                if (cameFrom.ContainsKey(next)) continue;
                cameFrom[next] = current;
                frontier.Enqueue(next);
            }
        }

        if (!found) return false;

        Vector2Int cursor = goal;
        while (cursor != start)
        {
            path.Add(cursor);
            cursor = cameFrom[cursor];
        }
        path.Reverse();
        return path.Count > 0;
    }

    private Vector2Int GetNthPathCell(int index)
    {
        if (index < 0 || index >= plannedPath.Count) return GetCurrentCell();
        int i = 0;
        foreach (Vector2Int cell in plannedPath)
        {
            if (i == index) return cell;
            i++;
        }
        return GetCurrentCell();
    }

    private Vector2Int FindFleeCell(Vector2Int currentCell, Vector2Int enemyCell)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        Vector2Int bestCell = currentCell;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < dirs.Length; i++)
        {
            for (int step = 1; step <= Mathf.Max(2, visionRangeCells); step++)
            {
                Vector2Int candidate = currentCell + dirs[i] * step;
                if (!maze.IsWalkableCell(candidate)) break;

                float score = Vector2Int.Distance(candidate, enemyCell);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCell = candidate;
                }
            }
        }

        return bestCell;
    }

    private List<Vector2Int> TrimPathToBand(List<Vector2Int> fullPath, int currentDistance, int maxBand)
    {
        if (fullPath == null || fullPath.Count == 0) return fullPath;
        int stepsNeeded = Mathf.Max(1, currentDistance - maxBand);
        int count = Mathf.Min(stepsNeeded, fullPath.Count);
        return fullPath.GetRange(0, count);
    }

    private bool TryFaceCell(Vector2Int currentCell, Vector2Int targetCell)
    {
        Vector2Int delta = targetCell - currentCell;
        if (delta == Vector2Int.zero) return false;

        Vector2Int faceDir;
        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
        {
            faceDir = new Vector2Int(delta.x > 0 ? 1 : -1, 0);
        }
        else
        {
            faceDir = new Vector2Int(0, delta.y > 0 ? 1 : -1);
        }

        if (faceDir == Vector2Int.zero) return false;
        currentDirection = faceDir;
        targetRotation = CalculateTargetRotation();
        targetPosition = transform.position;
        plannedPath.Clear();
        return true;
    }

    private bool TryFindNearestEnemyCellGlobal(Vector2Int currentCell, out Vector2Int enemyCell, out int distance)
    {
        enemyCell = currentCell;
        distance = int.MaxValue;

        RobotCombat[] all = FindObjectsByType<RobotCombat>(FindObjectsSortMode.None);
        bool found = false;

        for (int i = 0; i < all.Length; i++)
        {
            RobotCombat other = all[i];
            if (other == null || !other.IsAlive) continue;
            if (other.gameObject == gameObject) continue;

            Vector2Int otherCell = maze != null ? maze.WorldToCell(other.transform.position) : currentCell;
            int d = Mathf.Abs(currentCell.x - otherCell.x) + Mathf.Abs(currentCell.y - otherCell.y);
            if (d < distance)
            {
                distance = d;
                enemyCell = otherCell;
                found = true;
            }
        }

        return found;
    }
}
