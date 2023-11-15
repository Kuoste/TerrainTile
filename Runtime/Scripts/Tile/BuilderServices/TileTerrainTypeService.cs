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
        private readonly ITerrainTypeBuilder _reader, _creator;

        private readonly ConcurrentQueue<Tile> _tileQueue = new();

        public TileTerrainTypeService(ITerrainTypeBuilder reader, ITerrainTypeBuilder creator)
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
                    //Stopwatch sw = Stopwatch.StartNew();

                    string sFullFilename = Path.Combine(tile.DirectoryIntermediate, ITerrainTypeBuilder.Filename(tile.Name, tile.Version));

                    if (File.Exists(sFullFilename))
                    {
                        // Load raster from filesystem
                        tile.TerrainType = _reader.Build(tile);
                    }
                    else
                    {
                        // Create raster from shapefiles
                        tile.TerrainType = _creator.Build(tile);
                    }

                    Interlocked.Increment(ref tile.CompletedCountOther);

                    //sw.Stop();
                    //Debug.Log($"Tile {tile.Name} terrain types built in {sw.ElapsedMilliseconds} ms.");

                    Thread.Sleep(10);
                }
                else
                {
                    Thread.Sleep(1000);
                }
            }
        }

    }
}
