using UnityEngine;

public enum MechaClass
{
    Sword,
    ShieldKnife,
    Gunner,
    Bomber
}

[RequireComponent(typeof(SteeringMovementController))]
[RequireComponent(typeof(Rigidbody2D))]
public class MechaCombatController : MonoBehaviour
{
    [Header("Identity")]
    public string mechaName = "Mecha";
    public MechaClass mechaClass;
    public float maxHp = 100f;
    public float attackDamage = 20f;
    public float attackRange = 3.5f;
    public float attackCooldown = 1.2f;
    public bool IsAlive => isAlive;
    public Vector2 FacingDirection => facingDirection;

    [Header("Per-Class Attack Cooldown")]
    public float swordAttackCooldown = 1.2f;
    public float shieldKnifeAttackCooldown = 1.2f;
    public float gunnerAttackCooldown = 0.75f;
    public float bomberAttackCooldown = 1.8f;

    [Header("Melee")]
    public float meleeAttackRange = 1.25f;
    public float meleePush = 0f;
    public float shieldFrontDamageMultiplier = 0.5f;
    [Range(-1f, 1f)] public float shieldFrontDotThreshold = 0.25f;
    public GameObject swordSlashEffectPrefab;
    public GameObject knifeAttackEffectPrefab;

    [Header("Gunner")]
    public float gunnerReactionDelay = 0.5f;
    public GameObject projectilePrefab;
    public Transform projectileSpawnPoint;
    public float projectilePush = 0f;
    [HideInInspector] public int gunnerProjectileCount = 1;
    [Range(0f, 180f)] public float gunnerConeAngle = 120f;

    [Header("Bomber")]
    public GameObject bombProjectilePrefab;
    public float bombTravelSpeed = 4.5f;
    public float bombFuseTime = 0.8f;
    public float bombThrowRange = 6f;
    public float bombExplosionRadius = 1.8f;
    public float bomberAoeRadius = 1.8f;
    public float bomberPush = 4f;
    public GameObject bombExplosionEffectPrefab;

    [Header("Feedback")]
    public Animator animator;
    [Header("Attack Movement Slow")]
    public bool enableAttackMoveSlow = false;
    [Range(0.1f, 1f)] public float meleeAttackMoveMultiplier = 0.35f;
    [Range(0.1f, 1f)] public float rangedAttackMoveMultiplier = 0.55f;
    public float attackMoveSlowDuration = 0.16f;

    private SteeringMovementController movement;
    private Rigidbody2D rb;
    private SpriteRenderer selfRenderer;
    private float hp;
    private float cooldownTimer;
    private float reactionTimer;
    private bool waitingReaction;
    private bool isAlive = true;
    private Vector2 facingDirection = Vector2.right;
    private float attackSlowTimer;
    private float meleeFacingLogCooldown;

    private void Awake()
    {
        movement = GetComponent<SteeringMovementController>();
        rb = GetComponent<Rigidbody2D>();
        selfRenderer = GetComponent<SpriteRenderer>();
        hp = maxHp;
        EnsureYSorting();
    }

    private void Start()
    {
        GameManager.Instance?.RegisterMecha(this);
    }

    private void Update()
    {
        cooldownTimer -= Time.deltaTime;
        reactionTimer -= Time.deltaTime;
        attackSlowTimer -= Time.deltaTime;
        meleeFacingLogCooldown -= Time.deltaTime;
        if (movement != null)
        {
            if (enableAttackMoveSlow && attackSlowTimer > 0f)
            {
                float mult = (mechaClass == MechaClass.Sword || mechaClass == MechaClass.ShieldKnife)
                    ? meleeAttackMoveMultiplier
                    : rangedAttackMoveMultiplier;
                movement.SetSpeedMultiplier(mult);
            }
            else
            {
                movement.SetSpeedMultiplier(1f);
            }
        }

        if (movement != null)
        {
            Vector2 velocity = movement.Velocity;
            if (velocity.sqrMagnitude > 0.01f)
            {
                bool isMeleeClass = mechaClass == MechaClass.Sword || mechaClass == MechaClass.ShieldKnife;
                if (isMeleeClass && cooldownTimer > 0f && meleeFacingLogCooldown <= 0f)
                {
                    meleeFacingLogCooldown = 0.2f;
                    #region agent log
                    AgentDebugLogger.Log(
                        "H31",
                        "MechaCombatController.cs:Update",
                        "Melee facing updated from velocity during cooldown",
                        "{\"name\":\"" + gameObject.name + "\",\"cooldownTimer\":" + cooldownTimer.ToString("0.###") + ",\"velX\":" + velocity.x.ToString("0.###") + ",\"velY\":" + velocity.y.ToString("0.###") + ",\"oldFaceX\":" + facingDirection.x.ToString("0.###") + ",\"oldFaceY\":" + facingDirection.y.ToString("0.###") + "}"
                    );
                    #endregion
                }
                facingDirection = velocity.normalized;
            }
        }
    }

