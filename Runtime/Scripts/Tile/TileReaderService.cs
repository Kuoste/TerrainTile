//using LasUtility.Nls;
//using LasUtility.VoxelGrid;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Threading;
//using Debug = UnityEngine.Debug;

//namespace Kuoste.LidarWorld.Tile
//{
//    public class TileReaderService : ITileBuilderService
//    {
//        private readonly string _sDirectoryIntermediate;
//        private readonly string _sVersion;

//        private readonly List<Thread> _readerThreads = new();
//        private readonly ConcurrentQueue<Tile> _tileQueue = new();

//        public void AddTile(Tile tile)
//        {
//            _tileQueue.Enqueue(tile);
//        }

//        public void BuilderThread()
//        {
//            ITileBuilder tb = new TileReader();
//            tb.SetIntermediateDirectory(_sDirectoryIntermediate);

//            //long iLoopCount = 0;

//            while (true)
//            {
//                if (_tileQueue.Count > 0 && _tileQueue.TryDequeue(out Tile tile))
//                {
//                    Debug.Log($"New job found. Starting to load {tile.Name}.");
//                    Stopwatch sw = Stopwatch.StartNew();

//                    // Load grid from filesystem
//                    tb.BuildDemAndDsmPointCloud(tile);

//                    //if (!grids.ContainsKey(tile.Name))
//                    //{
//                    //    // Tile is still being triangulated
//                    //    _tileQueue.Enqueue(tile);
//                    //    continue;
//                    //}


//                    //tile.TerrainGrid = grids[tile.Name];

//                    sw.Stop();
//                    Debug.Log($"Tile {tile.Name} loaded in {sw.ElapsedMilliseconds}.");
//                    sw.Restart();


//                    for (int i = 0; i < tile.TerrainGrid.Bounds.RowCount; i++)
//                    {
//                        for (int j = 0; j < tile.TerrainGrid.Bounds.ColumnCount; j++)
//                        {
//                            int iRadius = 2;
//                            int iHighVegetationCount = 0;

//                            for (int ii = i - iRadius; ii <= i + iRadius; ii++)
//                            {
//                                for (int jj = j - iRadius; jj <= j + iRadius; jj++)
//                                {
//                                    if (ii < 0 || ii > tile.TerrainGrid.Bounds.RowCount - 1 ||
//                                        jj < 0 || jj > tile.TerrainGrid.Bounds.ColumnCount - 1)
//                                    {
//                                        continue;
//                                    }

//                                    List<BinPoint> points = tile.TerrainGrid.GetPoints(ii, jj);

//                                    foreach (BinPoint p in points)
//                                    {
//                                        if (p.Class == (byte)PointCloud05p.Classes.HighVegetation)
//                                            iHighVegetationCount++;
//                                    }
//                                }
//                            }

//                            if (iHighVegetationCount < 15)
//                                continue;

//                            if (tile.TerrainGrid.IsHighestBinInNeighborhood(i, j, iRadius,
//                                (byte)PointCloud05p.Classes.HighVegetation,
//                                (byte)PointCloud05p.Classes.HighVegetation))
//                            {
//                                tile.Trees.Add(new(i, j,
//                                    tile.TerrainGrid.GetHighestPointInClassRange(i, j,
//                                    (byte)PointCloud05p.Classes.HighVegetation,
//                                    (byte)PointCloud05p.Classes.HighVegetation).Z -
//                                    tile.TerrainGrid.GetGroundHeight(i, j)));
//                            }
//                        }
//                    }

//                    sw.Stop();
//                    Debug.Log($"Tile {tile.Name}: {tile.Trees.Count} trees determined in {sw.ElapsedMilliseconds} ms.");
//                    sw.Restart();

//                    // Clone terrainData
//                    //tile.TerrainData = Tools.TerrainDataCloner.Clone(tile.TerrainData);

//                    Interlocked.Increment(ref tile.CompletedCount);
//                }

//                //if (_tilesPostphoned.Count > 0 && ++iLoopCount % 5 == 0)
//                //{
//                //    for (int i = _tilesPostphoned.Count - 1; i >= 0; i--)
//                //    {
//                //        // Load grid from filesystem
//                //        var grids = tp.GetTerrain(_sDirectoryIntermediate, _tilesPostphoned[i].Name, _sVersion);

//                //        if (!grids.ContainsKey(_tilesPostphoned[i].Name))
//                //        {
//                //            // Tile is still being triangulated
//                //            continue;
//                //        }

//                //        _tilesPostphoned[i].TerrainGrid = grids[_tilesPostphoned[i].Name];
//                //        Interlocked.Increment(ref _tilesPostphoned[i].CompletedCount);

//                //        _tilesPostphoned.RemoveAt(i);
//                //    }
//                //}

//                Thread.Sleep(1000);
//            }
//        }

//        public TileReaderService(string sDirectoryIntermediate, string sVersion, int iThreadCount)
//        {
//            _sDirectoryIntermediate = sDirectoryIntermediate;
//            _sVersion = sVersion;

//            for (int i = 0; i < iThreadCount; i++)
//            {
//                Thread t = new(() => BuilderThread());
//                t.Start();
//                _readerThreads.Add(t);
//            }
//        }
//    }
//}

