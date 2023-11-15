
using LasUtility.DEM;
using LasUtility.LAS;
using LasUtility.Nls;
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
        public List<Tile.Building> Build(Tile tile)
        {
            List<Tile.Building> buildings = new();

            if (tile.Token.IsCancellationRequested)
                return buildings;

            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);

            using StreamWriter streamWriter = new(Path.Combine(tile.DirectoryIntermediate, IBuildingsBuilder.Filename(tile.Name, tile.Version)));

            string sFullFilename = Path.Combine(tile.DirectoryOriginal, TopographicDb.sPrefixForBuildings + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");
            Feature[] features = Shapefile.ReadAllFeatures(sFullFilename);

            foreach (Feature f in features)
            {
                if (tile.Token.IsCancellationRequested)
                {
                    streamWriter.Close();
                    File.Delete(Path.Combine(tile.DirectoryIntermediate, IBuildingsBuilder.Filename(tile.Name, tile.Version)));
                    return buildings;
                }

                // Make sure f is inside bounds.
                Envelope featureBounds = f.Geometry.EnvelopeInternal;
                if (featureBounds.MinX < bounds.MinX || featureBounds.MaxX >= bounds.MaxX ||
                    featureBounds.MinY < bounds.MinY || featureBounds.MaxY >= bounds.MaxY)
                {
                    continue;
                }


                int classification = (int)(long)f.Attributes["LUOKKA"];

                if (false == TopographicDb.BuildingPolygonClassesToRasterValues.ContainsKey(classification))
                {
                    continue;
                }

                MultiPolygon mp = (MultiPolygon)f.Geometry;

                // Go through polygons in the multipolygon
                for (int j = 0; j < mp.NumGeometries; j++)
                {
                    Polygon buildingPolygon = (Polygon)mp.GetGeometryN(j);

                    LineString buildingExterior = buildingPolygon.ExteriorRing;

                    List<float> buildingHeights = new();
                    List<float> buildingGroundHeights = new();

                    for (int i = 0; i < buildingExterior.NumPoints; i++)
                    {
                        Coordinate c = buildingExterior.GetCoordinateN(i);

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
                    Envelope buildingBounds = new(
                        Math.Floor(buildingPolygon.EnvelopeInternal.MinX),
                        Math.Ceiling(buildingPolygon.EnvelopeInternal.MaxX),
                        Math.Floor(buildingPolygon.EnvelopeInternal.MinY),
                        Math.Ceiling(buildingPolygon.EnvelopeInternal.MaxY));

                    // For triangulation, move coordinates to origo
                    SurfaceTriangulation tri = new(
                        (int)(buildingBounds.MaxX - buildingBounds.MinX),
                        (int)(buildingBounds.MaxY - buildingBounds.MinY),
                        0, 0, buildingBounds.MaxX - buildingBounds.MinX,
                        buildingBounds.MaxY - buildingBounds.MinY);

                    // Make faster by skipping some coordinates
                    int iSkip = 2;
                    for (int x = (int)buildingBounds.MinX; x < buildingBounds.MaxX; x += iSkip)
                    {
                        for (int y = (int)buildingBounds.MinY; y < buildingBounds.MaxY; y += iSkip)
                        {
                            tile.DemDsm.GetGridIndexes(x, y, out int iRow, out int jCol);
                            BinPoint bp = tile.DemDsm.GetHighestPointInClassRange(iRow, jCol, 0, byte.MaxValue);

                            if (bp != null && buildingPolygon.Contains(new Point(x, y)))
                            {
                                tri.AddPoint(new LasPoint() { x = x - buildingBounds.MinX, y = y - buildingBounds.MinY, z = bp.Z });

                                buildingHeights.Add(bp.Z);
                            }
                        }
                    }

                    if (buildingHeights.Count < 10)
                    {
                        continue;
                    }

                    // Take a percentile of building heights. Aiming for the actual roof height, not the walls or overhanging trees.
                    buildingHeights.Sort();
                    float fBuildingHeight = buildingHeights[buildingHeights.Count / 40];

                    // Add also building corners to get the full roof
                    for (int i = 0; i < buildingExterior.NumPoints; i++)
                    {
                        Coordinate c = buildingExterior.GetCoordinateN(i);
                        tri.AddPoint(new LasPoint() { x = c.X - buildingBounds.MinX, y = c.Y - buildingBounds.MinY, z = fBuildingHeight });
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

                        c0.X = Math.Round(c0.X + buildingBounds.MinX, 2);
                        c0.Y = Math.Round(c0.Y + buildingBounds.MinY, 2);
                        c1.X = Math.Round(c1.X + buildingBounds.MinX, 2);
                        c1.Y = Math.Round(c1.Y + buildingBounds.MinY, 2);
                        c2.X = Math.Round(c2.X + buildingBounds.MinX, 2);
                        c2.Y = Math.Round(c2.Y + buildingBounds.MinY, 2);

                        // Skip extra segments on concave corners
                        Point center = new((c0.X + c1.X + c2.X) / 3, (c0.Y + c1.Y + c2.Y) / 3);
                        if (false == buildingPolygon.Contains(center))
                        {
                            continue;
                        }

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

                    // Add building boundaries
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

            streamWriter.Close();
            BuildingsReader reader = new();
            return reader.Build(tile);
        }
    }
}
