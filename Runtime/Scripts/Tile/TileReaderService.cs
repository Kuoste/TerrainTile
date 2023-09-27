using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Terrain
{
    public class TileReaderService// : MonoBehaviour
    {
        //private readonly string _sDirectoryIntermediate;
        //private readonly string _sVersion;

        readonly Thread _loaderThread;
        readonly ConcurrentQueue<TerrainTile> _tileQueue = new();
        readonly List<TerrainTile> _tilesPostphoned = new();

        public void AddTerrain(TerrainTile tile)
        {
            _tileQueue.Enqueue(tile);
        }

        public class TerrainTile
        {
            public string Name;
            public int Index;
            //public GameObject Terrain;
            public VoxelGrid TerrainGrid;

            public List<CoordinateZ> Trees = new();

            public int CompletedCount;
        }

        public TileReaderService(string sDirectoryIntermediate, string sVersion)
        {
            //_sDirectoryIntermediate = sDirectoryIntermediate;
            //_sVersion = sVersion;

            _loaderThread = new Thread(() =>
            {
                ITileProvider tp = new TileReader();

                long iLoopCount = 0;

                while (true)
                {
                    if (_tileQueue.Count > 0 && _tileQueue.TryDequeue(out TerrainTile tile))
                    {
                        Debug.Log($"New job found. Starting to load {tile.Name}.");
                        Stopwatch sw = Stopwatch.StartNew();

                        // Load grid from filesystem
                        var grids = tp.GetTerrain(sDirectoryIntermediate, tile.Name, sVersion);

                        if (!grids.ContainsKey(tile.Name))
                        {
                            // Tile is still being triangulated
                            _tilesPostphoned.Add(tile);
                            continue;
                        }

                        tile.TerrainGrid = grids[tile.Name];

                        sw.Stop();
                        Debug.Log($"Tile {tile.Name} loaded in {sw.ElapsedMilliseconds}.");
                        sw.Restart();


                        for (int i = 0; i < tile.TerrainGrid.Bounds.RowCount; i++)
                        {
                            for (int j = 0; j < tile.TerrainGrid.Bounds.ColumnCount; j++)
                            {
                                int iRadius = 2;
                                int iHighVegetationCount = 0;

                                for (int ii = i - iRadius; ii <= i + iRadius; ii++)
                                {
                                    for (int jj = j - iRadius; jj <= j + iRadius; jj++)
                                    {
                                        if (ii < 0 || ii > tile.TerrainGrid.Bounds.RowCount - 1 ||
                                            jj < 0 || jj > tile.TerrainGrid.Bounds.ColumnCount - 1)
                                        {
                                            continue;
                                        }

                                        List<BinPoint> points = tile.TerrainGrid.GetPoints(ii, jj);

                                        foreach (BinPoint p in points)
                                        {
                                            if (p.Class == (byte)NlsClasses.PointCloud05p.HighVegetation)
                                                iHighVegetationCount++;
                                        }
                                    }
                                }

                                if (iHighVegetationCount < 15)
                                    continue;

                                if (tile.TerrainGrid.IsHighestBinInNeighborhood(i, j, iRadius, 
                                    (byte)NlsClasses.PointCloud05p.HighVegetation, 
                                    (byte)NlsClasses.PointCloud05p.HighVegetation))
                                {
                                    tile.Trees.Add(new(i, j,
                                        tile.TerrainGrid.GetHighestPointInClassRange(i, j, 
                                        (byte)NlsClasses.PointCloud05p.HighVegetation, 
                                        (byte)NlsClasses.PointCloud05p.HighVegetation).Z -
                                        tile.TerrainGrid.GetGroundHeight(i, j)));
                                }
                            }
                        }

                        sw.Stop();
                        Debug.Log($"Tile {tile.Name}: {tile.Trees.Count} trees determined in {sw.ElapsedMilliseconds} ms.");
                        sw.Restart();

                        // Clone terrainData
                        //tile.TerrainData = Tools.TerrainDataCloner.Clone(tile.TerrainData);

                        Interlocked.Increment(ref tile.CompletedCount);
                    }

                    if (_tilesPostphoned.Count > 0 && ++iLoopCount % 5 == 0)
                    {
                        for (int i = _tilesPostphoned.Count - 1; i >= 0; i--)
                        {
                            // Load grid from filesystem
                            var grids = tp.GetTerrain(sDirectoryIntermediate, _tilesPostphoned[i].Name, sVersion);

                            if (!grids.ContainsKey(_tilesPostphoned[i].Name))
                            {
                                // Tile is still being triangulated
                                continue;
                            }

                            _tilesPostphoned[i].TerrainGrid = grids[_tilesPostphoned[i].Name];
                            Interlocked.Increment(ref _tilesPostphoned[i].CompletedCount);

                            _tilesPostphoned.RemoveAt(i);
                        }
                    }

                    Thread.Sleep(1000);
                }
            });

            _loaderThread.Start();
        }


        //private void Start()
        //{

        //}

        //private void Update()
        //{
        //    if (!_isProcessing && _taskProcessLas.Status.Equals(TaskStatus.Created))
        //    {
        //        _taskProcessLas.Start();
        //    }
        //}

        //private void ProcessLasFile(string sTileName)
        //{

        //}

        //private void CreateTile(string sTileName)
        //{
        //    if (_lasTilesAskedForProcessing.Contains(sTileName))
        //        return;

        //    _lasTilesAskedForProcessing.Add(sTileName);
        //    _taskProcessLas = new Task(() => { ProcessLasFile(sTileName); });
        //}

        //private TerrainTile GetTile(string sTileName)
        //{

        //    string sFilenameGrid = Path.Combine(sDirectoryIntermediate, sTileName + "_v" + sVersion + ".obj");

        //    if (!File.Exists(sFilenameGrid))
        //    {
        //        CreateTile(sTileName);
        //    }

        //    return null;
        //}

        //public TerrainTile(VoxelGrid grid, int iOffsetEast, int iOffsetNorth)
        //{
        ////_grid = grid;
        //_iGridEdgeLength = (int)(grid.Bounds.MaxX - grid.Bounds.MinX);

        //// East
        //string sName = NlsTileNamer.Encode((int)(grid.Bounds.MinX + _iGridEdgeLength),
        //    (int)grid.Bounds.MinY, _iGridEdgeLength);

        //if (loadedGrids.ContainsKey(sName))
        //{
        //    GridToEast = new(loadedGrids[sName],
        //        iOffsetEast + _iGridEdgeLength,
        //        iOffsetNorth,
        //        loadedGrids);
        //}

        //// West
        //sName = NlsTileNamer.Encode((int)(_grid.Bounds.MinX - _iGridEdgeLength),
        //    (int)_grid.Bounds.MinY, _iGridEdgeLength);

        //if (loadedGrids.ContainsKey(sName))
        //{
        //    GridToWest = new(loadedGrids[sName],
        //        iOffsetEast - _iGridEdgeLength,
        //        iOffsetNorth,
        //        loadedGrids);
        //}

        //// North
        //sName = NlsTileNamer.Encode((int)_grid.Bounds.MinX,
        //    (int)(_grid.Bounds.MinY + _iGridEdgeLength), _iGridEdgeLength);

        //if (loadedGrids.ContainsKey(sName))
        //{
        //    GridToNorth = new(loadedGrids[sName],
        //        iOffsetEast,
        //        iOffsetNorth + _iGridEdgeLength,
        //        loadedGrids);
        //}

        //// South
        //sName = NlsTileNamer.Encode((int)_grid.Bounds.MinX,
        //    (int)(_grid.Bounds.MinY - _iGridEdgeLength), _iGridEdgeLength);

        //if (loadedGrids.ContainsKey(sName))
        //{
        //    GridToSouth = new(loadedGrids[sName],
        //        iOffsetEast,
        //        iOffsetNorth - _iGridEdgeLength,
        //        loadedGrids);
        //}
    }
}

