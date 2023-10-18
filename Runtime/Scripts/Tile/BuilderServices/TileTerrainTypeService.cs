using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Kuoste.LidarWorld.Tile
{
    public class TileTerrainTypeService : ITileBuilderService
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

        public TileTerrainTypeService(string sDirectoryOriginal, string sDirectoryIntermediate)
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
                    string sFullFilename = Path.Combine(_sDirectoryIntermediate, tile.FilenameTerrainType);

                    if (File.Exists(sFullFilename))
                    {
                        // Load raster from filesystem
                        reader.BuildTerrainTypeRaster(tile);
                    }
                    else
                    {
                        // Create raster from shapefiles
                        creator.BuildTerrainTypeRaster(tile);
                    }
                }

                Thread.Sleep(1000);
            }
        }

    }
}
