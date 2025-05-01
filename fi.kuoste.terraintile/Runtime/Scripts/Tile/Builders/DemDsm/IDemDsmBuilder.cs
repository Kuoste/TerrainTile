using LasUtility.Common;
using LasUtility.VoxelGrid;
using System.Collections.Concurrent;

namespace Kuoste.LidarWorld.Tile
{
    public interface IDemDsmBuilder : IBuilder
    {
        public static string Filename(string sName, string sVersion) => sName + "_DemDsm_v" + sVersion + ".voxelgrid";

        public VoxelGrid Build(Tile tile);
    }
}
