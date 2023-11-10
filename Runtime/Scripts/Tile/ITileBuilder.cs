using LasUtility;
using LasUtility.Common;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Kuoste.LidarWorld.Tile
{
    public interface ITileBuilder
    {
        void SetCancellationToken(CancellationToken token);

        string DirectoryIntermediate { get; set; }
        string DirectoryOriginal { get; set; }

        ConcurrentDictionary<string, bool> DemDsmDone { get; }

        void BuildDemAndDsmPointCloud(Tile tile);

        void BuildRoadRaster(Tile tile);

        void BuildTerrainTypeRaster(Tile tile);

        void BuildBuildings(Tile tile);

        void BuildTrees(Tile tile);

        void BuildWaterAreas(Tile tile);

    }
}
