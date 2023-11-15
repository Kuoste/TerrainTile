using LasUtility.Common;

namespace Kuoste.LidarWorld.Tile
{
    public interface IRoadRasterBuilder
    {
        public static string Filename(string sName, string sVersion) => sName + "_roads_v" + sVersion + ".asp";

        public IRaster Build(Tile tile);
    }
}
