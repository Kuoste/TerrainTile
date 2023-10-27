using LasUtility;
using LasUtility.Common;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Kuoste.LidarWorld.Tile
{
    public interface ITileBuilder
    {
        string DirectoryIntermediate { get; set; }
        string DirectoryOriginal { get; set; }

        ConcurrentDictionary<string, bool> DemDsmDone { get; }

        void BuildDemAndDsmPointCloud(Tile tile);

        void BuildRoadRaster(Tile tile);

        void BuildTerrainTypeRaster(Tile tile);

        void BuildBuildingVertices(Tile tile);
    }
}
