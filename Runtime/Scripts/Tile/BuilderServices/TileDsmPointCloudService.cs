using LasUtility.Nls;
using LasUtility.VoxelGrid;
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
    public class TileDsmPointCloudService : ITileBuilderService
    {
        private readonly IDemDsmBuilder _reader;
        private readonly IDemDsmBuilder _creator;

        private readonly ConcurrentQueue<Tile> _tileQueue = new();

        public TileDsmPointCloudService(IDemDsmBuilder reader, IDemDsmBuilder creator)
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

                    string sFullFilename = Path.Combine(tile.DirectoryIntermediate, IDemDsmBuilder.Filename(tile.Name, tile.Version));

                    if (File.Exists(sFullFilename))
                    {
                        // Load grid from filesystem
                        tile.DemDsm = _reader.Build(tile);
                    }
                    else
                    {
                        // Create grid from las files
                        tile.DemDsm = _creator.Build(tile);
                        //_creator.Build(tile);
                    }

                    Interlocked.Increment(ref tile.CompletedCountDemDsm);

                    sw.Stop();
                    Debug.Log($"Tile {tile.Name} DEM and voxelgrid built in {sw.Elapsed.TotalSeconds} s.");

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
