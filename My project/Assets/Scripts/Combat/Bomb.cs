using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Bomb : MonoBehaviour
{
    public RobotCombat owner;
    public float fuseTime = 2f;
    public int explosionRange = 2;
    public LayerMask wallMask;
    public LayerMask robotMask;
    public GameObject explosionEffectPrefab;
    public float effectLifeTime = 0.2f;

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
        List<Vector2> burstCells = new List<Vector2>();
        burstCells.Add(center);
        SpawnExplosionEffect(center);

        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        for (int d = 0; d < dirs.Length; d++)
        {
            for (int i = 1; i <= explosionRange; i++)
            {
                Vector2 point = center + dirs[d] * i;
                if (Physics2D.OverlapCircle(point, 0.15f, wallMask) != null) break;
                burstCells.Add(point);
                SpawnExplosionEffect(point);
            }
        }

        StartCoroutine(DamageWindowRoutine(burstCells));
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

    private void SpawnExplosionEffect(Vector2 point)
    {
        if (explosionEffectPrefab == null) return;
        GameObject fx = Instantiate(explosionEffectPrefab, point, Quaternion.identity);
        if (effectLifeTime > 0f)
        {
            Destroy(fx, effectLifeTime);
        }
    }

    private IEnumerator DamageWindowRoutine(List<Vector2> points)
    {
        float activeTime = Mathf.Max(0.01f, effectLifeTime);
        float elapsed = 0f;

        while (elapsed < activeTime)
        {
            for (int i = 0; i < points.Count; i++)
            {
                DamageAtPoint(points[i]);
            }
            elapsed += 0.05f;
            yield return new WaitForSeconds(0.05f);
        }

        Destroy(gameObject);
    }
}
