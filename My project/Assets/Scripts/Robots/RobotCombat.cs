using System.Collections;
using UnityEngine;

public enum RobotType
{
    Dasher,
    Gunner,
    Bomber,
    Tank
}

public enum DamageSource
{
    Collision,
    Dash,
    Bullet,
    Bomb,
    TankRam
}

[RequireComponent(typeof(RobotController))]
public class RobotCombat : MonoBehaviour
{
    [Header("Identity")]
    public string displayName = "Robot";
    public Color baseColor = Color.white;
    public RobotType robotType;

    [Header("Common")]
    public SpriteRenderer bodyRenderer;
    public GameObject extraLifeHalo;
    public LayerMask robotMask;
    public LayerMask wallMask;
    public bool IsAlive => isAlive;
    public bool IsDashing => dashing;

    [Header("Dasher")]
    public float dashCheckDistance = 5f;
    public float dashMultiplier = 3f;
    public float dashDuration = 0.8f;
    public float dashCooldown = 1.2f;

    [Header("Gunner")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float shootCheckDistance = 4f;
    public float shootCooldown = 2f;
    public float gunnerReactionDelay = 0.5f;

    [Header("Bomber")]
    public GameObject bombPrefab;
    public float bombInterval = 3f;
    public int bombExplosionRange = 2;
    public float bombEffectLifeTime = 0.2f;

    [Header("Tank")]
    public float tankScale = 1.2f;
    public float tankBurstCooldown = 1.2f;
    public float tankBurstHitRadius = 0.35f;
    public GameObject tankBurstEffectPrefab;
    public float tankBurstEffectLifeTime = 0.2f;

    private RobotController controller;
    private MazeGenerator maze;
    private bool isAlive = true;
    private bool invincible;
    private bool hasExtraLife;

    private float dashTimer;
    private float shootTimer;
    private float bombTimer;
    private float tankBurstTimer;
    private float gunnerReactionTimer;
    private bool gunnerWaitingForShot;
    private bool dashing;
    private Color originalColor;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
        maze = GameManager.Instance != null ? GameManager.Instance.mazeGenerator : FindAnyObjectByType<MazeGenerator>();
        if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void Start()
    {
        originalColor = baseColor;
        if (bodyRenderer != null) bodyRenderer.color = baseColor;
        if (extraLifeHalo != null) extraLifeHalo.SetActive(false);

        if (robotType == RobotType.Tank)
        {
            transform.localScale *= tankScale;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterRobot(this);
        }
    }

    private void Update()
    {
        if (!isAlive) return;

        dashTimer -= Time.deltaTime;
        shootTimer -= Time.deltaTime;
        bombTimer -= Time.deltaTime;
        tankBurstTimer -= Time.deltaTime;
        gunnerReactionTimer -= Time.deltaTime;

        if (robotType == RobotType.Dasher) TryDash();
        if (robotType == RobotType.Gunner) TryShoot();
        if (robotType == RobotType.Bomber) TryDropBomb();
        if (robotType == RobotType.Tank)
        {
            TryTankBurst();
        }
    }

    public void ApplyPowerUp(PowerUpType type, float duration)
    {
        if (!isAlive) return;

        if (type == PowerUpType.Invincible)
        {
            StartCoroutine(InvincibleRoutine(duration));
            return;
        }

        if (type == PowerUpType.Speed)
        {
            if (controller != null) controller.SetSpeedMultiplierFor(2f, duration);
            return;
        }

        if (type == PowerUpType.ExtraLife)
        {
            hasExtraLife = true;
            if (extraLifeHalo != null) extraLifeHalo.SetActive(true);
        }
    }

    public void TakeHit(RobotCombat attacker, DamageSource source)
    {
        if (!isAlive) return;
        if (invincible) return;

        // Dasher should not die to tank ram while actively charging.
        if (robotType == RobotType.Dasher && dashing && source == DamageSource.TankRam)
        {
            return;
        }

        if (hasExtraLife)
        {
            hasExtraLife = false;
            if (extraLifeHalo != null) extraLifeHalo.SetActive(false);
            return;
        }

        Die();
    }

    public bool IsFacingTarget(Transform target)
    {
        if (target == null) return false;
        Vector2 myForward = GetForwardDirection();
        Vector2 toTarget = ((Vector2)target.position - (Vector2)transform.position).normalized;
        return Vector2.Dot(myForward, toTarget) > 0.5f;
    }

    private void Die()
    {
        if (!isAlive) return;
        isAlive = false;
        if (GameManager.Instance != null)
        {
            GameManager.Instance.NotifyRobotDestroyed(this);
        }
        Destroy(gameObject);
    }

    private void TryDash()
    {
        if (dashing || dashTimer > 0f) return;
        if (controller != null && controller.IsTurning) return;
        RobotCombat enemy = FindDashTarget();
        if (enemy == null) return;

        StartCoroutine(DashRoutine(enemy));
    }

    private IEnumerator DashRoutine(RobotCombat target)
    {
        dashing = true;
        dashTimer = dashCooldown;
        if (controller != null)
        {
            controller.SetSpeedMultiplier(dashMultiplier);
            controller.BeginStraightDash(GetForwardDirection());
        }

        float timer = 0f;
        while (timer < dashDuration && isAlive)
        {
            timer += Time.deltaTime;

            if (controller != null && controller.IsDashBlocked)
            {
                break;
            }

            if (target != null && target.IsAlive)
            {
                float dist = Vector2.Distance(transform.position, target.transform.position);
                if (dist <= 0.8f)
                {
                    target.TakeHit(this, DamageSource.Dash);
                    break;
                }
            }
            else
            {
                RobotCombat hit = FindDashContactTarget();
                if (hit != null)
                {
                    hit.TakeHit(this, DamageSource.Dash);
                    break;
                }
            }
            yield return null;
        }

        if (controller != null)
        {
            controller.SetSpeedMultiplier(1f);
            controller.EndStraightDash();
        }
        dashing = false;
    }

    private RobotCombat FindDashTarget()
    {
        Vector2 myForward = GetForwardDirection();
        if (myForward.sqrMagnitude <= 0.0001f) return null;

        // Dash checks only the straight front line (not plus/area scan).
        RaycastHit2D wallHit = Physics2D.Raycast(transform.position, myForward, dashCheckDistance, wallMask);
        float maxDistance = wallHit.collider != null ? wallHit.distance : dashCheckDistance;
        if (maxDistance <= 0.01f) return null;

        RaycastHit2D[] robotHits = Physics2D.RaycastAll(transform.position, myForward, maxDistance, robotMask);
        RobotCombat best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < robotHits.Length; i++)
        {
            if (robotHits[i].collider == null || robotHits[i].collider.gameObject == gameObject) continue;
            RobotCombat enemy = ResolveRobotCombat(robotHits[i].collider);
            if (enemy == null || !enemy.IsAlive) continue;
            float distance = robotHits[i].distance;
            if (distance <= 0.01f) continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = enemy;
            }
        }

