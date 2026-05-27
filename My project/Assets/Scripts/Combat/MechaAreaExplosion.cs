using System.Collections;
using UnityEngine;

public class MechaAreaExplosion : MonoBehaviour
{
    public float radius = 1.6f;
    public float duration = 0.2f;
    public float damage = 20f;
    public float pushForce = 3f;
    public LayerMask mechaMask;
    public MechaCombatController owner;

    public void Trigger()
    {
        StartCoroutine(DamageRoutine());
    }

    private IEnumerator DamageRoutine()
    {
        float timer = 0f;
        while (timer < duration)
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius, mechaMask);
            for (int i = 0; i < hits.Length; i++)
            {
                MechaCombatController target = hits[i].GetComponentInParent<MechaCombatController>();
                if (target == null || !target.IsAlive) continue;
                if (owner != null && target == owner) continue;

                Vector2 dir = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
                target.TakeDamage(damage, owner, dir * pushForce);
            }

            timer += 0.05f;
            yield return new WaitForSeconds(0.05f);
        }

        Destroy(gameObject);
    }
}