    public bool TryAttack(MechaCombatController target)
    {
        if (!isAlive || target == null || !target.IsAlive) return false;
        if (cooldownTimer > 0f) return false;

        float distance = Vector2.Distance(transform.position, target.transform.position);
        bool isMelee = mechaClass == MechaClass.Sword || mechaClass == MechaClass.ShieldKnife;
        float requiredRange = isMelee ? Mathf.Min(meleeAttackRange, attackRange) : attackRange;
        if (distance > requiredRange) return false;
        if (mechaClass == MechaClass.Gunner)
        {
            if (!waitingReaction)
            {
                waitingReaction = true;
                reactionTimer = gunnerReactionDelay;
                return false;
            }
            if (reactionTimer > 0f) return false;
            waitingReaction = false;
        }
        else
        {
            waitingReaction = false;
        }

        #region agent log
        AgentDebugLogger.Log(
            "H4",
            "MechaCombatController.cs:TryAttack",
            "Attack confirmed after prechecks",
            "{\"attacker\":\"" + gameObject.name + "\",\"target\":\"" + target.gameObject.name + "\",\"class\":\"" + mechaClass + "\",\"distance\":" + distance.ToString("0.###") + ",\"requiredRange\":" + requiredRange.ToString("0.###") + "}"
        );
        #endregion

        Vector2 toTarget = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            facingDirection = toTarget;
        }

        if (mechaClass == MechaClass.Sword)
        {
            PerformMeleeStrike(target, swordSlashEffectPrefab, meleePush);
        }
        else if (mechaClass == MechaClass.ShieldKnife)
        {
            PerformMeleeStrike(target, knifeAttackEffectPrefab, meleePush);
        }
        else if (mechaClass == MechaClass.Gunner)
        {
            ShootAtTarget(target);
        }
        else if (mechaClass == MechaClass.Bomber)
        {
            ThrowBomb(target);
        }

