using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [System.Serializable]
    public class RobotSpawnEntry
    {
        public string displayName;
        public GameObject prefab;
        public Color color = Color.white;
    }

    [Header("References")]
    public MazeGenerator mazeGenerator;
    public List<RobotSpawnEntry> robotEntries = new List<RobotSpawnEntry>();
    public TMP_Text winText;

    [Header("Loop")]
    public float restartDelay = 3f;

    private readonly List<RobotCombat> aliveRobots = new List<RobotCombat>();
    private bool gameEnded;

    private void Awake()
    {
        Instance = this;
        if (winText != null) winText.gameObject.SetActive(false);
    }

    private IEnumerator Start()
    {
        if (mazeGenerator != null)
        {
            mazeGenerator.GenerateMaze();
            FitCameraToGrid();
        }

        yield return null;
        SpawnAllRobots();
    }

    public void RegisterRobot(RobotCombat robot)
    {
        if (robot == null || aliveRobots.Contains(robot)) return;
        aliveRobots.Add(robot);
    }

    public void NotifyRobotDestroyed(RobotCombat robot)
    {
        if (robot != null) aliveRobots.Remove(robot);
        if (!gameEnded) CheckWinCondition();
    }

    private void SpawnAllRobots()
    {
        if (mazeGenerator == null) return;

        List<Vector3> spawnPoints = mazeGenerator.GetSpawnPoints();
        int count = Mathf.Min(robotEntries.Count, spawnPoints.Count);

        for (int i = 0; i < count; i++)
        {
            RobotSpawnEntry entry = robotEntries[i];
            if (entry == null || entry.prefab == null) continue;

            GameObject robotObj = Instantiate(entry.prefab, spawnPoints[i], Quaternion.identity);
            robotObj.name = string.IsNullOrWhiteSpace(entry.displayName) ? "Robot_" + i : entry.displayName;

            RobotCombat combat = robotObj.GetComponent<RobotCombat>();
            if (combat != null)
            {
                combat.displayName = robotObj.name;
                combat.baseColor = entry.color;
            }
        }
    }

    private void CheckWinCondition()
    {
        aliveRobots.RemoveAll(r => r == null);
        if (aliveRobots.Count != 1) return;

        gameEnded = true;
        RobotCombat winner = aliveRobots[0];
        if (winText != null)
        {
            winText.gameObject.SetActive(true);
            winText.text = winner.displayName + " KAZANDI";
            winText.color = winner.baseColor;
        }

        StartCoroutine(RestartSceneRoutine());
    }

    private IEnumerator RestartSceneRoutine()
    {
        yield return new WaitForSeconds(restartDelay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void FitCameraToGrid()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic || mazeGenerator == null) return;

        float width = mazeGenerator.gridSizeX * mazeGenerator.cellSize;
        float height = mazeGenerator.gridSizeY * mazeGenerator.cellSize;
        Vector3 center = mazeGenerator.transform.position + new Vector3((width - mazeGenerator.cellSize) * 0.5f, (height - mazeGenerator.cellSize) * 0.5f, 0f);

        cam.transform.position = new Vector3(center.x, center.y, cam.transform.position.z);
        float vertical = height * 0.6f;
        float horizontal = (width * 0.6f) / Mathf.Max(0.01f, cam.aspect);
        cam.orthographicSize = Mathf.Max(vertical, horizontal, 5f);
    }
}
