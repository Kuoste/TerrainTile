using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Reflection;
using Kuoste.LidarWorld.Tile;
using Kuoste.LidarWorld.Tools;
using NetTopologySuite.Geometries;
using LasUtility.Nls;
using System.Threading;
using System.Globalization;
using System.IO;

public class TileManager : MonoBehaviour
{
    public GameObject TerrainTemplate;
    public GameObject WaterPlane;
    public Material BuildingRoof;
    public Material BuildingWall;

    public string RenderedArea;

    /// <summary>
    /// Folder where data from Nls is found
    /// </summary>
    public string DataDirectoryOriginal;

    /// <summary>
    ///  Folder for saving the rasterised / triangulated data
    /// </summary>
    public string DataDirectoryIntermediate;

    private Thread _dsmPointCloudThread;
    private Thread _rasterThread;
    private Thread _geometryThread;

    private readonly string _sVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private readonly List<Tile> _terrainTilesInProcess = new();

    private Coordinate _origo;

    private CancellationTokenSource _cancellationTokenSource;

    public int GetTilesInProcessCount()
    {
        return _terrainTilesInProcess.Count;
    }

    private void Awake()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        Stopwatch sw = Stopwatch.StartNew();

        if (!Directory.Exists(DataDirectoryOriginal))
            Debug.Log($"Cannot find data from {nameof(DataDirectoryOriginal)}: " +  DataDirectoryOriginal);

        if (!Directory.Exists(DataDirectoryIntermediate))
            Directory.CreateDirectory(DataDirectoryIntermediate);

        _cancellationTokenSource = new CancellationTokenSource();
        CancellationToken token = _cancellationTokenSource.Token;

        ITileBuilderService DsmPointCloudService = new TileDsmPointCloudService(new DemDsmReader(), new DemDsmCreator(), token);
        ITileBuilderService RasterService = new TileRasterService(new RasterReader(), new RasterCreator(), token);
        ITileBuilderService GeometryService = new TileGeometryService(new BuildingsReader(), new BuildingsCreator(),
            new TreeReader(),  new SimpleTreeCreator(),
            new WaterAreasReader(), new IWaterAreasCreator(), token);

        _dsmPointCloudThread = new(() => DsmPointCloudService.BuilderThread());
        _rasterThread = new(() => RasterService.BuilderThread());
        _geometryThread = new(() => GeometryService.BuilderThread());

        _dsmPointCloudThread.Start();
        _rasterThread.Start();
        _geometryThread.Start();

        TileNamer.Decode(RenderedArea, out Envelope bounds);
        _origo = new Coordinate(bounds.Centre.X, bounds.Centre.Y);

        TerrainData terrainData = TerrainTemplate.GetComponent<Terrain>().terrainData;
        TileCommon common = new((int)terrainData.heightmapScale.y, terrainData.alphamapResolution,
            Path.GetFullPath(DataDirectoryIntermediate), Path.GetFullPath(DataDirectoryOriginal),
            _sVersion, WaterPlane, BuildingWall, BuildingRoof);

        for (int x = (int)bounds.MinX; x < bounds.MaxX; x += TileCommon.EdgeLength)
        {
            for (int y = (int)bounds.MinY;  y < bounds.MaxY; y += TileCommon.EdgeLength)
            {
                string sTileName = TileNamer.Encode(x, y, TileCommon.EdgeLength);

                Tile t = new()
                {
                    Name = sTileName,
                    Common = common,
                };

                DsmPointCloudService.AddTile(t);
                RasterService.AddTile(t);
                GeometryService.AddTile(t);

                _terrainTilesInProcess.Add(t);
            }
        }
    }

    void OnApplicationQuit()
    {
        // Send cancellation signal to threads for manual exit
        _cancellationTokenSource.Cancel();

        // Wait for threads to exit
        _dsmPointCloudThread.Join();
        _rasterThread.Join();
        _geometryThread.Join();
    }


    // Update is called once per frame
    private void Update()
    {
        for (int i = _terrainTilesInProcess.Count - 1; i >= 0; i--)
        {
            if (_terrainTilesInProcess[i].IsCompleted)
            {
                //Stopwatch sw = Stopwatch.StartNew();

                Tile tile = _terrainTilesInProcess[i];
                _terrainTilesInProcess.RemoveAt(i);

                TileNamer.Decode(tile.Name, out Envelope bounds);
                Vector3 pos = new((float)(bounds.MinX - _origo.X), 0, (float)(bounds.MinY - _origo.Y));
                GameObject terrain = Instantiate(TerrainTemplate, pos, Quaternion.identity, transform);
                terrain.name = tile.Name;

                // Create and assign a deep copy of the terrain data
                TerrainData newTerrainData = TerrainDataCloner.Clone(terrain.GetComponent<Terrain>().terrainData);
                terrain.GetComponent<Terrain>().terrainData = newTerrainData;
                terrain.GetComponent<TerrainCollider>().terrainData = newTerrainData;

                // Add the tile updater component and assign the tile
                terrain.AddComponent<TileUpdater>().SetTile(tile);

                //sw.Stop();
                //Debug.Log($"Instantiating and cloning terrain {tile.Name} took {sw.ElapsedMilliseconds} ms");

                if (_terrainTilesInProcess.Count == 0)
                {
                    Debug.Log("All tiles instantiated");
                }
            }
        }
    }
}


