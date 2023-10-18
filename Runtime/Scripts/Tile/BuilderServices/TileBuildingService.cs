using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Kuoste.LidarWorld.Tile
{
    public class TileBuildingService : ITileBuilderService
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

        public TileBuildingService(string sDirectoryOriginal, string sDirectoryIntermediate)
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
            //throw new System.NotImplementedException();
        }

    }
}
