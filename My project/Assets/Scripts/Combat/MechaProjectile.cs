using UnityEngine;

public class MechaProjectile : MonoBehaviour
{
    public float speed = 8f;
    public float lifeTime = 2f;
    public float damage = 15f;
    public float pushForce = 2f;
    public LayerMask hitMask;
    public LayerMask wallMask = Physics2D.AllLayers;

    private MechaCombatController owner;
    private float timer;
    private bool loggedInit;

    public void Setup(MechaCombatController source, float dmg, float push)
    {
        owner = source;
        damage = dmg;
        pushForce = push;
        if (speed <= 0.01f) speed = 8f;
        if (lifeTime <= 0.05f) lifeTime = 2f;
        #region agent log
        AgentDebugLogger.Log(
            "H13",
            "MechaProjectile.cs:Setup",
            "Projectile initialized",
            "{\"owner\":\"" + (owner != null ? owner.gameObject.name : "null") + "\",\"speed\":" + speed.ToString("0.###") + ",\"lifeTime\":" + lifeTime.ToString("0.###") + ",\"dirX\":" + transform.right.x.ToString("0.###") + ",\"dirY\":" + transform.right.y.ToString("0.###") + "}"
        );
        #endregion
        loggedInit = true;
    }

    private void Update()
    {
        Vector3 previous = transform.position;
        timer += Time.deltaTime;
        if (timer >= lifeTime)
        {
            Destroy(gameObject);
            return;
        }
        transform.position += transform.right * (speed * Time.deltaTime);
        Vector3 current = transform.position;
        Vector2 delta = (Vector2)(current - previous);
        float distance = delta.magnitude;
        if (distance > 0.0001f)
        {
            RaycastHit2D[] wallHits = Physics2D.RaycastAll(previous, delta.normalized, distance, wallMask);
            for (int i = 0; i < wallHits.Length; i++)
            {
                Collider2D c = wallHits[i].collider;
                if (c == null) continue;
                if (c.transform.root == transform.root) continue;
                if (!c.CompareTag("Wall")) continue;

                #region agent log
                AgentDebugLogger.Log(
                    "H17",
                    "MechaProjectile.cs:Update",
                    "Projectile wall sweep hit",
                    "{\"wall\":\"" + c.gameObject.name + "\",\"distance\":" + wallHits[i].distance.ToString("0.###") + "}"
                );
                #endregion
                Destroy(gameObject);
                return;
            }
        }
        if (!loggedInit)
        {
            loggedInit = true;
            #region agent log
            AgentDebugLogger.Log(
                "H13",
                "MechaProjectile.cs:Update",
                "Projectile auto-init fallback",
                "{\"speed\":" + speed.ToString("0.###") + ",\"lifeTime\":" + lifeTime.ToString("0.###") + "}"
            );
            #endregion
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null) return;
        if (owner != null && other.transform.root == owner.transform.root) return;

        MechaCombatController target = other.GetComponentInParent<MechaCombatController>();
        if (target != null && target.IsAlive)
        {
            Vector2 pushDir = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
            target.TakeDamage(damage, owner, pushDir * pushForce);
            Destroy(gameObject);
            return;
        }

        if (((1 << other.gameObject.layer) & hitMask) != 0)
        {
            Destroy(gameObject);
        }
    }
}
