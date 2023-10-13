using LasUtility;
using LasUtility.Common;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace Kuoste.LidarWorld.Tile
{
    public interface ITileProvider
    {
        Dictionary<string, VoxelGrid> GetTerrain(string sDirectory, string sMapTileName, string sVersion);

        Dictionary<string, HeightMap> GetBuildingsAndRoads(string sDirectory, string sMapTileName, string sVersion);

        Dictionary<string, HeightMap> GetTerrainFeatures(string sDirectory, string sMapTileName, string sVersion);

        List<Polygon> GetBuildings(string sDirectory, string sMapTileName, string sVersion);
    }
}
