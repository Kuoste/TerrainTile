using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Loggers;
using Kuoste.TerrainEngine.Common.Tiles;
using LasUtility.Nls;
using LasUtility.VoxelGrid;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Kuoste.TerrainTile.Tiles.BuilderServices
{ 
    public class TileDsmPointCloudService : TileService, ITileBuilderService
    {
        private readonly IDemDsmBuilder _reader;
        private readonly IDemDsmBuilder _creator;

        public TileDsmPointCloudService(IDemDsmBuilder reader, IDemDsmBuilder creator, 
            CancellationToken token, CompositeLogger logger)
        {
            _reader = reader;
            _creator = creator;
            _token = token;
            _logger = logger;
        }

        public void BuilderThread()
        {
            while (true)
            {
                if (_token != null && _token.IsCancellationRequested)
                    return;

                if (_tileQueue.Count > 0 && _tileQueue.TryDequeue(out Tile tile))
                {
                    Stopwatch sw = Stopwatch.StartNew();

                    string sFullFilename = Path.Combine(tile.Common.DirectoryIntermediate, IDemDsmBuilder.Filename(tile.Name, tile.Common.Version));

                    if (File.Exists(sFullFilename))
                    {
                        // Load grid from filesystem
                        tile.DemDsm = _reader.Build(tile);
                        sw.Stop();
                        _logger.LogInfo($"Tile {tile.Name} DEM and voxelgrid read in {sw.Elapsed.TotalSeconds} s.");
                    }
                    else
                    {
                        // Create grid from las files
                        tile.DemDsm = _creator.Build(tile);
                        sw.Stop();
                        _logger.LogInfo($"Tile {tile.Name} DEM and voxelgrid created in {sw.Elapsed.TotalSeconds} s.");
                    }

                    Interlocked.Increment(ref tile.CompletedCount);

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
