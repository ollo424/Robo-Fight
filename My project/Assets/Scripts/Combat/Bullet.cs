using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float speed = 6f;
    public float lifeTime = 2f;
    public LayerMask wallMask;
    public RobotCombat owner;

    private float timer;

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= lifeTime)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += transform.up * (speed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (owner != null && other.gameObject == owner.gameObject) return;

        RobotCombat target = other.GetComponent<RobotCombat>();
        if (target != null)
        {
            target.TakeHit(owner, DamageSource.Bullet);
            Destroy(gameObject);
            return;
        }

        if (((1 << other.gameObject.layer) & wallMask) != 0)
        {
            Destroy(gameObject);
        }
    }
}
