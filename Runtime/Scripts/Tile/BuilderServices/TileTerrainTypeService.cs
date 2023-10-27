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
    public class TileTerrainTypeService : ITileBuilderService
    {
        ITileBuilder _reader;
        ITileBuilder _creator;

        private readonly ConcurrentQueue<Tile> _tileQueue = new();

        public TileTerrainTypeService(ITileBuilder reader, ITileBuilder creator)
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
                if (_tileQueue.Count > 0 && _tileQueue.TryDequeue(out Tile tile))
                {
                    Stopwatch sw = Stopwatch.StartNew();

                    string sFullFilename = Path.Combine(_reader.DirectoryIntermediate, tile.FilenameTerrainType);

                    if (File.Exists(sFullFilename))
                    {
                        // Load raster from filesystem
                        _reader.BuildTerrainTypeRaster(tile);
                    }
                    else
                    {
                        // Create raster from shapefiles
                        _creator.BuildTerrainTypeRaster(tile);
                    }

                    sw.Stop();
                    Debug.Log($"Tile {tile.Name} terrain types built in {sw.ElapsedMilliseconds} ms.");
                }

                Thread.Sleep(1000);
            }
        }

    }
}
