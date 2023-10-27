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
    public class TileBuildingService : ITileBuilderService
    {
        ITileBuilder _reader;
        ITileBuilder _creator;

        private readonly ConcurrentQueue<Tile> _tileQueue = new();

        public TileBuildingService(ITileBuilder reader, ITileBuilder creator)
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
                    string sFullFilename = Path.Combine(_reader.DirectoryIntermediate, tile.FilenameBuildings);

                    if (File.Exists(sFullFilename))
                    {
                        Stopwatch sw = Stopwatch.StartNew();

                        // Load from filesystem
                        _reader.BuildBuildingVertices(tile);

                        sw.Stop();
                        Debug.Log($"Tile {tile.Name} building vertices read in {sw.ElapsedMilliseconds} ms.");
                    }
                    else
                    {
                        // Buildings require surface heights to be available first
                        _creator.DemDsmDone.TryGetValue(tile.Name, out bool isDemDsmBuilt);

                        if (false == isDemDsmBuilt)
                            _reader.DemDsmDone.TryGetValue(tile.Name, out isDemDsmBuilt);

                        if (true == isDemDsmBuilt)
                        {
                            Stopwatch sw = Stopwatch.StartNew();

                            // Create from shapefiles and DSM
                            _creator.BuildBuildingVertices(tile);

                            sw.Stop();
                            Debug.Log($"Tile {tile.Name} building vertices created in {sw.ElapsedMilliseconds} ms.");

                            iSleepMs = 1000;
                        }
                        else
                        {
                            _tileQueue.Enqueue(tile);

                            iSleepMs = 10000;
                        }
                    }

                }

                Thread.Sleep(iSleepMs);
            }
        }
    }
}
