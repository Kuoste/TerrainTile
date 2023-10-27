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
        ITileBuilder _reader;
        ITileBuilder _creator;

        private readonly ConcurrentQueue<Tile> _tileQueue = new();

        public TileDsmPointCloudService(ITileBuilder reader, ITileBuilder creator)
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

                    string sFullFilename = Path.Combine(_reader.DirectoryIntermediate, tile.FilenameGrid);

                    if (File.Exists(sFullFilename))
                    {
                        // Load grid from filesystem
                        _reader.BuildDemAndDsmPointCloud(tile);
                    }
                    else
                    {
                        // Create grid from las files
                        _creator.BuildDemAndDsmPointCloud(tile);
                    }

                    sw.Stop();
                    Debug.Log($"Tile {tile.Name} built in {sw.ElapsedMilliseconds} ms.");
                }

                Thread.Sleep(1000);
            }
        }

    }
}
