using LasUtility;
using LasUtility.VoxelGrid;
using System.Collections.Generic;

namespace Kuoste.LidarWorld.Terrain
{
    public interface ITileProvider
    {
        Dictionary<string, VoxelGrid> GetTerrain(string sDirectory, string sMapTileName, string sVersion);
    }
}
