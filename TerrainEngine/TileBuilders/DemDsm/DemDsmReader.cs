using Kuoste.TerrainEngine.Common.Interfaces;
using Kuoste.TerrainEngine.Common.Tiles;
using LasUtility.VoxelGrid;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Kuoste.TerrainEngine.TileBuilders.DemDsm
{
    public class DemDsmReader : Builder, IDemDsmBuilder
    {
        public VoxelGrid Build(Tile tile)
        {
            if (IsCancellationRequested())
                return new VoxelGrid();

            string sFullFilename = Path.Combine(tile.Common.DirectoryIntermediate, IDemDsmBuilder.Filename(tile.Name, tile.Common.Version));

            VoxelGrid grid = VoxelGrid.Deserialize(sFullFilename);

            return grid;
        }
    }
}
