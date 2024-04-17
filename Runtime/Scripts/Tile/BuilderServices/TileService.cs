using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Tile
{
    public class TileService
    {
        protected ConcurrentQueue<Tile> _tileQueue = new();

        protected CancellationToken _token;

        public void AddTile(Tile tile)
        {
            _tileQueue.Enqueue(tile);
        }
    }
}