        return best;
    }

    private RobotCombat FindDashContactTarget()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, 0.55f, robotMask);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null || hits[i].gameObject == gameObject) continue;
            RobotCombat enemy = hits[i].GetComponent<RobotCombat>();
            if (enemy == null || !enemy.IsAlive) continue;
            return enemy;
        }
        return null;
    }

    private void TryShoot()
    {
        if (shootTimer > 0f || bulletPrefab == null) return;
        Vector2 fireDir;
        if (!TryFindGridShootDirection(out fireDir))
        {
            gunnerReactionTimer = 0f;
            gunnerWaitingForShot = false;
            return;
        }

        if (!gunnerWaitingForShot)
        {
            gunnerWaitingForShot = true;
            gunnerReactionTimer = gunnerReactionDelay;
            return;
        }
        if (gunnerReactionTimer > 0f) return;

        // Always shoot from current grid center so bullets stay on lane center.
        Vector3 spawnPos = GetCurrentCellCenter();
        Quaternion bulletRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(fireDir.x, fireDir.y, 0f));
        GameObject bulletObj = Instantiate(bulletPrefab, spawnPos, bulletRotation);
        Bullet bullet = bulletObj.GetComponent<Bullet>();
        if (bullet != null) bullet.owner = this;
        shootTimer = shootCooldown;
        gunnerReactionTimer = 0f;
        gunnerWaitingForShot = false;
    }

    private bool TryFindGridShootDirection(out Vector2 fireDir)
    {
        fireDir = Vector2.zero;
        if (maze == null) return false;

        Vector2Int origin = maze.WorldToCell(transform.position);
        int maxSteps = Mathf.Max(1, Mathf.RoundToInt(shootCheckDistance / Mathf.Max(0.01f, maze.cellSize)));
        Vector2Int forwardDir = Vector2Int.up;
        if (controller != null)
        {
            Vector2 fwd = controller.ForwardDirection;
            forwardDir = new Vector2Int(Mathf.RoundToInt(fwd.x), Mathf.RoundToInt(fwd.y));
            if (forwardDir == Vector2Int.zero) forwardDir = Vector2Int.up;
        }
        Vector2Int leftDir = new Vector2Int(-forwardDir.y, forwardDir.x);
        Vector2Int rightDir = new Vector2Int(forwardDir.y, -forwardDir.x);
        Vector2Int[] dirs = { forwardDir, leftDir, rightDir };

        int bestStep = int.MaxValue;
        bool found = false;

        for (int d = 0; d < dirs.Length; d++)
        {
            for (int step = 1; step <= maxSteps; step++)
            {
                Vector2Int scan = origin + dirs[d] * step;
                if (!maze.IsWalkableCell(scan)) break;

                Vector3 world = maze.CellToWorld(scan);
                Collider2D[] robots = Physics2D.OverlapCircleAll(world, 0.35f, robotMask);
                for (int i = 0; i < robots.Length; i++)
                {
                    if (robots[i] == null || robots[i].gameObject == gameObject) continue;
                    RobotCombat enemy = robots[i].GetComponent<RobotCombat>();
                    if (enemy == null || !enemy.IsAlive) continue;

                    if (step < bestStep)
                    {
                        bestStep = step;
                        fireDir = new Vector2(dirs[d].x, dirs[d].y);
                        found = true;
                    }
                    break;
                }

                if (found && step == bestStep) break;
            }
        }

        return found;
    }

    private void TryDropBomb()
    {
        if (bombTimer > 0f || bombPrefab == null) return;

        if (!IsOnGridCenter()) return;
        Vector3 spawnPos = GetCurrentCellCenter();
        GameObject bombObj = Instantiate(bombPrefab, spawnPos, Quaternion.identity);
        Bomb bomb = bombObj.GetComponent<Bomb>();
        if (bomb != null)
        {
            bomb.owner = this;
            bomb.explosionRange = Mathf.Max(1, bombExplosionRange);
            bomb.effectLifeTime = Mathf.Max(0.01f, bombEffectLifeTime);
        }
        bombTimer = bombInterval;
    }

    private bool IsOnGridCenter()
    {
        if (maze == null) return true;
        Vector2Int cell = maze.WorldToCell(transform.position);
        Vector3 center = maze.CellToWorld(cell);
        return Vector2.Distance(transform.position, center) <= 0.08f;
    }

    private Vector3 GetCurrentCellCenter()
    {
        if (maze == null) return transform.position;
        Vector2Int cell = maze.WorldToCell(transform.position);
        return maze.CellToWorld(cell);
    }

    private IEnumerator InvincibleRoutine(float duration)
    {
        invincible = true;
        if (bodyRenderer != null) bodyRenderer.color = Color.white;
        yield return new WaitForSeconds(duration);
        invincible = false;
        if (bodyRenderer != null) bodyRenderer.color = originalColor;
    }

    private void TryTankBurst()
    {
        if (tankBurstTimer > 0f) return;
        if (maze == null) return;

        Vector2Int myCell = maze.WorldToCell(transform.position);
        if (!HasEnemyAtDistanceOne(myCell)) return;

        StartCoroutine(TankBurstRoutine(myCell));
        tankBurstTimer = tankBurstCooldown;
    }

    private Vector2 GetForwardDirection()
    {
        if (controller != null) return controller.ForwardDirection.normalized;
        return transform.up;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Tank attack no longer relies on collision crush logic.
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        // Tank attack no longer relies on collision crush logic.
    }

    private bool HasEnemyAtDistanceOne(Vector2Int myCell)
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector2Int checkCell = myCell + dirs[i];
            if (!maze.IsWalkableCell(checkCell)) continue;
            Vector3 checkWorld = maze.CellToWorld(checkCell);
            Collider2D[] hits = Physics2D.OverlapCircleAll(checkWorld, tankBurstHitRadius, robotMask);
            for (int j = 0; j < hits.Length; j++)
            {
                RobotCombat other = ResolveRobotCombat(hits[j]);
                if (other == null || !other.IsAlive) continue;
                if (other.gameObject == gameObject) continue;
                return true;
            }
        }
        return false;
    }

    private IEnumerator TankBurstRoutine(Vector2Int myCell)
    {
        Vector2Int[] cells =
        {
            myCell,
            myCell + Vector2Int.up,
            myCell + Vector2Int.down,
            myCell + Vector2Int.left,
            myCell + Vector2Int.right
        };

        for (int i = 0; i < cells.Length; i++)
        {
            SpawnTankBurstEffect(cells[i]);
        }

        float activeTime = Mathf.Max(0.01f, tankBurstEffectLifeTime);
        float elapsed = 0f;
        while (elapsed < activeTime)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                DamageTankBurstCell(cells[i]);
            }
            elapsed += 0.05f;
            yield return new WaitForSeconds(0.05f);
        }
    }

    private void DamageTankBurstCell(Vector2Int cell)
    {
        if (maze != null && !maze.IsWalkableCell(cell)) return;
        Vector3 world = maze != null ? maze.CellToWorld(cell) : transform.position;
        Collider2D[] hits = Physics2D.OverlapCircleAll(world, tankBurstHitRadius, robotMask);
        for (int i = 0; i < hits.Length; i++)
        {
            RobotCombat other = ResolveRobotCombat(hits[i]);
            if (other == null || !other.IsAlive) continue;
            if (other.gameObject == gameObject) continue;
            other.TakeHit(this, DamageSource.TankRam);
        }
    }

    private void SpawnTankBurstEffect(Vector2Int cell)
    {
        if (maze != null && !maze.IsWalkableCell(cell)) return;
        if (tankBurstEffectPrefab == null) return;

        Vector3 world = maze != null ? maze.CellToWorld(cell) : transform.position;
        GameObject fx = Instantiate(tankBurstEffectPrefab, world, Quaternion.identity);
        Destroy(fx, Mathf.Max(0.01f, tankBurstEffectLifeTime));
    }

    private RobotCombat ResolveRobotCombat(Collider2D col)
    {
        if (col == null) return null;
        RobotCombat direct = col.GetComponent<RobotCombat>();
        if (direct != null) return direct;
        return col.GetComponentInParent<RobotCombat>();
    }
}
