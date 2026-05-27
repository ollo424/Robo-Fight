using UnityEngine;

[RequireComponent(typeof(SteeringMovementController))]
[RequireComponent(typeof(MechaCombatController))]
public class MechaUnitController : MonoBehaviour
{
    public float targetRefreshInterval = 0.25f;
    public float wanderRadius = 3f;
    public float keepDistanceMin = 6f;
    public float keepDistanceMax = 8f;
    public float strafeDistance = 1.5f;
    [Header("Ranged Distance Band")]
    public float rangedMinRangeFactor = 0.65f;
    public float rangedMaxRangeFactor = 0.9f;
    [Min(0)] public int rangedEdgeInsetCells = 2;
    [Header("Ranged Random Patrol")]
    public float rangedMinEnemyDistance = 3f;
    public int rangedBuildingClearanceCells = 2;
    public float rangedTargetReachDistance = 0.6f;
    public float rangedRetargetMinTime = 0.8f;
    public float rangedRetargetMaxTime = 1.6f;
    [Header("Melee Unstuck Random")]
    public bool enableMeleeUnstuckRandom = true;
    public float meleeStuckSeconds = 0.8f;
    public float meleeRandomRetargetCooldown = 1f;
    public float meleeMinEnemyDistanceForRandom = 1.5f;
    public int meleeRandomBuildingClearanceCells = 1;
    public int meleeRandomEdgeInsetCells = 1;
    public float meleeStuckVelocityThreshold = 0.08f;
    [Header("Pathing")]
    public float pathDetourTolerance = 6f;

    private SteeringMovementController movement;
    private MechaCombatController combat;
    private float refreshTimer;
    private Vector2 wanderTarget;
    private float debugLogTimer;
    private string lastMeleeTargetName;
    private Vector2 rangedPatrolTarget;
    private bool hasRangedPatrolTarget;
    private float rangedRetargetTimer;
    private float meleeStuckTimer;
    private float meleeRandomTimer;
    private Vector2 meleeRandomTarget;
    private bool hasMeleeRandomTarget;

    private void Awake()
    {
        movement = GetComponent<SteeringMovementController>();
        combat = GetComponent<MechaCombatController>();
        wanderTarget = transform.position;
    }

