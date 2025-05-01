using NetTopologySuite.Geometries;
using System.Collections.Generic;
using Kuoste.TerrainEngine.Tiles;

namespace Kuoste.TerrainEngine.Interfaces.TileBuilders
{
    public interface IBuildingsBuilder : IBuilder
    {
        public static string Filename(string sName, string sVersion) => sName + "_buildings_v" + sVersion + ".geojson";

        public List<Tile.Building> Build(Tile tile);
    }
}
