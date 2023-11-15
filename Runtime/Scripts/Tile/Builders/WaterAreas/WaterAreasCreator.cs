using Kuoste.LidarWorld.Tile;
using LasUtility.Nls;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using System;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;

public class IWaterAreasCreator : IWaterAreasBuilder
{
    public List<Polygon> Build(Tile tile)
    {
        List<Polygon> waterAreas = new();

        if (tile.Token.IsCancellationRequested)
            return waterAreas;

        // Get topographic db tile name
        TileNamer.Decode(tile.Name, out Envelope envBounds);
        GeometryFactory factory = new();
        Geometry bounds = factory.ToGeometry(envBounds);
        string s12km12kmMapTileName = TileNamer.Encode((int)envBounds.MinX, (int)envBounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);

        string sFullFilename = Path.Combine(tile.DirectoryOriginal, TopographicDb.sPrefixForTerrainType + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");

        Feature[] features = Shapefile.ReadAllFeatures(sFullFilename);

        using StreamWriter streamWriter = new(Path.Combine(tile.DirectoryIntermediate, IWaterAreasBuilder.Filename(tile.Name, tile.Version)));

        foreach (Feature f in features)
        {
            if (tile.Token.IsCancellationRequested)
            {
                streamWriter.Close();
                File.Delete(Path.Combine(tile.DirectoryIntermediate, IWaterAreasBuilder.Filename(tile.Name, tile.Version)));
                return waterAreas;
            }

            int classification = (int)(long)f.Attributes["LUOKKA"];

            if (true == TopographicDb.WaterPolygonClassesToRasterValues.ContainsKey(classification))
            {
                Geometry intersection = f.Geometry.Intersection(bounds);

                if (intersection != Polygon.Empty)
                {
                    for (int g = 0; g < intersection.NumGeometries; g++)
                    {
                        Polygon p = (Polygon)intersection.GetGeometryN(g);

                        Point pointForLakeSurface = p.InteriorPoint;
                        float fHeight = (float)Math.Round(tile.DemDsm.GetValue(new Coordinate(pointForLakeSurface.X, pointForLakeSurface.Y)), 2);

                        List<CoordinateZ> coords = new();
                        foreach (Coordinate c in p.ExteriorRing.Coordinates)
                        {
                            coords.Add(new CoordinateZ(Math.Round(c.X, 2), Math.Round(c.Y, 2), fHeight));
                        }

                        streamWriter.Write("{ \"type\":\"Polygon\", \"coordinates\": ");
                        streamWriter.Write("[[");

                        for (int i = 0; i < coords.Count; i++)
                        {
                            streamWriter.Write($"[{coords[i].X},{coords[i].Y},{coords[i].Z}]");

                            if (i < coords.Count - 1)
                            {
                                streamWriter.Write(",");
                            }
                        }

                        // End polygon
                        streamWriter.Write("]]");
                        streamWriter.WriteLine("}");

                        waterAreas.Add(new Polygon(new LinearRing(coords.ToArray())));
                    }
                }
            }
        }

        return waterAreas;
    }
}
