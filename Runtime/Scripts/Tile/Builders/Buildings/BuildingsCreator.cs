using LasUtility.Common;
using LasUtility.DEM;
using LasUtility.LAS;
using LasUtility.Nls;
using LasUtility.ShapefileRasteriser;
using LasUtility.VoxelGrid;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kuoste.LidarWorld.Tile
{
    public class BuildingsCreator : IBuildingsBuilder
    {
        const int _iRequiredBuildingHeights = 10;
        const int _iPercentileForBuildingHeight = 80;

        public List<Tile.Building> Build(Tile tile)
        {
            if (tile.Token.IsCancellationRequested)
                return new();

            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);

            using StreamWriter streamWriter = new(Path.Combine(tile.DirectoryIntermediate, IBuildingsBuilder.Filename(tile.Name, tile.Version)));

            string sFullFilename = Path.Combine(tile.DirectoryOriginal, TopographicDb.sPrefixForBuildings + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");
            Feature[] features = Shapefile.ReadAllFeatures(sFullFilename);

            GeometryFactory factory = new();
            Geometry geometryTileBounds = factory.ToGeometry(bounds);

            foreach (Feature f in features)
            {
                if (tile.Token.IsCancellationRequested)
                {
                    streamWriter.Close();
                    File.Delete(Path.Combine(tile.DirectoryIntermediate, IBuildingsBuilder.Filename(tile.Name, tile.Version)));
                    return new();
                }

                int classification = (int)(long)f.Attributes["LUOKKA"];

                if (false == TopographicDb.BuildingPolygonClassesToRasterValues.ContainsKey(classification))
                {
                    continue;
                }

                Geometry intersection = f.Geometry.Intersection(geometryTileBounds);

                if (intersection == Polygon.Empty)
                {
                    // The whole building is outside the tile
                    continue;
                }

                // Go through polygons in the intersection
                for (int j = 0; j < intersection.NumGeometries; j++)
                {
                    LineString buildingExterior = ((Polygon)intersection.GetGeometryN(j)).ExteriorRing;

                    Polygon buildingPolygonExteriorOnly = new(new LinearRing(buildingExterior.Coordinates));

                    List<float> buildingHeights = new();
                    List<float> buildingGroundHeights = new();

                    for (int i = 0; i < buildingExterior.NumPoints; i++)
                    {
                        Coordinate c = buildingExterior.GetCoordinateN(i);

                        // Geometry.Intersection returns points that are on the higher bounds. Move them a little so that they are inside our area.
                        if (c.X == bounds.MaxX)
                            c.X -= RasterBounds.dEpsilon;
                        if (c.Y == bounds.MaxY)
                            c.Y -= RasterBounds.dEpsilon;

                        // Get building height at the coordinate
                        tile.DemDsm.GetGridIndexes(c.X, c.Y, out int iX, out int iY);
                        BinPoint bp = tile.DemDsm.GetHighestPointInClassRange(iX, iY, 0, byte.MaxValue);

                        if (bp != null)
                        {
                            buildingHeights.Add(bp.Z);
                        }

                        double dGroundHeight = tile.DemDsm.GetValue(c);
                        if (!double.IsNaN(dGroundHeight))
                        {
                            buildingGroundHeights.Add((float)dGroundHeight);
                        }
                    }

                    if (buildingGroundHeights.Count == 0)
                    {
                        Debug.Log("No ground height for a building found.");
                        continue;
                    }
                    buildingGroundHeights.Sort();

                    // Create roof triangulation

                    // Extend bounds to next integer coordinates

                    Envelope buildingBounds = buildingExterior.EnvelopeInternal;
                    Envelope buildingBoundsRounded = new(
                        Math.Floor(buildingBounds.MinX), Math.Ceiling(buildingBounds.MaxX),
                        Math.Floor(buildingBounds.MinY), Math.Ceiling(buildingBounds.MaxY));

                    // For triangulation, move coordinates to origo
                    SurfaceTriangulation tri = new(
                        (int)(buildingBoundsRounded.MaxX - buildingBoundsRounded.MinX),
                        (int)(buildingBoundsRounded.MaxY - buildingBoundsRounded.MinY),
                        0, 0, buildingBoundsRounded.MaxX - buildingBoundsRounded.MinX,
                        buildingBoundsRounded.MaxY - buildingBoundsRounded.MinY, false);

                    // Make faster by skipping some coordinates
                    int iSkip = 2;
                    for (int x = (int)buildingBoundsRounded.MinX; x < buildingBoundsRounded.MaxX; x += iSkip)
                    {
                        for (int y = (int)buildingBoundsRounded.MinY; y < buildingBoundsRounded.MaxY; y += iSkip)
                        {
                            tile.DemDsm.GetGridIndexes(x, y, out int iRow, out int jCol);
                            BinPoint bp = tile.DemDsm.GetHighestPointInClassRange(iRow, jCol, 0, byte.MaxValue);

                            if (bp != null && buildingPolygonExteriorOnly.Contains(new Point(x, y)))
                            {
                                tri.AddPoint(new LasPoint() { x = x - buildingBoundsRounded.MinX, y = y - buildingBoundsRounded.MinY, z = bp.Z });

                                buildingHeights.Add(bp.Z);
                            }
                        }
                    }

                    if (buildingHeights.Count < _iRequiredBuildingHeights)
                    {
                        continue;
                    }

                    // Take a percentile of building heights. Aiming for the actual roof height, not the walls, inner yards or overhanging trees.
                    buildingHeights.Sort();
                    float fBuildingHeight = buildingHeights[(int)(buildingHeights.Count / (double)100 * _iPercentileForBuildingHeight)];

                    // Add also building corners to get the full roof
                    for (int i = 0; i < buildingExterior.NumPoints; i++)
                    {
                        Coordinate c = buildingExterior.GetCoordinateN(i);
                        tri.AddPoint(new LasPoint() { x = c.X - buildingBoundsRounded.MinX, y = c.Y - buildingBoundsRounded.MinY, z = fBuildingHeight });
                    }

                    try
                    {
                        tri.Create();
                    }
                    catch (Exception e)
                    {
                        Debug.Log(e.Message);
                        continue;
                    }

                    // Start geometrycollection
                    streamWriter.WriteLine("{ \"type\":\"GeometryCollection\", \"geometries\": [");

                    // Add roof vertices
                    int iTriangleCount = tri.GetTriangleCount();
                    for (int i = 0; i < iTriangleCount; i++)
                    {
                        tri.GetTriangle(i, out Coordinate c0, out Coordinate c1, out Coordinate c2);

                        c0.X = Math.Round(c0.X + buildingBoundsRounded.MinX, 2);
                        c0.Y = Math.Round(c0.Y + buildingBoundsRounded.MinY, 2);
                        c1.X = Math.Round(c1.X + buildingBoundsRounded.MinX, 2);
                        c1.Y = Math.Round(c1.Y + buildingBoundsRounded.MinY, 2);
                        c2.X = Math.Round(c2.X + buildingBoundsRounded.MinX, 2);
                        c2.Y = Math.Round(c2.Y + buildingBoundsRounded.MinY, 2);

                        // Skip extra segments on concave corners
                        Point center = new((c0.X + c1.X + c2.X) / 3, (c0.Y + c1.Y + c2.Y) / 3);
                        if (false == buildingPolygonExteriorOnly.Contains(center))
                        {
                            continue;
                        }

                        WriteRoofTriangle(streamWriter, fBuildingHeight, c0, c1, c2);
                    }

                    // Add building boundaries
                    WriteBuildingPolygon(tile, streamWriter, buildingExterior, buildingGroundHeights);
                }
            }

            streamWriter.Close();

            BuildingsReader reader = new();
            return reader.Build(tile);
        }

        private static void WriteRoofTriangle(StreamWriter streamWriter, float fBuildingHeight, Coordinate c0, Coordinate c1, Coordinate c2)
        {
            // Start polygon
            streamWriter.Write("{ \"type\":\"Polygon\", \"coordinates\": ");
            streamWriter.Write("[[");

            streamWriter.Write($"[{c0.X},{c0.Y},{fBuildingHeight}],");
            streamWriter.Write($"[{c1.X},{c1.Y},{fBuildingHeight}],");
            streamWriter.Write($"[{c2.X},{c2.Y},{fBuildingHeight}],");
            streamWriter.Write($"[{c0.X},{c0.Y},{fBuildingHeight}]");

            // End polygon
            streamWriter.Write("]]");
            streamWriter.WriteLine("},");
        }

        private static void WriteBuildingPolygon(Tile tile, StreamWriter streamWriter, LineString buildingExterior, List<float> buildingGroundHeights)
        {
            streamWriter.Write("{ \"type\":\"Polygon\", \"coordinates\": ");
            streamWriter.Write("[[");

            for (int i = 0; i < buildingExterior.Coordinates.Length; i++)
            {
                Coordinate c = buildingExterior.Coordinates[i];

                double dGroundHeight = tile.DemDsm.GetValue(c);
                if (double.IsNaN(dGroundHeight))
                {
                    // buildingGroundHeights is sorted, so the first value is the lowest
                    dGroundHeight = buildingGroundHeights[0];
                }

                streamWriter.Write($"[{Math.Round(c.X, 2)},{Math.Round(c.Y, 2)},{dGroundHeight}]");

                if (i < buildingExterior.Coordinates.Length - 1)
                {
                    streamWriter.Write(",");
                }
            }

            // End polygon
            streamWriter.Write("]]");
            streamWriter.WriteLine("},");

            // End geometry collection
            streamWriter.WriteLine("]}");
        }
    }
}
