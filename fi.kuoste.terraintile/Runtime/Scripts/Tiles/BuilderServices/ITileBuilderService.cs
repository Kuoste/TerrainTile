using Kuoste.TerrainEngine.Common.Tiles;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Kuoste.TerrainTile.Tiles.BuilderServices
{
    public interface ITileBuilderService
    {
        void AddTile(Tile tile);

        void BuilderThread();
    }
}
