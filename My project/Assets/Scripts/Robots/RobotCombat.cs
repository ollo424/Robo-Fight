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

    [Header("Dasher")]
    public float dashCheckDistance = 2f;
    public float dashMultiplier = 3f;
    public float dashDuration = 0.5f;
    public float dashCooldown = 1.5f;

    [Header("Gunner")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float shootCheckDistance = 4f;
    public float shootCooldown = 2f;

    [Header("Bomber")]
    public GameObject bombPrefab;
    public float bombInterval = 3f;

    [Header("Tank")]
    public float tankScale = 1.2f;
    public float shieldRegenTime = 5f;

    private RobotController controller;
    private bool isAlive = true;
    private bool invincible;
    private bool hasExtraLife;
    private bool hasShield;

    private float dashTimer;
    private float shootTimer;
    private float bombTimer;
    private float shieldTimer;
    private bool dashing;
    private Color originalColor;

    private void Awake()
    {
        controller = GetComponent<RobotController>();
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
            hasShield = true;
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
        shieldTimer -= Time.deltaTime;

        if (robotType == RobotType.Dasher) TryDash();
        if (robotType == RobotType.Gunner) TryShoot();
        if (robotType == RobotType.Bomber) TryDropBomb();
        if (robotType == RobotType.Tank) HandleTankShieldRegen();
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

        if (robotType == RobotType.Tank && hasShield && source == DamageSource.Bullet)
        {
            hasShield = false;
            shieldTimer = shieldRegenTime;
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
        Vector2 toTarget = ((Vector2)target.position - (Vector2)transform.position).normalized;
        return Vector2.Dot(transform.up, toTarget) > 0.5f;
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
        RaycastHit2D hit = Physics2D.Raycast(transform.position, transform.up, dashCheckDistance, robotMask);
        if (hit.collider == null || hit.collider.gameObject == gameObject) return;

        RobotCombat enemy = hit.collider.GetComponent<RobotCombat>();
        if (enemy == null || !enemy.IsAlive) return;

        StartCoroutine(DashRoutine(enemy));
    }

    private IEnumerator DashRoutine(RobotCombat target)
    {
        dashing = true;
        dashTimer = dashCooldown;
        if (controller != null) controller.SetSpeedMultiplier(dashMultiplier);

        float timer = 0f;
        while (timer < dashDuration && isAlive)
        {
            timer += Time.deltaTime;
            if (target != null && target.IsAlive)
            {
                float dist = Vector2.Distance(transform.position, target.transform.position);
                if (dist <= 0.8f)
                {
                    target.TakeHit(this, DamageSource.Dash);
                    break;
                }
            }
            yield return null;
        }

        if (controller != null) controller.SetSpeedMultiplier(1f);
        dashing = false;
    }

    private void TryShoot()
    {
        if (shootTimer > 0f || bulletPrefab == null) return;
        RaycastHit2D hit = Physics2D.Raycast(transform.position, transform.up, shootCheckDistance, robotMask);
        if (hit.collider == null || hit.collider.gameObject == gameObject) return;

        RobotCombat enemy = hit.collider.GetComponent<RobotCombat>();
        if (enemy == null || !enemy.IsAlive) return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position + transform.up * 0.6f;
        GameObject bulletObj = Instantiate(bulletPrefab, spawnPos, transform.rotation);
        Bullet bullet = bulletObj.GetComponent<Bullet>();
        if (bullet != null) bullet.owner = this;
        shootTimer = shootCooldown;
    }

    private void TryDropBomb()
    {
        if (bombTimer > 0f || bombPrefab == null) return;
        Vector3 spawnPos = transform.position - transform.up * 0.6f;
        GameObject bombObj = Instantiate(bombPrefab, spawnPos, Quaternion.identity);
        Bomb bomb = bombObj.GetComponent<Bomb>();
        if (bomb != null) bomb.owner = this;
        bombTimer = bombInterval;
    }

    private void HandleTankShieldRegen()
    {
        if (hasShield) return;
        if (shieldTimer > 0f) return;
        hasShield = true;
    }

    private IEnumerator InvincibleRoutine(float duration)
    {
        invincible = true;
        if (bodyRenderer != null) bodyRenderer.color = Color.white;
        yield return new WaitForSeconds(duration);
        invincible = false;
        if (bodyRenderer != null) bodyRenderer.color = originalColor;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isAlive) return;
        RobotCombat other = collision.gameObject.GetComponent<RobotCombat>();
        if (other == null || !other.IsAlive) return;

        if (robotType == RobotType.Tank && hasShield && IsFacingTarget(other.transform))
        {
            other.TakeHit(this, DamageSource.TankRam);
        }
    }
}
