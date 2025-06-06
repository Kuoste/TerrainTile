using Kuoste.TerrainEngine.Common.Tiles;
using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace Kuoste.TerrainEngine.Common.Interfaces
{
    public interface IWaterAreasBuilder : IBuilder
    {
        public static string Filename(string sName, string sVersion) => sName + "_waterareas_v" + sVersion + ".geojson";

        public List<Polygon> Build(Tile tile);
    }
}
