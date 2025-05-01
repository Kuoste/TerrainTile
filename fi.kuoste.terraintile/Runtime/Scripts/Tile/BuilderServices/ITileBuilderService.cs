using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Kuoste.LidarWorld.Tile
{
    public interface ITileBuilderService
    {
        void AddTile(Tile tile);

        void BuilderThread();
    }
}
