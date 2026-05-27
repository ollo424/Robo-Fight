using System.Collections.Generic;
using UnityEngine;

public class IsometricArenaController : MonoBehaviour
{
    public static IsometricArenaController Instance { get; private set; }
    public RoadGenerator roadGenerator;
    public Camera arenaCamera;
    public float cameraSize = 8f;
    public Vector3 cameraOffset = new Vector3(0f, 0f, -10f);
    [Min(0f)] public float minSpawnDistance = 2.5f;

    public void BuildArena()
    {
        if (roadGenerator != null)
        {
            roadGenerator.GenerateRoads();
        }
        SetupCamera();
    }

    private void Awake()
    {
        Instance = this;
    }

    public List<Vector3> GetSpawnPositions(int count)
    {
        List<Vector3> points = new List<Vector3>();
        if (roadGenerator == null) return points;

        List<Vector3> cornerSpawns = roadGenerator.GetInnerCornerSpawnPoints();
        if (cornerSpawns.Count > 0)
        {
            int requiredCornerCount = Mathf.Min(count, cornerSpawns.Count);
            for (int i = 0; i < requiredCornerCount; i++)
            {
                points.Add(cornerSpawns[i]);
                #region agent log
                AgentDebugLogger.Log(
                    "H33",
                    "IsometricArenaController.cs:GetSpawnPositions",
                    "Forced inner-corner spawn",
                    "{\"index\":" + i + ",\"x\":" + cornerSpawns[i].x.ToString("0.###") + ",\"y\":" + cornerSpawns[i].y.ToString("0.###") + "}"
                );
                #endregion
            }
        }
        if (points.Count >= count) return points;

        int spawnClearance = roadGenerator.GetSpawnClearanceCells();
        List<Vector3> roads = roadGenerator.GetSpawnableRoadPoints(spawnClearance);
        bool usedFallbackAllRoads = false;
        if (roads.Count < count)
        {
            List<Vector3> relaxed = roadGenerator.GetSpawnableRoadPoints(0);
            if (relaxed.Count > 0)
            {
                roads = relaxed;
            }
            if (roads.Count < count)
            {
                List<Vector3> allRoads = roadGenerator.GetRoadWorldPoints();
                List<Vector3> innerOnly = new List<Vector3>();
                for (int i = 0; i < allRoads.Count; i++)
                {
                    if (IsInsideInnerArenaByWorld(allRoads[i]))
                    {
                        innerOnly.Add(allRoads[i]);
                    }
                }
                roads = innerOnly;
                usedFallbackAllRoads = true;
            }
        }
        if (roads.Count == 0) return points;
        #region agent log
        AgentDebugLogger.Log(
            "H21",
            "IsometricArenaController.cs:GetSpawnPositions",
            "Spawn candidates prepared",
            "{\"requested\":" + count + ",\"candidateCount\":" + roads.Count + ",\"clearanceCells\":" + spawnClearance + ",\"usedFallbackAllRoads\":" + (usedFallbackAllRoads ? "true" : "false") + "}"
        );
        #endregion

        HashSet<int> used = new HashSet<int>();
        for (int i = 0; i < roads.Count; i++)
        {
            for (int p = 0; p < points.Count; p++)
            {
                if (Vector2.Distance(roads[i], points[p]) < 0.01f)
                {
                    used.Add(i);
                    break;
                }
            }
        }
        int max = Mathf.Min(count, roads.Count);

        for (int i = 0; i < max; i++)
        {
            if (points.Count >= max) break;
            int pick = Random.Range(0, roads.Count);
            int safety = 0;
            while ((used.Contains(pick) || !IsFarEnough(roads[pick], points)) && safety < 2000)
            {
                safety++;
                pick = Random.Range(0, roads.Count);
            }
            used.Add(pick);
            points.Add(roads[pick]);
            bool insideInner = IsInsideInnerArenaByWorld(roads[pick]);
            #region agent log
            AgentDebugLogger.Log(
                "H21",
                "IsometricArenaController.cs:GetSpawnPositions",
                "Spawn point selected",
                "{\"index\":" + i + ",\"x\":" + roads[pick].x.ToString("0.###") + ",\"y\":" + roads[pick].y.ToString("0.###") + ",\"insideInner\":" + (insideInner ? "true" : "false") + "}"
            );
            #endregion
        }
        #region agent log
        AgentDebugLogger.Log(
            "H1",
            "IsometricArenaController.cs:GetSpawnPositions",
            "Spawn positions selected",
            "{\"selectedCount\":" + points.Count + "}"
        );
        #endregion

        return points;
    }

    private bool IsFarEnough(Vector3 candidate, List<Vector3> chosen)
    {
        for (int i = 0; i < chosen.Count; i++)
        {
            if (Vector2.Distance(candidate, chosen[i]) < minSpawnDistance)
            {
                return false;
            }
        }
        return true;
    }

    public bool TryGetNextWaypoint(Vector2 fromWorld, Vector2 desiredWorld, out Vector2 waypoint)
    {
        waypoint = desiredWorld;
        if (roadGenerator == null) return false;
        return roadGenerator.TryGetNextWaypoint(fromWorld, desiredWorld, out waypoint);
    }

    public bool TryGetNearestWalkableWorldPoint(Vector2 world, int radius, out Vector2 point)
    {
        point = world;
        if (roadGenerator == null) return false;
        return roadGenerator.TryGetNearestWalkableWorldPoint(world, radius, out point);
    }

    public bool TryClampToInnerInsetWorld(Vector2 world, int insetCells, out Vector2 point)
    {
        point = world;
        if (roadGenerator == null) return false;
        return roadGenerator.TryClampToInnerInsetWorld(world, insetCells, out point);
    }

    public bool IsInsideInnerArenaByWorld(Vector2 worldPoint)
    {
        if (roadGenerator == null) return false;
        return roadGenerator.IsInsideInnerArenaByWorld(worldPoint);
    }

    private void SetupCamera()
    {
        Camera cam = arenaCamera != null ? arenaCamera : Camera.main;
        if (cam == null) return;
        cam.orthographic = true;
        cam.orthographicSize = cameraSize;

        Vector3 center = transform.position;
        if (roadGenerator != null)
        {
            center = roadGenerator.GetArenaCenterWorld();
        }

        cam.transform.position = new Vector3(
            center.x + cameraOffset.x,
            center.y + cameraOffset.y,
            cameraOffset.z
        );
    }
}
