using LasUtility.Common;

namespace Kuoste.LidarWorld.Tile
{
    public interface ITerrainTypeBuilder
    {
        public static string Filename(string sName, string sVersion) => sName + "_terraintype_v" + sVersion + ".asp";

        public IRaster Build(Tile tile);
    }
}
