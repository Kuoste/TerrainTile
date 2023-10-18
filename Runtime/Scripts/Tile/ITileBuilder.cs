using LasUtility;
using LasUtility.Common;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace Kuoste.LidarWorld.Tile
{
    public interface ITileBuilder
    {
        void SetOriginalDirectory(string sDirectory);
        void SetIntermediateDirectory(string sDirectory);

        void BuildDemAndDsmPointCloud(Tile tile);

        void BuildRoadRaster(Tile tile);

        void BuildTerrainTypeRaster(Tile tile);

        void BuildBuildingPolygons(Tile tile);
    }
}
