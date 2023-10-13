using LasUtility.Common;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System.Collections.Generic;
using System.IO;

namespace Kuoste.LidarWorld.Tile
{
    public class TileReader : ITileProvider
    {
        public List<Polygon> GetBuildings(string sDirectory, string sMapTileName, string sVersion)
        {
            throw new System.NotImplementedException();
        }

        public Dictionary<string, HeightMap> GetBuildingsAndRoads(string sDirectory, string sMapTileName, string sVersion)
        {
            throw new System.NotImplementedException();
        }

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

        public Dictionary<string, HeightMap> GetTerrainFeatures(string sDirectory, string sMapTileName, string sVersion)
        {
            throw new System.NotImplementedException();
        }
    }
}
