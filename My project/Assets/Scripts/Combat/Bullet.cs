using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float speed = 6f;
    public float lifeTime = 2f;
    public LayerMask wallMask;
    public LayerMask robotMask;
    public RobotCombat owner;

    private float timer;
    private Vector3 previousPosition;

    private void Start()
    {
        previousPosition = transform.position;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= lifeTime)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 nextPosition = transform.position + transform.up * (speed * Time.deltaTime);
        ResolveHitsBetween(previousPosition, nextPosition);
        transform.position = nextPosition;
        previousPosition = transform.position;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHandleColliderHit(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision == null) return;
        TryHandleColliderHit(collision.collider);
    }

    private void ResolveHitsBetween(Vector3 from, Vector3 to)
    {
        Vector2 delta = (Vector2)(to - from);
        float dist = delta.magnitude;
        if (dist <= 0.0001f) return;

        RaycastHit2D[] robotHits = Physics2D.RaycastAll(from, delta.normalized, dist, robotMask);
        for (int i = 0; i < robotHits.Length; i++)
        {
            if (robotHits[i].collider == null) continue;
            if (TryHandleColliderHit(robotHits[i].collider)) return;
        }

        RaycastHit2D wallHit = Physics2D.Raycast(from, delta.normalized, dist, wallMask);
        if (wallHit.collider != null)
        {
            Destroy(gameObject);
        }
    }

    private bool TryHandleColliderHit(Collider2D other)
    {
        if (other == null) return false;
        if (owner != null && other.gameObject == owner.gameObject) return false;

        RobotCombat target = other.GetComponent<RobotCombat>();
        if (target != null)
        {
            target.TakeHit(owner, DamageSource.Bullet);
            Destroy(gameObject);
            return true;
        }

        if (((1 << other.gameObject.layer) & wallMask) != 0)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
