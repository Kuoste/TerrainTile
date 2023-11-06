using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Tile
{
    public class TileGeometryService : ITileBuilderService
    {
        ITileBuilder _reader;
        ITileBuilder _creator;

        private readonly ConcurrentQueue<Tile> _tileQueue = new();

        public TileGeometryService(ITileBuilder reader, ITileBuilder creator)
        {
            _reader = reader;
            _creator = creator;
        }

        public void AddTile(Tile tile)
        {
            _tileQueue.Enqueue(tile);
        }

        public void BuilderThread()
        {
            int iSleepMs = 1000;

            while (true)
            {
                if (_tileQueue.Count > 0 && _tileQueue.TryDequeue(out Tile tile))
                {
                    // Buildings require surface heights to be available first
                    _creator.DemDsmDone.TryGetValue(tile.Name, out bool isDemDsmBuilt);

                    if (false == isDemDsmBuilt)
                        _reader.DemDsmDone.TryGetValue(tile.Name, out isDemDsmBuilt);

                    if (true == isDemDsmBuilt)
                    {
                        Stopwatch sw = Stopwatch.StartNew();

                        string sFullFilename = Path.Combine(_reader.DirectoryIntermediate, tile.FilenameBuildings);

                        if (!File.Exists(sFullFilename))
                        {
                            // Create from shapefiles and DSM
                            _creator.BuildBuildings(tile);
                        }

                        // Read from file
                        _reader.BuildBuildings(tile);

                        sFullFilename = Path.Combine(_reader.DirectoryIntermediate, tile.FilenameTrees);

                        if (!File.Exists(sFullFilename))
                        {
                            // Create from terrain
                            _creator.BuildTrees(tile);
                        }

                        // Read from file
                        _reader.BuildTrees(tile);

                        sFullFilename = Path.Combine(_reader.DirectoryIntermediate, tile.FilenameWaterAreas);

                        if (!File.Exists(sFullFilename))
                        {
                            // Create from terrain
                            _creator.BuildWaterAreas(tile);
                        }

                        // Read from file
                        _reader.BuildWaterAreas(tile);

                        sw.Stop();
                        Debug.Log($"Tile {tile.Name} geometries created in {sw.ElapsedMilliseconds} ms.");

                        iSleepMs = 100;
                    }
                    else
                    {
                        _tileQueue.Enqueue(tile);

                        iSleepMs = 1000;
                    }
                }

                Thread.Sleep(iSleepMs);
            }
        }
    }
}