        if (enableAttackMoveSlow)
        {
            attackSlowTimer = Mathf.Max(0.01f, attackMoveSlowDuration);
        }
        float appliedCooldown = GetAttackCooldownByClass();
        cooldownTimer = appliedCooldown;
        #region agent log
        AgentDebugLogger.Log(
            "H25",
            "MechaCombatController.cs:TryAttack",
            "Attack cooldown applied",
            "{\"attacker\":\"" + gameObject.name + "\",\"class\":\"" + mechaClass + "\",\"cooldown\":" + appliedCooldown.ToString("0.###") + "}"
        );
        #endregion
        return true;
    }

    private float GetAttackCooldownByClass()
    {
        switch (mechaClass)
        {
            case MechaClass.Sword:
                return Mathf.Max(0.05f, swordAttackCooldown);
            case MechaClass.ShieldKnife:
                return Mathf.Max(0.05f, shieldKnifeAttackCooldown);
            case MechaClass.Gunner:
                return Mathf.Max(0.05f, gunnerAttackCooldown);
            case MechaClass.Bomber:
                return Mathf.Max(0.05f, bomberAttackCooldown);
            default:
                return Mathf.Max(0.05f, attackCooldown);
        }
    }

    public void TakeDamage(float damage, MechaCombatController source, Vector2 pushForce)
    {
        if (!isAlive) return;

        if (mechaClass == MechaClass.ShieldKnife && source != null)
        {
            Vector2 incoming = ((Vector2)source.transform.position - (Vector2)transform.position).normalized;
            float frontDot = Vector2.Dot(facingDirection.normalized, incoming);
            if (frontDot >= shieldFrontDotThreshold)
            {
                damage *= Mathf.Clamp01(shieldFrontDamageMultiplier);
            }
        }

        hp -= damage;
        #region agent log
        AgentDebugLogger.Log(
            "H5",
            "MechaCombatController.cs:TakeDamage",
            "Damage applied",
            "{\"target\":\"" + gameObject.name + "\",\"source\":\"" + (source != null ? source.gameObject.name : "null") + "\",\"damage\":" + damage.ToString("0.###") + ",\"hpAfter\":" + hp.ToString("0.###") + "}"
        );
        #endregion
        bool allowPush = source != null && source.mechaClass == MechaClass.Bomber;
        if (allowPush && pushForce.sqrMagnitude > 0.0001f)
        {
            rb.AddForce(pushForce, ForceMode2D.Impulse);
        }
        #region agent log
        AgentDebugLogger.Log(
            "H10",
            "MechaCombatController.cs:TakeDamage",
            "Push evaluation",
            "{\"target\":\"" + gameObject.name + "\",\"sourceClass\":\"" + (source != null ? source.mechaClass.ToString() : "null") + "\",\"allowPush\":" + (allowPush ? "true" : "false") + ",\"pushMagnitude\":" + pushForce.magnitude.ToString("0.###") + "}"
        );
        #endregion
        if (hp <= 0f)
        {
            isAlive = false;
            #region agent log
            AgentDebugLogger.Log(
                "H5",
                "MechaCombatController.cs:TakeDamage",
                "Mecha destroyed",
                "{\"target\":\"" + gameObject.name + "\"}"
            );
            #endregion
            GameManager.Instance?.NotifyMechaDestroyed(this);
            Destroy(gameObject);
        }
    }

    private void PerformMeleeStrike(MechaCombatController target, GameObject effectPrefab, float push)
    {
        if (target == null || !target.IsAlive) return;

        Vector2 dir = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
        if (dir.sqrMagnitude > 0.0001f) facingDirection = dir;
        target.TakeDamage(attackDamage, this, dir * push);
        SpawnDirectionalEffect(effectPrefab, dir, 0.7f);
    }

    private void ShootAtTarget(MechaCombatController target)
    {
        if (projectilePrefab == null)
        {
            #region agent log
            AgentDebugLogger.Log(
                "H9",
                "MechaCombatController.cs:ShootCone",
                "Projectile prefab missing",
                "{\"attacker\":\"" + gameObject.name + "\"}"
            );
            #endregion
            return;
        }
        if (target == null) return;

        int count = 1; // Gunner tekli atis yapar.
        Vector2 baseDir = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
        if (baseDir.sqrMagnitude <= 0.0001f)
        {
            baseDir = facingDirection.sqrMagnitude > 0.001f ? facingDirection.normalized : Vector2.right;
        }
        Vector3 spawn = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;

        for (int i = 0; i < count; i++)
        {
            Vector2 dir = baseDir;
            // Projectile sprite front faces right, so align local +X to shot direction.
            Quaternion rot = Quaternion.FromToRotation(Vector3.right, new Vector3(dir.x, dir.y, 0f));
            GameObject go = Instantiate(projectilePrefab, spawn, rot);
            MatchSorting(go);
            MechaProjectile p = go.GetComponent<MechaProjectile>();
            if (p == null)
            {
                p = go.AddComponent<MechaProjectile>();
                #region agent log
                AgentDebugLogger.Log(
                    "H13",
                    "MechaCombatController.cs:ShootCone",
                    "Projectile component auto-added",
                    "{\"attacker\":\"" + gameObject.name + "\",\"projectileName\":\"" + go.name + "\"}"
                );
                #endregion
            }
            if (p != null)
            {
                p.Setup(this, attackDamage, projectilePush);
            }
        }
        #region agent log
        AgentDebugLogger.Log(
            "H13",
            "MechaCombatController.cs:ShootAtTarget",
            "Gunner fired projectiles",
            "{\"attacker\":\"" + gameObject.name + "\",\"count\":" + count + ",\"target\":\"" + target.gameObject.name + "\"}"
        );
        #endregion
    }

    private void ThrowBomb(MechaCombatController target)
    {
        if (bombProjectilePrefab == null || target == null)
        {
            #region agent log
            AgentDebugLogger.Log(
                "H9",
                "MechaCombatController.cs:ThrowBomb",
                "Bomb setup skipped",
                "{\"attacker\":\"" + gameObject.name + "\",\"bombPrefabMissing\":" + (bombProjectilePrefab == null ? "true" : "false") + ",\"targetMissing\":" + (target == null ? "true" : "false") + "}"
            );
            #endregion
            return;
        }

        Vector2 toTarget = (Vector2)target.transform.position - (Vector2)transform.position;
        Vector2 throwDir = toTarget.normalized;
        float clampedDistance = Mathf.Clamp(toTarget.magnitude, 1f, bombThrowRange);
        Vector2 targetPoint = (Vector2)transform.position + throwDir * clampedDistance;

        Vector3 spawn = projectileSpawnPoint != null ? projectileSpawnPoint.position : transform.position;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, new Vector3(throwDir.x, throwDir.y, 0f));
        GameObject go = Instantiate(bombProjectilePrefab, spawn, rot);
        MatchSorting(go);

        MechaBombProjectile bomb = go.GetComponent<MechaBombProjectile>();
        if (bomb != null)
        {
            bomb.Setup(
                this,
                targetPoint,
                attackDamage,
                bomberPush,
                bomberAoeRadius > 0f ? bomberAoeRadius : bombExplosionRadius,
                bombTravelSpeed,
                bombFuseTime,
                bombExplosionEffectPrefab
            );
        }
    }

    private void SpawnDirectionalEffect(GameObject effectPrefab, Vector2 direction, float distance)
    {
        if (effectPrefab == null) return;

        Vector2 dir = direction.sqrMagnitude > 0.001f ? direction.normalized : facingDirection;
        Vector3 pos = transform.position + (Vector3)(dir * distance);
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, new Vector3(dir.x, dir.y, 0f));
        float rawAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float snappedAngle = Mathf.Repeat(Mathf.Round(rawAngle / 45f) * 45f, 360f);
        #region agent log
        AgentDebugLogger.Log(
            "H30",
            "MechaCombatController.cs:SpawnDirectionalEffect",
            "Effect spawn direction",
            "{\"owner\":\"" + gameObject.name + "\",\"effectPrefab\":\"" + effectPrefab.name + "\",\"dirX\":" + dir.x.ToString("0.###") + ",\"dirY\":" + dir.y.ToString("0.###") + ",\"rawAngle\":" + rawAngle.ToString("0.###") + ",\"snapped45\":" + snappedAngle.ToString("0.###") + ",\"spawnRotZ\":" + rot.eulerAngles.z.ToString("0.###") + "}"
        );
        #endregion
        GameObject fx = Instantiate(effectPrefab, pos, rot);
        MatchSorting(fx);

        DirectionalOneShotEffect oneShot = fx.GetComponent<DirectionalOneShotEffect>();
        if (oneShot != null)
        {
            oneShot.Play(dir, selfRenderer);
        }
    }

    private Vector2 Rotate(Vector2 v, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float sin = Mathf.Sin(rad);
        float cos = Mathf.Cos(rad);
        return new Vector2(cos * v.x - sin * v.y, sin * v.x + cos * v.y).normalized;
    }

    private void MatchSorting(GameObject go)
    {
        if (go == null) return;
        SpriteRenderer spawnedRenderer = go.GetComponent<SpriteRenderer>();
        if (spawnedRenderer == null) return;
        if (selfRenderer == null) return;

        spawnedRenderer.sortingLayerID = selfRenderer.sortingLayerID;
        spawnedRenderer.sortingOrder = selfRenderer.sortingOrder + 1;
    }

    private void EnsureYSorting()
    {
        if (GetComponent<YSortSpriteOrder>() == null)
        {
            gameObject.AddComponent<YSortSpriteOrder>();
        }
    }
}
