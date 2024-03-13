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
    public GameObject _TerrainTemplate;
    public string _sStartTileName;

    private Thread _dsmPointCloudThread;
    private Thread _rasterThread;
    private Thread _geometryThread;

    /// <summary>
    /// Folder where data from Nls is found
    /// </summary>
    public string _sDirectoryOriginal;

    /// <summary>
    ///  Folder for saving the rasterised / triangulated data
    /// </summary>
    public string _sDirectoryIntermediate;

    private readonly string _sVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

    private readonly List<Tile> _terrainTilesInProcess = new();

    private Coordinate _origo;

    private CancellationTokenSource _cancellationTokenSource;

    private void Awake()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        Directory.CreateDirectory(_sDirectoryIntermediate);

        Stopwatch sw = Stopwatch.StartNew();

        _cancellationTokenSource = new CancellationTokenSource();

        ITileBuilderService DsmPointCloudService = new TileDsmPointCloudService(new DemDsmReader(), new DemDsmCreator());
        ITileBuilderService RasterService = new TileRasterService(new RasterReader(), new RasterCreator());
        ITileBuilderService GeometryService = new TileGeometryService(new BuildingsReader(), new BuildingsCreator(),
            new TreeReader(),  new SimpleTreeCreator(),
            new WaterAreasReader(), new IWaterAreasCreator());

        _dsmPointCloudThread = new(() => DsmPointCloudService.BuilderThread());
        _rasterThread = new(() => RasterService.BuilderThread());
        _geometryThread = new(() => GeometryService.BuilderThread());

        _dsmPointCloudThread.Start();
        _rasterThread.Start();
        _geometryThread.Start();

        sw.Stop();
        Debug.Log("Initializing threading took " + sw.ElapsedMilliseconds + " ms");
        sw.Restart();

        TileNamer.Decode(_sStartTileName, out Envelope bounds);
        _origo = new Coordinate(bounds.Centre.X, bounds.Centre.Y);

        TerrainData terrainData = _TerrainTemplate.GetComponent<Terrain>().terrainData;

        for (int x = (int)bounds.MinX; x < bounds.MaxX; x += Tile.EdgeLength)
        {
            for (int y = (int)bounds.MinY;  y < bounds.MaxY; y += Tile.EdgeLength)
            {
                string sTileName = TileNamer.Encode(x, y, Tile.EdgeLength);

                Tile t = new()
                {
                    Name = sTileName,
                    Version = _sVersion,
                    DemMaxHeight = (int)terrainData.heightmapScale.y,
                    AlphamapResolution = terrainData.alphamapResolution,
                    //HeightMapResolution = terrainData.heightmapResolution,
                    Token = _cancellationTokenSource.Token,
                    DirectoryIntermediate = _sDirectoryIntermediate,
                    DirectoryOriginal = _sDirectoryOriginal,
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

        // Interrupt threads in Thread.Sleep()
        _dsmPointCloudThread.Interrupt();
        _rasterThread.Interrupt();
        _geometryThread.Interrupt();

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
                GameObject terrain = Instantiate(_TerrainTemplate, pos, Quaternion.identity, transform);
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


