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
        /// <summary>
        /// Folder where data from Nls is found
        /// </summary>
        private readonly string _sDirectoryOriginal;

        /// <summary>
        ///  Folder for saving the rasterised / triangulated data
        /// </summary>
        private readonly string _sDirectoryIntermediate;

        private readonly ConcurrentQueue<Tile> _tileQueue = new();

        public TileDsmPointCloudService(string sDirectoryOriginal, string sDirectoryIntermediate)
        {
            _sDirectoryOriginal = sDirectoryOriginal;
            _sDirectoryIntermediate = sDirectoryIntermediate;
        }

        public void AddTile(Tile tile)
        {
            _tileQueue.Enqueue(tile);
        }

        public void BuilderThread()
        {
            ITileBuilder reader = new TileReader();
            ITileBuilder creator = new TileCreator();

            reader.SetIntermediateDirectory(_sDirectoryIntermediate);
            creator.SetIntermediateDirectory(_sDirectoryIntermediate);
            creator.SetOriginalDirectory(_sDirectoryOriginal);

            while (true)
            {
                if (_tileQueue.Count > 0 && _tileQueue.TryDequeue(out Tile tile))
                {
                    Stopwatch sw = Stopwatch.StartNew();

                    string sFullFilename = Path.Combine(_sDirectoryIntermediate, tile.FilenameGrid);

                    if (File.Exists(sFullFilename))
                    {
                        // Load grid from filesystem
                        reader.BuildDemAndDsmPointCloud(tile);
                    }
                    else
                    {
                        // Create grid from las files
                        creator.BuildDemAndDsmPointCloud(tile);
                    }

                    sw.Stop();
                    Debug.Log($"Tile {tile.Name} built in {sw.ElapsedMilliseconds} ms.");
                }

                Thread.Sleep(1000);
            }
        }

    }
}
