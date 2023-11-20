
using LasUtility.Nls;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kuoste.LidarWorld.Tile
{
    public class BuildingsReader : IBuildingsBuilder
    {
        public List<Tile.Building> Build(Tile tile)
        {
            List<Tile.Building> buildings = new();

            if (tile.Token.IsCancellationRequested)
                return buildings;

            TileNamer.Decode(tile.Name, out Envelope bounds);
            string sFullFilename = Path.Combine(tile.DirectoryIntermediate, IBuildingsBuilder.Filename(tile.Name, tile.Version));

            string[] sBuildings = File.ReadAllText(sFullFilename).Split("GeometryCollection");

            foreach (string sBuilding in sBuildings)
            {
                if (tile.Token.IsCancellationRequested)
                    return buildings;

                List<Vector3> buildingVertices = new();
                List<int> buildingTriangles = new();
                int iStartingTriangleIndexForWalls = 0;

                float fBuildingHeight = 0.0f;
                //Coordinate cMin = new(double.MaxValue, double.MaxValue);
                //Coordinate cMax = new(double.MinValue, double.MinValue);

                string[] sPolygons = sBuilding.Split("Polygon");

                for (int i = 0; i < sPolygons.Length; i++)
                {
                    List<CoordinateZ> coordinates = new();
                    string[] sCordinates = sPolygons[i].Split("[", StringSplitOptions.RemoveEmptyEntries);

                    foreach (string sCoordinate in sCordinates)
                    {
                        if (!char.IsDigit(sCoordinate[0]))
                            continue;

                        var coords = sCoordinate.Split(",", StringSplitOptions.RemoveEmptyEntries);

                        // Delete the last character which is a closing bracket and everyting after it
                        coords[2] = coords[2][..coords[2].IndexOf(']')];

                        coordinates.Add(new(
                            double.Parse(coords[0]),
                            double.Parse(coords[1]),
                            double.Parse(coords[2])));

                    }

                    if (coordinates.Count == 0)
                        continue;

                    if (i == sPolygons.Length - 1)
                    {
                        // Last polygon is walls

                        iStartingTriangleIndexForWalls = buildingTriangles.Count;

                        // Add wall vertices
                        for (int c = 0; c < coordinates.Count; c++)
                        {
                            Coordinate c1 = coordinates[c];

                            //cMin.X = Math.Min(cMin.X, c1.X);
                            //cMin.Y = Math.Min(cMin.Y, c1.Y);
                            //cMax.X = Math.Max(cMax.X, c1.X);
                            //cMax.Y = Math.Max(cMax.Y, c1.Y);

                            if (c > 0)
                            {
                                Coordinate c0 = coordinates[c - 1];

                                // Create a quad between the two points
                                int iVertexStart = buildingVertices.Count;
                                buildingVertices.Add(new Vector3((float)(c0.X - bounds.MinX), (float)c0.Z, (float)(c0.Y - bounds.MinY)));
                                buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX), (float)c1.Z, (float)(c1.Y - bounds.MinY)));
                                buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX), fBuildingHeight, (float)(c1.Y - bounds.MinY)));
                                buildingVertices.Add(new Vector3((float)(c0.X - bounds.MinX), fBuildingHeight, (float)(c0.Y - bounds.MinY)));

                                buildingTriangles.Add(iVertexStart);
                                buildingTriangles.Add(iVertexStart + 1);
                                buildingTriangles.Add(iVertexStart + 2);
                                buildingTriangles.Add(iVertexStart);
                                buildingTriangles.Add(iVertexStart + 2);
                                buildingTriangles.Add(iVertexStart + 3);
                            }
                        }
                    }
                    else
                    {
                        // Rest of the polygons are roof triangles

                        // Count should be 4 because the first and last coordinate are the same
                        if (coordinates.Count != 4)
                            throw new Exception("Invalid roof polygon format");

                        // Add roof vertices
                        Coordinate c0 = coordinates[0];
                        Coordinate c1 = coordinates[1];
                        Coordinate c2 = coordinates[2];

                        fBuildingHeight = (float)c0.Z;

                        int iVertexStart = buildingVertices.Count;
                        buildingVertices.Add(new Vector3((float)(c0.X - bounds.MinX),
                            fBuildingHeight, (float)(c0.Y - bounds.MinY)));
                        buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX),
                            fBuildingHeight, (float)(c1.Y - bounds.MinY)));
                        buildingVertices.Add(new Vector3((float)(c2.X - bounds.MinX),
                            fBuildingHeight, (float)(c2.Y - bounds.MinY)));

                        buildingTriangles.Add(iVertexStart);
                        buildingTriangles.Add(iVertexStart + 1);
                        buildingTriangles.Add(iVertexStart + 2);
                    }
                }

                if (iStartingTriangleIndexForWalls > 0)
                {
                    buildings.Add(new Tile.Building()
                    {
                        Vertices = buildingVertices.ToArray(),
                        Triangles = buildingTriangles.ToArray(),
                        iSubmeshSeparator = iStartingTriangleIndexForWalls,
                        //Bounds = new Envelope(cMin, cMax)
                    });
                }
            }

            return buildings;
        }
    }
}
