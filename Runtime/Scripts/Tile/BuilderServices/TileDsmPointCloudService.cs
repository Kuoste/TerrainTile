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
    public class TileDsmPointCloudService : TileService, ITileBuilderService
    {
        private readonly IDemDsmBuilder _reader;
        private readonly IDemDsmBuilder _creator;

        public TileDsmPointCloudService(IDemDsmBuilder reader, IDemDsmBuilder creator, CancellationToken token)
        {
            _reader = reader;
            _creator = creator;

            _reader.SetCancellationToken(token);
            _creator.SetCancellationToken(token);

            _token = token;
        }

        public void BuilderThread()
        {
            while (true)
            {
                if (_token.IsCancellationRequested)
                    return;

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
                    }

                    Interlocked.Increment(ref tile.CompletedCount);

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
