using UnityEngine;

public class PowerUpSpawner : MonoBehaviour
{
    public MazeGenerator mazeGenerator;
    public float spawnInterval = 10f;

    [Header("Prefabs")]
    public GameObject invinciblePrefab;
    public GameObject speedPrefab;
    public GameObject extraLifePrefab;

    private float timer;

    private void Update()
    {
        if (mazeGenerator == null) return;
        timer += Time.deltaTime;
        if (timer < spawnInterval) return;
        timer = 0f;

        SpawnRandomPowerUp();
    }

    private void SpawnRandomPowerUp()
    {
        GameObject prefab = GetRandomPrefab();
        if (prefab == null) return;

        Vector3 position = mazeGenerator.GetRandomWalkableWorldPosition();
        Instantiate(prefab, position, Quaternion.identity);
    }

    private GameObject GetRandomPrefab()
    {
        int pick = Random.Range(0, 3);
        if (pick == 0) return invinciblePrefab;
        if (pick == 1) return speedPrefab;
        return extraLifePrefab;
    }
}
