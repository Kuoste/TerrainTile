using LasUtility.Common;
using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace Kuoste.LidarWorld.Tile
{
    public interface IBuildingsBuilder : IBuilder
    {
        public static string Filename(string sName, string sVersion) => sName + "_buildings_v" + sVersion + ".geojson";

        public List<Tile.Building> Build(Tile tile);
    }
}
