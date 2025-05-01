using NetTopologySuite.Geometries;
using System.Collections.Generic;
using Kuoste.TerrainEngine.Common.Tiles;

namespace Kuoste.TerrainEngine.Common.Interfaces
{
    public interface IBuildingsBuilder : IBuilder
    {
        public static string Filename(string sName, string sVersion) => sName + "_buildings_v" + sVersion + ".geojson";

        public List<Tile.Building> Build(Tile tile);
    }
}
