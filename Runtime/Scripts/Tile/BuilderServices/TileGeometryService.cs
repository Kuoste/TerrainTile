using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Tile
{
    public class TileGeometryService : ITileBuilderService
    {
        private readonly ITileBuilder _reader;
        private readonly ITileBuilder _creator;

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
            while (true)
            {
                if (_tileQueue.TryPeek(out Tile tile))
                {
                    // Buildings require surface heights to be available first
                    bool bIsDemDsmBuilt = false;

                    if (true == _creator.DemDsmDone.TryGetValue(tile.Name, out bool isDemDsmCreated))
                    {
                        bIsDemDsmBuilt = isDemDsmCreated;
                    }
                    else if (true == _reader.DemDsmDone.TryGetValue(tile.Name, out bool isDemDsmRead))
                    {
                        bIsDemDsmBuilt |= isDemDsmRead;
                    }

                    if (true == bIsDemDsmBuilt && _tileQueue.TryDequeue(out tile))
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
                    }
                }

                Thread.Sleep(100);
            }
        }
    }
}
