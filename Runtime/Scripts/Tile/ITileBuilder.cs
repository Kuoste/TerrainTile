using LasUtility;
using LasUtility.VoxelGrid;
using System.Collections.Generic;

namespace Kuoste.LidarWorld.Terrain
{
    public interface ITerrainProvider
    {
        Dictionary<string, VoxelGrid> GetTerrain(string sDirectory, string sMapTileName, string sVersion);
    }
}
