using System.Collections.Generic;
using UnityEngine;

public class RoadGenerator : MonoBehaviour
{
    private enum RoadTileKind
    {
        Single,
        End,
        Straight,
        Corner,
        TJunction,
        Cross
    }

    [Header("Grid")]
    public int innerTileWidth = 9;
    public int innerTileHeight = 9;
    public int outerTileWidth = 28;
    public int outerTileHeight = 28;
    public float tileWidth = 1f;
    public float tileHeight = 1f;

    [Header("Simple Iso Ground")]
    public bool generateFlatIsoGround = true;
    public bool disableTileRotation = true;

    [Header("Road Sprites (Auto Objects)")]
    public Sprite roadSingleSprite;
    public Sprite roadEndSprite;
    public Sprite roadStraightSprite;
    public Sprite roadCornerSprite;
    public Sprite roadTJunctionSprite;
    public Sprite roadCrossSprite;
    public int roadSortingOrder = 0;
    public bool useIsometricLayerSorting = true;
    public int sortingMultiplier = 100;
    public bool addRoadCollider = false;

    [Header("Arena + Building Zones")]
    [Range(0f, 1f)] public float centerArenaBuildingDensity = 0.15f;
    [Range(0f, 1f)] public float outerZoneBuildingDensity = 0.62f;
    [Range(0f, 1f)] public float innerArenaDensityMultiplier = 0.25f;
    public bool avoidSamePrefabNeighbors = true;

    [Header("Building Lists By Footprint")]
    public List<Sprite> buildingList1x1 = new List<Sprite>();
    public List<Sprite> buildingList1x2 = new List<Sprite>();
    public List<Sprite> buildingList2x2 = new List<Sprite>();
    public bool addBuildingCollider = false;
    public bool forceBuildingCollider = true;
    public string buildingWallTag = "Wall";
    public float buildingYOffset = 0f;
    public int max2x2Placements = 1;
    public int baseBuildingSortingOffset = 110;
    public int largeBuildingExtraSortingOffset = 50;
    [Min(0)] public int spawnClearanceCells = 1;
    [Min(0)] public int spawnInsetFromInnerEdge = 2;
    public bool restrictMovementToInnerArea = true;

    [Header("Generation")]
    public int pathSeed = 0;
    public int randomWalkerSteps = 220;
    public int branchCount = 8;

    public Transform roadRoot;
    public Transform buildingRoot;

    private int mapWidth;
    private int mapHeight;
    private bool[,] roadCells;
    private readonly List<GameObject> spawnedRoads = new List<GameObject>();
    private readonly List<GameObject> spawnedBuildings = new List<GameObject>();
    private readonly List<Vector3> roadWorldPoints = new List<Vector3>();
    private bool[,] occupiedByBuilding;
    private bool[,] spawnReserved;
    private int[,] buildingPrefabIds;
    private int placed2x2Count;

    public void GenerateRoads()
    {
        ClearGenerated();
        EnsureRoots();
        ConfigureMapSize();

        roadCells = new bool[mapWidth, mapHeight];
        occupiedByBuilding = new bool[mapWidth, mapHeight];
        spawnReserved = new bool[mapWidth, mapHeight];
        buildingPrefabIds = new int[mapWidth, mapHeight];
        roadWorldPoints.Clear();
        placed2x2Count = 0;

        if (generateFlatIsoGround)
        {
            BuildFlatRoadGround();
        }
        else
        {
            if (pathSeed != 0) Random.InitState(pathSeed);
            BuildRandomRoadNetwork();
        }

        SpawnRoadTiles();
        MarkSpawnReservedCells();
        SpawnBuildings();
        #region agent log
        AgentDebugLogger.Log(
            "H6",
            "RoadGenerator.cs:GenerateRoads",
            "Generation summary",
            "{\"buildings\":" + spawnedBuildings.Count + ",\"forceBuildingCollider\":" + (forceBuildingCollider ? "true" : "false") + ",\"addBuildingCollider\":" + (addBuildingCollider ? "true" : "false") + "}"
        );
        #endregion
    }

