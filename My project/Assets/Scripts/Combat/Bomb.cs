using UnityEngine;

public class Bomb : MonoBehaviour
{
    public RobotCombat owner;
    public float fuseTime = 2f;
    public int explosionRange = 3;
    public LayerMask wallMask;
    public LayerMask robotMask;

    private float timer;
    private bool exploded;

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= fuseTime && !exploded)
        {
            Explode();
        }
    }

    private void Explode()
    {
        exploded = true;
        Vector2 center = transform.position;
        DamageAtPoint(center);

        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        for (int d = 0; d < dirs.Length; d++)
        {
            for (int i = 1; i <= explosionRange; i++)
            {
                Vector2 point = center + dirs[d] * i;
                if (Physics2D.OverlapCircle(point, 0.15f, wallMask) != null) break;
                DamageAtPoint(point);
            }
        }

        Destroy(gameObject);
    }

    private void DamageAtPoint(Vector2 point)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(point, 0.35f, robotMask);
        for (int i = 0; i < hits.Length; i++)
        {
            RobotCombat rc = hits[i].GetComponent<RobotCombat>();
            if (rc == null || !rc.IsAlive) continue;
            if (owner != null && rc.gameObject == owner.gameObject) continue;
            rc.TakeHit(owner, DamageSource.Bomb);
        }
    }
}
