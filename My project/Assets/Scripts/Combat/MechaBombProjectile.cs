using UnityEngine;
using System.Collections.Generic;

public class MechaBombProjectile : MonoBehaviour
{
    public LayerMask environmentMask;
    public float collisionExplodeDelay = 0.05f;

    private MechaCombatController owner;
    private Vector2 velocity;
    private float damage;
    private float pushForce;
    private float radius;
    private float fuseTimer;
    private GameObject explosionEffectPrefab;
    private bool exploded;

    public void Setup(
        MechaCombatController source,
        Vector2 targetPoint,
        float dmg,
        float push,
        float explosionRadius,
        float travelSpeed,
        float fuseTime,
        GameObject explosionPrefab)
    {
        owner = source;
        damage = dmg;
        pushForce = push;
        radius = explosionRadius;
        fuseTimer = Mathf.Max(0.05f, fuseTime);
        explosionEffectPrefab = explosionPrefab;

        Vector2 dir = (targetPoint - (Vector2)transform.position).normalized;
        velocity = dir * Mathf.Max(0f, travelSpeed);
        ApplyRotationFromDirection(dir);
    }

    private void Update()
    {
        if (exploded) return;

        Vector3 previous = transform.position;
        if (velocity.sqrMagnitude > 0.0001f)
        {
            ApplyRotationFromDirection(velocity.normalized);
        }

        transform.position += (Vector3)(velocity * Time.deltaTime);
        Vector3 current = transform.position;
        Vector2 delta = (Vector2)(current - previous);
        float distance = delta.magnitude;
        if (distance > 0.0001f)
        {
            RaycastHit2D[] wallHits = Physics2D.RaycastAll(previous, delta.normalized, distance, environmentMask);
            for (int i = 0; i < wallHits.Length; i++)
            {
                Collider2D c = wallHits[i].collider;
                if (c == null) continue;
                if (c.transform.root == transform.root) continue;
                if (owner != null && c.transform.root == owner.transform.root) continue;
                if (!c.CompareTag("Wall")) continue;

                #region agent log
                AgentDebugLogger.Log(
                    "H18",
                    "MechaBombProjectile.cs:Update",
                    "Bomb wall sweep hit",
                    "{\"wall\":\"" + c.gameObject.name + "\",\"distance\":" + wallHits[i].distance.ToString("0.###") + "}"
                );
                #endregion
                fuseTimer = Mathf.Min(fuseTimer, collisionExplodeDelay);
                break;
            }
        }
        fuseTimer -= Time.deltaTime;
        if (fuseTimer <= 0f)
        {
            Explode();
        }
    }

    private void ApplyRotationFromDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (exploded || other == null) return;
        if (owner != null && other.transform.root == owner.transform.root) return;

        bool hitEnvironment = ((1 << other.gameObject.layer) & environmentMask) != 0;
        MechaCombatController hitMecha = other.GetComponentInParent<MechaCombatController>();
        if (hitEnvironment || hitMecha != null)
        {
            fuseTimer = Mathf.Min(fuseTimer, collisionExplodeDelay);
        }
    }

    private void Explode()
    {
        if (exploded) return;
        exploded = true;

        if (explosionEffectPrefab != null)
        {
            GameObject fx = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            DirectionalOneShotEffect directional = fx.GetComponent<DirectionalOneShotEffect>();
            if (directional != null)
            {
                SpriteRenderer ownerRenderer = owner != null ? owner.GetComponent<SpriteRenderer>() : null;
                directional.Play(Vector2.up, ownerRenderer);
            }
        }

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius);
        HashSet<MechaCombatController> damagedTargets = new HashSet<MechaCombatController>();
        for (int i = 0; i < hits.Length; i++)
        {
            MechaCombatController target = hits[i].GetComponentInParent<MechaCombatController>();
            if (target == null || !target.IsAlive) continue;
            if (owner != null && target == owner) continue;
            if (!damagedTargets.Add(target)) continue;

            Vector2 dir = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
            target.TakeDamage(damage, owner, dir * pushForce);
        }

        Destroy(gameObject);
    }
}