    public List<Vector3> GetRoadWorldPoints()
    {
        return new List<Vector3>(roadWorldPoints);
    }

    public List<Vector3> GetSpawnableRoadPoints(int minClearanceCells = 1)
    {
        List<Vector3> points = new List<Vector3>();
        if (roadCells == null || occupiedByBuilding == null) return points;

        int clearance = Mathf.Max(0, minClearanceCells);
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                if (!roadCells[x, y]) continue;
                if (!IsInsideInnerArea(x, y)) continue;
                if (IsNearBuilding(x, y, clearance)) continue;
                points.Add(GridToIsoWorld(new Vector2Int(x, y)));
            }
        }

        return points;
    }

    public bool TryGetNextWaypoint(Vector2 fromWorld, Vector2 desiredWorld, out Vector2 waypoint)
    {
        waypoint = desiredWorld;
        if (roadCells == null || occupiedByBuilding == null) return false;

        if (!TryFindNearestWalkableCellByWorld(fromWorld, out Vector2Int startCell)) return false;
        if (!TryFindNearestWalkableCellByWorld(desiredWorld, out Vector2Int goalCell)) return false;
        if (!IsWalkable(startCell) || !IsWalkable(goalCell)) return false;

        List<Vector2Int> path = FindPathCells(startCell, goalCell);
        if (path == null || path.Count < 2) return false;

        waypoint = GridToIsoWorld(path[1]);
        return true;
    }

    public bool TryGetNearestWalkableWorldPoint(Vector2 world, int searchRadius, out Vector2 point)
    {
        point = world;
        if (!TryWorldToGrid(world, out Vector2Int cell))
        {
            if (!TryFindNearestWalkableCellByWorld(world, out cell)) return false;
        }

        Vector2Int snap = SnapToNearestWalkable(cell, Mathf.Max(1, searchRadius));
        if (!IsWalkable(snap))
        {
            return false;
        }

        point = GridToIsoWorld(snap);
        return true;
    }

    public bool TryClampToInnerInsetWorld(Vector2 world, int insetCells, out Vector2 point)
    {
        point = world;
        if (roadCells == null || occupiedByBuilding == null) return false;

        Vector2Int cell;
        if (!TryWorldToGrid(world, out cell))
        {
            if (!TryFindNearestWalkableCellByWorld(world, out cell)) return false;
        }

        int innerStartX = Mathf.Max(0, (mapWidth - innerTileWidth) / 2);
        int innerStartY = Mathf.Max(0, (mapHeight - innerTileHeight) / 2);
        int innerEndX = innerStartX + innerTileWidth - 1;
        int innerEndY = innerStartY + innerTileHeight - 1;

        int maxInsetX = Mathf.Max(0, (innerEndX - innerStartX) / 2);
        int maxInsetY = Mathf.Max(0, (innerEndY - innerStartY) / 2);
        int inset = Mathf.Clamp(insetCells, 0, Mathf.Min(maxInsetX, maxInsetY));

        int minX = innerStartX + inset;
        int maxX = innerEndX - inset;
        int minY = innerStartY + inset;
        int maxY = innerEndY - inset;

        Vector2Int clampedCell = new Vector2Int(
            Mathf.Clamp(cell.x, minX, maxX),
            Mathf.Clamp(cell.y, minY, maxY)
        );

        Vector2Int walkable = SnapToNearestWalkable(clampedCell, 6);
        if (!IsWalkable(walkable)) return false;
        point = GridToIsoWorld(walkable);
        return true;
    }

    public List<Vector3> GetInnerCornerSpawnPoints()
    {
        List<Vector3> points = new List<Vector3>();
        if (roadCells == null || occupiedByBuilding == null) return points;
        Vector2Int[] corners = GetInnerCornerCells();

        for (int i = 0; i < corners.Length; i++)
        {
            Vector2Int walkable = SnapToNearestWalkable(corners[i], 4);
            if (!IsWalkable(walkable)) continue;
            Vector3 world = GridToIsoWorld(walkable);
            points.Add(world);
            #region agent log
            AgentDebugLogger.Log(
                "H33",
                "RoadGenerator.cs:GetInnerCornerSpawnPoints",
                "Inner corner spawn resolved",
                "{\"cornerIndex\":" + i + ",\"gridX\":" + corners[i].x + ",\"gridY\":" + corners[i].y + ",\"walkableX\":" + walkable.x + ",\"walkableY\":" + walkable.y + ",\"worldX\":" + world.x.ToString("0.###") + ",\"worldY\":" + world.y.ToString("0.###") + "}"
            );
            #endregion
        }

        return points;
    }

    public int GetSpawnClearanceCells()
    {
        return Mathf.Max(0, spawnClearanceCells);
    }

    public Vector3 GetRandomRoadPoint()
    {
        if (roadWorldPoints.Count == 0) return transform.position;
        return roadWorldPoints[Random.Range(0, roadWorldPoints.Count)];
    }

    public Vector3 GetArenaCenterWorld()
    {
        if (mapWidth <= 0 || mapHeight <= 0)
        {
            ConfigureMapSize();
        }
        if (mapWidth <= 0 || mapHeight <= 0) return transform.position;

        Vector3 c1 = GridToIsoWorld(new Vector2Int(0, 0));
        Vector3 c2 = GridToIsoWorld(new Vector2Int(mapWidth - 1, 0));
        Vector3 c3 = GridToIsoWorld(new Vector2Int(0, mapHeight - 1));
        Vector3 c4 = GridToIsoWorld(new Vector2Int(mapWidth - 1, mapHeight - 1));

        float minX = Mathf.Min(c1.x, c2.x, c3.x, c4.x);
        float maxX = Mathf.Max(c1.x, c2.x, c3.x, c4.x);
        float minY = Mathf.Min(c1.y, c2.y, c3.y, c4.y);
        float maxY = Mathf.Max(c1.y, c2.y, c3.y, c4.y);

        return new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
    }

    private void BuildRandomRoadNetwork()
    {
        Vector2Int center = new Vector2Int(mapWidth / 2, mapHeight / 2);
        CarveRoad(center);

        Vector2Int walker = center;
        for (int i = 0; i < randomWalkerSteps; i++)
        {
            walker += RandomDirection();
            walker.x = Mathf.Clamp(walker.x, 1, mapWidth - 2);
            walker.y = Mathf.Clamp(walker.y, 1, mapHeight - 2);
            CarveRoad(walker);

            if (Random.value < 0.15f)
            {
                CarveRoad(walker + RandomDirection());
            }
        }

        for (int i = 0; i < branchCount; i++)
        {
            Vector2Int branchStart = new Vector2Int(
                Random.Range(1, mapWidth - 1),
                Random.Range(1, mapHeight - 1)
            );
            if (!roadCells[branchStart.x, branchStart.y]) continue;

            Vector2Int dir = RandomDirection();
            Vector2Int pos = branchStart;
            int branchLen = Random.Range(4, 12);
            for (int b = 0; b < branchLen; b++)
            {
                pos += dir;
                if (!IsInside(pos)) break;
                CarveRoad(pos);
                if (Random.value < 0.25f) dir = RandomDirection();
            }
        }
    }

    private void SpawnRoadTiles()
    {
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                if (!roadCells[x, y]) continue;

                Vector2Int cell = new Vector2Int(x, y);
                Vector3 world = GridToIsoWorld(cell);
                GameObject tile = SpawnRoadTile(cell, world);
                if (tile != null) spawnedRoads.Add(tile);
                if (IsInsideInnerArea(x, y))
                {
                    roadWorldPoints.Add(world);
                }
            }
        }
    }

    private GameObject SpawnRoadTile(Vector2Int cell, Vector3 world)
    {
        RoadTileKind kind;
        float zRot;
        if (generateFlatIsoGround)
        {
            kind = RoadTileKind.Single;
            zRot = 0f;
        }
        else
        {
            kind = SelectRoadTileKind(cell, out zRot);
        }
        Sprite sprite = GetRoadSprite(kind);
        if (sprite == null) return null;
        if (disableTileRotation) zRot = 0f;

        GameObject go = new GameObject("Road_" + cell.x + "_" + cell.y);
        go.transform.SetParent(roadRoot, false);
        go.transform.position = world;
        go.transform.rotation = Quaternion.Euler(0f, 0f, zRot);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = GetTileSortingOrder(world);

        if (addRoadCollider)
        {
            go.AddComponent<BoxCollider2D>();
        }

        return go;
    }

    private int GetTileSortingOrder(Vector3 world)
    {
        if (!useIsometricLayerSorting) return roadSortingOrder;

        // Upper tiles (bigger Y) stay in back, lower tiles render in front.
        int dynamicOrder = roadSortingOrder - Mathf.RoundToInt(world.y * sortingMultiplier);
        return dynamicOrder;
    }

    private void BuildFlatRoadGround()
    {
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                roadCells[x, y] = true;
            }
        }
    }

    private void SpawnBuildings()
    {
        if (buildingList1x1.Count == 0 &&
            buildingList1x2.Count == 0 &&
            buildingList2x2.Count == 0)
        {
            return;
        }

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                if (occupiedByBuilding[x, y]) continue;

                bool inArena = IsInsideInnerArea(x, y);
                float density = inArena
                    ? centerArenaBuildingDensity * Mathf.Clamp01(innerArenaDensityMultiplier)
                    : outerZoneBuildingDensity;
                if (Random.value > density) continue;

                TryPlaceRandomBuildingAt(new Vector2Int(x, y));
            }
        }
    }

    private bool TryPlaceRandomBuildingAt(Vector2Int anchor)
    {
        bool inInner = IsInsideInnerArea(anchor.x, anchor.y);
        int[] order = inInner ? new[] { 0 } : new[] { 0, 1, 2 };
        ShuffleIntArray(order);

        for (int i = 0; i < order.Length; i++)
        {
            Vector2Int size;
            List<Sprite> list = GetListByCategory(order[i], out size);
            if (list == null || list.Count == 0) continue;
            if (size.x == 2 && size.y == 2 && placed2x2Count >= Mathf.Max(0, max2x2Placements)) continue;

            Sprite sprite = list[Random.Range(0, list.Count)];
            if (sprite == null) continue;
            if (!CanPlaceFootprint(anchor, size, sprite.GetInstanceID())) continue;

            PlaceBuilding(anchor, size, sprite);
            return true;
        }

        return false;
    }

    private List<Sprite> GetListByCategory(int category, out Vector2Int size)
    {
        // Categories: 1x1, 1x2, 2x2.
        if (category == 0)
        {
            size = new Vector2Int(1, 1);
            return buildingList1x1;
        }
        if (category == 1)
        {
            size = new Vector2Int(1, 2);
            return buildingList1x2;
        }
        if (category == 2)
        {
            size = new Vector2Int(2, 2);
            return buildingList2x2;
        }

        size = new Vector2Int(1, 1);
        return buildingList1x1;
    }

    private bool CanPlaceFootprint(Vector2Int anchor, Vector2Int size, int prefabId)
    {
        int maxX = anchor.x + size.x - 1;
        int maxY = anchor.y + size.y - 1;
        if (anchor.x < 0 || anchor.y < 0 || maxX >= mapWidth || maxY >= mapHeight) return false;

        for (int y = anchor.y; y <= maxY; y++)
        {
            for (int x = anchor.x; x <= maxX; x++)
            {
                if (occupiedByBuilding[x, y]) return false;
                if (spawnReserved != null && spawnReserved[x, y]) return false;

                // Inner arena allows only 1x1 buildings.
                if ((size.x != 1 || size.y != 1) && IsInsideInnerArea(x, y))
                {
                    return false;
                }
            }
        }

        if (!avoidSamePrefabNeighbors) return true;

        for (int y = anchor.y - 1; y <= maxY + 1; y++)
        {
            for (int x = anchor.x - 1; x <= maxX + 1; x++)
            {
                if (x < 0 || y < 0 || x >= mapWidth || y >= mapHeight) continue;
                if (x >= anchor.x && x <= maxX && y >= anchor.y && y <= maxY) continue;
                if (buildingPrefabIds[x, y] == prefabId) return false;
            }
        }

        return true;
    }

    private Vector2Int[] GetInnerCornerCells()
    {
        int innerStartX = Mathf.Max(0, (mapWidth - innerTileWidth) / 2);
        int innerStartY = Mathf.Max(0, (mapHeight - innerTileHeight) / 2);
        int innerEndX = innerStartX + innerTileWidth - 1;
        int innerEndY = innerStartY + innerTileHeight - 1;
        int maxInsetX = Mathf.Max(0, (innerEndX - innerStartX) / 2);
        int maxInsetY = Mathf.Max(0, (innerEndY - innerStartY) / 2);
        int inset = Mathf.Min(spawnInsetFromInnerEdge, Mathf.Min(maxInsetX, maxInsetY));
        int sx = innerStartX + inset;
        int sy = innerStartY + inset;
        int ex = innerEndX - inset;
        int ey = innerEndY - inset;

        return new[]
        {
            new Vector2Int(sx, ey), // top-left in inset inner space
            new Vector2Int(ex, ey), // top-right in inset inner space
            new Vector2Int(sx, sy), // bottom-left in inset inner space
            new Vector2Int(ex, sy)  // bottom-right in inset inner space
        };
    }

    private void MarkSpawnReservedCells()
    {
        if (spawnReserved == null) return;
        Vector2Int[] corners = GetInnerCornerCells();
        int clearance = Mathf.Max(0, spawnClearanceCells);
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2Int c = corners[i];
            int minX = Mathf.Max(0, c.x - clearance);
            int maxX = Mathf.Min(mapWidth - 1, c.x + clearance);
            int minY = Mathf.Max(0, c.y - clearance);
            int maxY = Mathf.Min(mapHeight - 1, c.y + clearance);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    spawnReserved[x, y] = true;
                }
            }
        }
    }

    private void PlaceBuilding(Vector2Int anchor, Vector2Int size, Sprite sprite)
    {
        int prefabId = sprite.GetInstanceID();
        int maxX = anchor.x + size.x - 1;
        int maxY = anchor.y + size.y - 1;

        for (int y = anchor.y; y <= maxY; y++)
        {
            for (int x = anchor.x; x <= maxX; x++)
            {
                occupiedByBuilding[x, y] = true;
                buildingPrefabIds[x, y] = prefabId;
            }
        }

        Vector3 p1 = GridToIsoWorld(anchor);
        Vector3 p2 = GridToIsoWorld(new Vector2Int(maxX, maxY));
        Vector3 center = (p1 + p2) * 0.5f;
        center.y += buildingYOffset;

        GameObject b = new GameObject("Building_" + anchor.x + "_" + anchor.y + "_" + size.x + "x" + size.y);
        b.transform.SetParent(buildingRoot, false);
        b.transform.position = center;
        TryAssignTag(b, buildingWallTag);
        SpriteRenderer sr = b.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;

        if (addBuildingCollider || forceBuildingCollider)
        {
            BoxCollider2D col = b.AddComponent<BoxCollider2D>();
            #region agent log
            AgentDebugLogger.Log(
                "H6",
                "RoadGenerator.cs:PlaceBuilding",
                "Building collider added",
                "{\"name\":\"" + b.name + "\",\"isTrigger\":" + (col.isTrigger ? "true" : "false") + ",\"tag\":\"" + b.tag + "\"}"
            );
            #endregion
        }

        FitBuildingScaleToCategory(b, sr, size);
        b.name = "Building_" + anchor.x + "_" + anchor.y + "_" + size.x + "x" + size.y;

        int sortingBonus = baseBuildingSortingOffset;
        if ((size.x == 1 && size.y == 2) || (size.x == 2 && size.y == 2))
        {
            sortingBonus += largeBuildingExtraSortingOffset;
        }
        sr.sortingOrder = GetTileSortingOrder(center) + sortingBonus;

        spawnedBuildings.Add(b);
        if (size.x == 2 && size.y == 2)
        {
            placed2x2Count++;
        }
    }

    private void FitBuildingScaleToCategory(GameObject b, SpriteRenderer sr, Vector2Int size)
    {
        if (b == null) return;
        if (sr == null || sr.sprite == null) return;

        Vector2 spriteSize = sr.sprite.bounds.size;
        if (spriteSize.x <= 0.0001f || spriteSize.y <= 0.0001f) return;

        float targetW = tileWidth * size.x * 0.9f;
        float targetH = tileHeight * size.y * 0.65f;

        float sx = targetW / spriteSize.x;
        float sy = targetH / spriteSize.y;
        float fit = Mathf.Min(sx, sy);
        if (fit <= 0.0001f) return;

        b.transform.localScale *= fit;
    }

    private void ShuffleIntArray(int[] arr)
    {
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int t = arr[i];
            arr[i] = arr[j];
            arr[j] = t;
        }
    }

    private RoadTileKind SelectRoadTileKind(Vector2Int cell, out float rotationZ)
    {
        bool up = IsRoad(cell + Vector2Int.up);
        bool down = IsRoad(cell + Vector2Int.down);
        bool left = IsRoad(cell + Vector2Int.left);
        bool right = IsRoad(cell + Vector2Int.right);

        int count = (up ? 1 : 0) + (down ? 1 : 0) + (left ? 1 : 0) + (right ? 1 : 0);
        rotationZ = 0f;

        if (count == 4) return RoadTileKind.Cross;
        if (count == 3)
        {
            if (!up) rotationZ = 180f;
            else if (!right) rotationZ = 90f;
            else if (!left) rotationZ = -90f;
            return RoadTileKind.TJunction;
        }

        if (count == 2)
        {
            if (up && down) return RoadTileKind.Straight;
            if (left && right)
            {
                rotationZ = 90f;
                return RoadTileKind.Straight;
            }

            if (up && right) rotationZ = 0f;
            else if (right && down) rotationZ = -90f;
            else if (down && left) rotationZ = 180f;
            else if (left && up) rotationZ = 90f;
            return RoadTileKind.Corner;
        }

        if (count == 1)
        {
            if (down) rotationZ = 180f;
            else if (left) rotationZ = 90f;
            else if (right) rotationZ = -90f;
            return RoadTileKind.End;
        }

        return RoadTileKind.Single;
    }

    private Sprite GetRoadSprite(RoadTileKind kind)
    {
        // If only one sprite is provided, use it for every road tile.
        if (roadSingleSprite != null &&
            roadEndSprite == null &&
            roadStraightSprite == null &&
            roadCornerSprite == null &&
            roadTJunctionSprite == null &&
            roadCrossSprite == null)
        {
            return roadSingleSprite;
        }

        if (kind == RoadTileKind.Cross) return roadCrossSprite != null ? roadCrossSprite : roadSingleSprite;
        if (kind == RoadTileKind.TJunction) return roadTJunctionSprite != null ? roadTJunctionSprite : roadSingleSprite;
        if (kind == RoadTileKind.Corner) return roadCornerSprite != null ? roadCornerSprite : roadSingleSprite;
        if (kind == RoadTileKind.Straight) return roadStraightSprite != null ? roadStraightSprite : roadSingleSprite;
        if (kind == RoadTileKind.End) return roadEndSprite != null ? roadEndSprite : roadSingleSprite;
        return roadSingleSprite;
    }

    private Vector3 GridToIsoWorld(Vector2Int cell)
    {
        float x = (cell.x - cell.y) * tileWidth * 0.5f;
        float y = (cell.x + cell.y) * tileHeight * 0.25f;
        return transform.position + new Vector3(x, y, 0f);
    }

    private bool TryWorldToGrid(Vector2 world, out Vector2Int cell)
    {
        Vector2 local = world - (Vector2)transform.position;
        float a = tileWidth > 0f ? local.x / (tileWidth * 0.5f) : 0f;    // x - y
        float b = tileHeight > 0f ? local.y / (tileHeight * 0.25f) : 0f;  // x + y
        float gx = (a + b) * 0.5f;
        float gy = (b - a) * 0.5f;

        cell = new Vector2Int(Mathf.RoundToInt(gx), Mathf.RoundToInt(gy));
        return IsInside(cell);
    }

    private bool IsWalkable(Vector2Int cell)
    {
        if (!IsInside(cell)) return false;
        if (roadCells == null || occupiedByBuilding == null) return false;
        if (restrictMovementToInnerArea && !IsInsideInnerArea(cell.x, cell.y)) return false;
        return roadCells[cell.x, cell.y] && !occupiedByBuilding[cell.x, cell.y];
    }

    private Vector2Int SnapToNearestWalkable(Vector2Int origin, int radius)
    {
        if (IsWalkable(origin)) return origin;

        for (int r = 1; r <= Mathf.Max(1, radius); r++)
        {
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    Vector2Int c = new Vector2Int(origin.x + x, origin.y + y);
                    if (IsWalkable(c)) return c;
                }
            }
        }

        return origin;
    }

    private List<Vector2Int> FindPathCells(Vector2Int start, Vector2Int goal)
    {
        if (start == goal) return new List<Vector2Int> { start };

        bool[,] closed = new bool[mapWidth, mapHeight];
        float[,] g = new float[mapWidth, mapHeight];
        bool[,] hasParent = new bool[mapWidth, mapHeight];
        Vector2Int[,] parent = new Vector2Int[mapWidth, mapHeight];
        List<Vector2Int> open = new List<Vector2Int>();

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                g[x, y] = float.MaxValue;
            }
        }

        g[start.x, start.y] = 0f;
        open.Add(start);

        Vector2Int[] dirs = new[]
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        int safety = 0;
        while (open.Count > 0 && safety < 5000)
        {
            safety++;
            int bestIndex = 0;
            float bestF = float.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                Vector2Int n = open[i];
                float h = Mathf.Abs(n.x - goal.x) + Mathf.Abs(n.y - goal.y);
                float f = g[n.x, n.y] + h;
                if (f < bestF)
                {
                    bestF = f;
                    bestIndex = i;
                }
            }

            Vector2Int current = open[bestIndex];
            open.RemoveAt(bestIndex);
            if (current == goal)
            {
                return ReconstructPath(start, goal, hasParent, parent);
            }

            closed[current.x, current.y] = true;
            for (int d = 0; d < dirs.Length; d++)
            {
                Vector2Int next = current + dirs[d];
                if (!IsWalkable(next)) continue;
                if (closed[next.x, next.y]) continue;

                float ng = g[current.x, current.y] + 1f;
                if (ng < g[next.x, next.y])
                {
                    g[next.x, next.y] = ng;
                    parent[next.x, next.y] = current;
                    hasParent[next.x, next.y] = true;
                    if (!open.Contains(next))
                    {
                        open.Add(next);
                    }
                }
            }
        }

        return null;
    }

    private List<Vector2Int> ReconstructPath(Vector2Int start, Vector2Int goal, bool[,] hasParent, Vector2Int[,] parent)
    {
        List<Vector2Int> reversed = new List<Vector2Int>();
        Vector2Int current = goal;
        reversed.Add(current);

        int safety = 0;
        while (current != start && safety < 5000)
        {
            safety++;
            if (!hasParent[current.x, current.y]) break;
            current = parent[current.x, current.y];
            reversed.Add(current);
        }

        reversed.Reverse();
        return reversed;
    }

    private bool TryFindNearestWalkableCellByWorld(Vector2 world, out Vector2Int nearest)
    {
        nearest = default;
        if (roadCells == null || occupiedByBuilding == null) return false;

        bool found = false;
        float best = float.MaxValue;
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                Vector2Int c = new Vector2Int(x, y);
                if (!IsWalkable(c)) continue;
                Vector2 w = GridToIsoWorld(c);
                float d = (w - world).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = c;
                    found = true;
                }
            }
        }
        return found;
    }

    private void CarveRoad(Vector2Int cell)
    {
        if (!IsInside(cell)) return;
        roadCells[cell.x, cell.y] = true;
    }

    private bool IsRoad(Vector2Int cell)
    {
        return IsInside(cell) && roadCells[cell.x, cell.y];
    }

    private bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < mapWidth && cell.y < mapHeight;
    }

    private Vector2Int RandomDirection()
    {
        int r = Random.Range(0, 4);
        if (r == 0) return Vector2Int.up;
        if (r == 1) return Vector2Int.down;
        if (r == 2) return Vector2Int.left;
        return Vector2Int.right;
    }

    private void EnsureRoots()
    {
        if (roadRoot == null)
        {
            GameObject go = new GameObject("GeneratedRoads");
            roadRoot = go.transform;
            roadRoot.SetParent(transform, false);
        }
        if (buildingRoot == null)
        {
            GameObject go = new GameObject("GeneratedBuildings");
            buildingRoot = go.transform;
            buildingRoot.SetParent(transform, false);
        }
    }

    private void ClearGenerated()
    {
        for (int i = 0; i < spawnedRoads.Count; i++)
        {
            if (spawnedRoads[i] != null) Destroy(spawnedRoads[i]);
        }
        spawnedRoads.Clear();

        for (int i = 0; i < spawnedBuildings.Count; i++)
        {
            if (spawnedBuildings[i] != null) Destroy(spawnedBuildings[i]);
        }
        spawnedBuildings.Clear();
    }

    private void ConfigureMapSize()
    {
        innerTileWidth = Mathf.Max(3, innerTileWidth);
        innerTileHeight = Mathf.Max(3, innerTileHeight);
        outerTileWidth = Mathf.Max(innerTileWidth, outerTileWidth);
        outerTileHeight = Mathf.Max(innerTileHeight, outerTileHeight);

        mapWidth = outerTileWidth;
        mapHeight = outerTileHeight;
    }

    private bool IsInsideInnerArea(int x, int y)
    {
        int innerStartX = Mathf.Max(0, (mapWidth - innerTileWidth) / 2);
        int innerStartY = Mathf.Max(0, (mapHeight - innerTileHeight) / 2);
        int innerEndX = innerStartX + innerTileWidth - 1;
        int innerEndY = innerStartY + innerTileHeight - 1;
        return x >= innerStartX && x <= innerEndX && y >= innerStartY && y <= innerEndY;
    }

    public bool IsInsideInnerArenaByWorld(Vector2 worldPoint)
    {
        if (!TryWorldToGrid(worldPoint, out Vector2Int cell)) return false;
        return IsInsideInnerArea(cell.x, cell.y);
    }

    private bool IsNearBuilding(int x, int y, int clearance)
    {
        int minX = Mathf.Max(0, x - clearance);
        int maxX = Mathf.Min(mapWidth - 1, x + clearance);
        int minY = Mathf.Max(0, y - clearance);
        int maxY = Mathf.Min(mapHeight - 1, y + clearance);

        for (int cy = minY; cy <= maxY; cy++)
        {
            for (int cx = minX; cx <= maxX; cx++)
            {
                if (occupiedByBuilding[cx, cy]) return true;
            }
        }

        return false;
    }

    private void TryAssignTag(GameObject go, string tagName)
    {
        if (go == null || string.IsNullOrWhiteSpace(tagName)) return;
        try
        {
            go.tag = tagName;
        }
        catch
        {
            go.tag = "Untagged";
        }
    }
}
