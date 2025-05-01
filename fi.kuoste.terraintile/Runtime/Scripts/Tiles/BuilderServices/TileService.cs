using Kuoste.TerrainEngine.Common.Loggers;
using Kuoste.TerrainEngine.Common.Tiles;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Kuoste.TerrainTile.Tiles.BuilderServices
{
    public class TileService
    {
        protected ConcurrentQueue<Tile> _tileQueue = new();

        protected CancellationToken _token;

        protected CompositeLogger _logger;

        public void AddTile(Tile tile)
        {
            _tileQueue.Enqueue(tile);
        }
    }
}
