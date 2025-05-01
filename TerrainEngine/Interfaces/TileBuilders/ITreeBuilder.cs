using Kuoste.TerrainEngine.Tiles;
using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace Kuoste.TerrainEngine.Interfaces.TileBuilders
{
    public interface ITreeBuilder : IBuilder
    {
        public static string Filename(string sName, string sVersion) => sName + "_trees_v" + sVersion + ".geojson";

        public List<Point> Build(Tile tile);
    }
}