    private void Update()
    {
        if (!combat.IsAlive) return;
        refreshTimer -= Time.deltaTime;
        debugLogTimer -= Time.deltaTime;
        if (refreshTimer > 0f) return;
        refreshTimer = targetRefreshInterval;

        MechaCombatController nearest = FindNearestEnemy();
        if (nearest == null)
        {
            UpdateWander();
            LogAiDecision("no_enemy_wander", Vector2.Distance(transform.position, wanderTarget), wanderTarget);
            return;
        }

        float distance = Vector2.Distance(transform.position, nearest.transform.position);
        bool prefersBand = combat.mechaClass == MechaClass.Gunner || combat.mechaClass == MechaClass.Bomber;
        float adaptiveMin;
        float adaptiveMax;
        if (prefersBand)
        {
            adaptiveMin = Mathf.Max(0.8f, combat.attackRange * rangedMinRangeFactor);
            adaptiveMax = Mathf.Max(adaptiveMin + 0.25f, combat.attackRange * rangedMaxRangeFactor);
        }
        else
        {
            adaptiveMin = Mathf.Min(keepDistanceMin, combat.attackRange * 0.9f);
            adaptiveMax = Mathf.Max(keepDistanceMax, combat.attackRange * 1.15f);
        }
        #region agent log
        AgentDebugLogger.Log(
            "H22",
            "MechaUnitController.cs:Update",
            "Distance band evaluated",
            "{\"name\":\"" + gameObject.name + "\",\"class\":\"" + combat.mechaClass + "\",\"distance\":" + distance.ToString("0.###") + ",\"adaptiveMin\":" + adaptiveMin.ToString("0.###") + ",\"adaptiveMax\":" + adaptiveMax.ToString("0.###") + ",\"attackRange\":" + combat.attackRange.ToString("0.###") + "}"
        );
        #endregion

        if (prefersBand)
        {
            rangedRetargetTimer -= targetRefreshInterval;
            bool reached = hasRangedPatrolTarget &&
                           Vector2.Distance(transform.position, rangedPatrolTarget) <= Mathf.Max(0.2f, rangedTargetReachDistance);
            if (!hasRangedPatrolTarget || reached || rangedRetargetTimer <= 0f)
            {
                if (TryPickRangedPatrolTarget((Vector2)nearest.transform.position, out Vector2 picked))
                {
                    rangedPatrolTarget = picked;
                    hasRangedPatrolTarget = true;
                    rangedRetargetTimer = Random.Range(
                        Mathf.Max(0.1f, rangedRetargetMinTime),
                        Mathf.Max(rangedRetargetMinTime + 0.1f, rangedRetargetMaxTime)
                    );
                }
                else
                {
                    rangedPatrolTarget = KeepAwayFromInnerEdges((Vector2)transform.position);
                    hasRangedPatrolTarget = true;
                    rangedRetargetTimer = 0.5f;
                }
            }
            SetSmartMoveTarget(rangedPatrolTarget);
            LogAiDecision("ranged_random_patrol", distance, rangedPatrolTarget);
        }
        else
        {
            // Melee birimleri (Sword/ShieldKnife) direkt hedefe gider, kacis davranisi olmaz.
            float meleeRange = Mathf.Min(combat.meleeAttackRange, combat.attackRange);
            if (distance <= meleeRange)
            {
                movement.ClearMoveTarget();
                meleeStuckTimer = 0f;
                meleeRandomTimer = 0f;
                hasMeleeRandomTarget = false;
                #region agent log
                AgentDebugLogger.Log(
                    "H32",
                    "MechaUnitController.cs:Update",
                    "Melee in-range holding position",
                    "{\"name\":\"" + gameObject.name + "\",\"enemy\":\"" + nearest.gameObject.name + "\",\"distance\":" + distance.ToString("0.###") + ",\"meleeRange\":" + meleeRange.ToString("0.###") + "}"
                );
                #endregion
            }
            else
            {
                float speed = movement != null ? movement.Velocity.magnitude : 0f;
                if (speed < Mathf.Max(0.01f, meleeStuckVelocityThreshold))
                {
                    meleeStuckTimer += targetRefreshInterval;
                }
                else
                {
                    meleeStuckTimer = 0f;
                }

                meleeRandomTimer -= targetRefreshInterval;
                bool reachedRandom = hasMeleeRandomTarget &&
                                     Vector2.Distance(transform.position, meleeRandomTarget) <= Mathf.Max(0.2f, rangedTargetReachDistance);

                bool shouldUseUnstuckRandom = enableMeleeUnstuckRandom &&
                                              meleeStuckTimer >= Mathf.Max(0.2f, meleeStuckSeconds);

                if (shouldUseUnstuckRandom)
                {
                    if (!hasMeleeRandomTarget || reachedRandom || meleeRandomTimer <= 0f)
                    {
                        if (TryPickRandomTarget(
                            (Vector2)nearest.transform.position,
                            Mathf.Max(0f, meleeMinEnemyDistanceForRandom),
                            Mathf.Max(0, meleeRandomBuildingClearanceCells),
                            Mathf.Max(0, meleeRandomEdgeInsetCells),
                            out Vector2 picked))
                        {
                            meleeRandomTarget = picked;
                            hasMeleeRandomTarget = true;
                            meleeRandomTimer = Mathf.Max(0.2f, meleeRandomRetargetCooldown);
                        }
                    }

                    if (hasMeleeRandomTarget)
                    {
                        SetSmartMoveTarget(meleeRandomTarget);
                        LogAiDecision("melee_unstuck_random", distance, meleeRandomTarget);
                    }
                    else
                    {
                        SetSmartMoveTarget(nearest.transform.position);
                    }
                }
                else
                {
                    hasMeleeRandomTarget = false;
                    SetSmartMoveTarget(nearest.transform.position);
                }
            }
            if (lastMeleeTargetName != nearest.gameObject.name)
            {
                #region agent log
                AgentDebugLogger.Log(
                    "H32",
                    "MechaUnitController.cs:Update",
                    "Melee target switched",
                    "{\"name\":\"" + gameObject.name + "\",\"previous\":\"" + (lastMeleeTargetName ?? "null") + "\",\"next\":\"" + nearest.gameObject.name + "\",\"distance\":" + distance.ToString("0.###") + "}"
                );
                #endregion
                lastMeleeTargetName = nearest.gameObject.name;
            }
            #region agent log
            AgentDebugLogger.Log(
                "H23",
                "MechaUnitController.cs:Update",
                "Melee chase target set",
                "{\"name\":\"" + gameObject.name + "\",\"enemy\":\"" + nearest.gameObject.name + "\",\"distance\":" + distance.ToString("0.###") + "}"
            );
            #endregion
            LogAiDecision("melee_chase", distance, nearest.transform.position);
        }

        combat.TryAttack(nearest);
    }

