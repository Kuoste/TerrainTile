using LasUtility.VoxelGrid;
using System.Collections.Generic;
using System.IO;

namespace Kuoste.LidarWorld.Terrain
{
    public class TileReader : ITileProvider
    {
        public Dictionary<string, VoxelGrid> GetTerrain(string sDirectory, string sMapTileName, string sVersion)
        {
            string sFilename = Path.Combine(sDirectory, sMapTileName + "_v" + sVersion + ".obj");

            Dictionary<string, VoxelGrid> grids = new();

            if (File.Exists(sFilename))
            {
                grids.Add(sMapTileName, VoxelGrid.Deserialize(sFilename));
            }

            return grids;
        }
    }
}
