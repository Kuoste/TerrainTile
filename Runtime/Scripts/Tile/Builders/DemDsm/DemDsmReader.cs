
using LasUtility.VoxelGrid;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Kuoste.LidarWorld.Tile
{
    public class DemDsmReader : IDemDsmBuilder
    {
        public VoxelGrid Build(Tile tile)
        {
            if (tile.Token.IsCancellationRequested)
                return new VoxelGrid();

            string sFullFilename = Path.Combine(tile.DirectoryIntermediate, IDemDsmBuilder.Filename(tile.Name, tile.Version));

            VoxelGrid grid = VoxelGrid.Deserialize(sFullFilename);

            return grid;
        }
    }
}