    private MechaCombatController FindNearestEnemy()
    {
        MechaCombatController[] all = FindObjectsByType<MechaCombatController>(FindObjectsSortMode.None);
        MechaCombatController best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < all.Length; i++)
        {
            MechaCombatController other = all[i];
            if (other == null || !other.IsAlive) continue;
            if (other == combat) continue;
            if (other.gameObject == gameObject) continue;
            if (other.transform.root == transform.root) continue;

            float d = Vector2.Distance(transform.position, other.transform.position);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = other;
            }
        }

        return best;
    }

    private void UpdateWander()
    {
        if (Vector2.Distance(transform.position, wanderTarget) < 0.6f)
        {
            wanderTarget = (Vector2)transform.position + Random.insideUnitCircle * wanderRadius;
        }
        SetSmartMoveTarget(wanderTarget);
    }

    private void LogAiDecision(string decision, float distance, Vector2 target)
    {
        if (debugLogTimer > 0f) return;
        debugLogTimer = 1f;
        #region agent log
        AgentDebugLogger.Log(
            "H2",
            "MechaUnitController.cs:Update",
            "AI decision",
            "{\"name\":\"" + gameObject.name + "\",\"class\":\"" + combat.mechaClass + "\",\"decision\":\"" + decision + "\",\"distance\":" + distance.ToString("0.###") + ",\"targetX\":" + target.x.ToString("0.###") + ",\"targetY\":" + target.y.ToString("0.###") + "}"
        );
        #endregion
    }

    private void SetSmartMoveTarget(Vector2 desiredTarget)
    {
        Vector2 nextTarget = desiredTarget;
        bool usedPath = IsometricArenaController.Instance != null &&
                        IsometricArenaController.Instance.TryGetNextWaypoint(transform.position, desiredTarget, out nextTarget);

        if (!usedPath && IsometricArenaController.Instance != null)
        {
            Vector2 snappedDesired;
            if (IsometricArenaController.Instance.TryGetNearestWalkableWorldPoint(desiredTarget, 5, out snappedDesired))
            {
                usedPath = IsometricArenaController.Instance.TryGetNextWaypoint(transform.position, snappedDesired, out nextTarget);
            }
        }

        if (usedPath)
        {
            float desiredDist = Vector2.Distance(transform.position, desiredTarget);
            float nextDist = Vector2.Distance(transform.position, nextTarget);
            if (nextDist > desiredDist + Mathf.Max(0.5f, pathDetourTolerance))
            {
                #region agent log
                AgentDebugLogger.Log(
                    "H34",
                    "MechaUnitController.cs:SetSmartMoveTarget",
                    "Large path detour accepted",
                    "{\"name\":\"" + gameObject.name + "\",\"desiredDist\":" + desiredDist.ToString("0.###") + ",\"nextDist\":" + nextDist.ToString("0.###") + ",\"detourTolerance\":" + pathDetourTolerance.ToString("0.###") + "}"
                );
                #endregion
            }
        }

        if (!usedPath)
        {
            if (IsometricArenaController.Instance != null &&
                IsometricArenaController.Instance.TryGetNearestWalkableWorldPoint(desiredTarget, 8, out Vector2 clamped))
            {
                nextTarget = clamped;
                #region agent log
                AgentDebugLogger.Log(
                    "H19",
                    "MechaUnitController.cs:SetSmartMoveTarget",
                    "Fallback clamped to nearest walkable",
                    "{\"name\":\"" + gameObject.name + "\",\"desiredX\":" + desiredTarget.x.ToString("0.###") + ",\"desiredY\":" + desiredTarget.y.ToString("0.###") + ",\"clampedX\":" + clamped.x.ToString("0.###") + ",\"clampedY\":" + clamped.y.ToString("0.###") + "}"
                );
                #endregion
            }
            else
            {
                nextTarget = desiredTarget;
            }
        }

        movement.SetMoveTarget(nextTarget);

        #region agent log
        AgentDebugLogger.Log(
            "H12",
            "MechaUnitController.cs:SetSmartMoveTarget",
            "Path steering target resolved",
            "{\"name\":\"" + gameObject.name + "\",\"usedPath\":" + (usedPath ? "true" : "false") + ",\"desiredX\":" + desiredTarget.x.ToString("0.###") + ",\"desiredY\":" + desiredTarget.y.ToString("0.###") + ",\"nextX\":" + nextTarget.x.ToString("0.###") + ",\"nextY\":" + nextTarget.y.ToString("0.###") + "}"
        );
        #endregion
    }

    private Vector2 KeepAwayFromInnerEdges(Vector2 desired)
    {
        if (IsometricArenaController.Instance == null) return desired;
        if (IsometricArenaController.Instance.TryClampToInnerInsetWorld(desired, Mathf.Max(0, rangedEdgeInsetCells), out Vector2 clamped))
        {
            return clamped;
        }
        return desired;
    }

    private bool TryPickRangedPatrolTarget(Vector2 enemyPos, out Vector2 picked)
    {
        return TryPickRandomTarget(
            enemyPos,
            Mathf.Max(0.5f, rangedMinEnemyDistance),
            Mathf.Max(0, rangedBuildingClearanceCells),
            Mathf.Max(0, rangedEdgeInsetCells),
            out picked);
    }

    private bool TryPickRandomTarget(Vector2 enemyPos, float minEnemyDistance, int buildingClearanceCells, int edgeInsetCells, out Vector2 picked)
    {
        picked = transform.position;
        IsometricArenaController arena = IsometricArenaController.Instance;
        if (arena == null || arena.roadGenerator == null) return false;

        var candidates = arena.roadGenerator.GetSpawnableRoadPoints(Mathf.Max(0, buildingClearanceCells));
        if (candidates == null || candidates.Count == 0) return false;

        int start = Random.Range(0, candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector2 c = candidates[(start + i) % candidates.Count];
            if (Vector2.Distance(c, enemyPos) < minEnemyDistance) continue;

            if (!arena.TryClampToInnerInsetWorld(c, Mathf.Max(0, edgeInsetCells), out Vector2 clamped)) continue;
            if (Vector2.Distance(c, clamped) > 0.05f) continue;
            if (HasBlockingMechaOnPath((Vector2)transform.position, clamped)) continue;

            picked = clamped;
            return true;
        }

        return false;
    }

    private bool HasBlockingMechaOnPath(Vector2 from, Vector2 to)
    {
        if (movement == null || movement.mechaMask.value == 0) return false;
        RaycastHit2D[] hits = Physics2D.LinecastAll(from, to, movement.mechaMask);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D col = hits[i].collider;
            if (col == null) continue;
            MechaCombatController m = col.GetComponentInParent<MechaCombatController>();
            if (m == null || !m.IsAlive) continue;
            if (m == combat) continue;
            return true;
        }
        return false;
    }
}
