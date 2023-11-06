using LasUtility.Common;
using LasUtility.Nls;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Kuoste.LidarWorld.Tile
{
    public class TileReader : ITileBuilder
    {
        public string DirectoryIntermediate { get; set; }
        public string DirectoryOriginal { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public ConcurrentDictionary<string, bool> DemDsmDone => _1kmDemDsmDone;
        public ConcurrentDictionary<string, bool> _1kmDemDsmDone = new();

        public void BuildBuildings(Tile tile)
        {
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameBuildings);

            tile.BuildingVertices = new();
            tile.BuildingTriangles = new();
            tile.BuildingSubmeshSeparator = new();

            string[] sBuildings = File.ReadAllText(sFullFilename).Split("GeometryCollection");

            foreach (string sBuilding in sBuildings)
            {
                List<Vector3> buildingVertices = new();
                List<int> buildingTriangles = new();
                int iStartingTrinagleIndexForWalls = 0;

                float fBuildingHeight = 0.0f;

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
                        coords[2] = coords[2].Substring(0, coords[2].IndexOf(']'));

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

                        iStartingTrinagleIndexForWalls = buildingTriangles.Count;

                        // Add wall vertices
                        for (int c = 1; c < coordinates.Count; c++)
                        {
                            Coordinate c0 = coordinates[c - 1];
                            Coordinate c1 = coordinates[c];

                            // Create a quad between the two points
                            int iVertexStart = buildingVertices.Count;
                            buildingVertices.Add(new Vector3((float)(c0.X - bounds.MinX), (float)c0.Z * tile.DemMaxHeight, (float)(c0.Y - bounds.MinY)));
                            buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX), (float)c1.Z * tile.DemMaxHeight, (float)(c1.Y - bounds.MinY)));
                            buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX), fBuildingHeight * tile.DemMaxHeight, (float)(c1.Y - bounds.MinY)));
                            buildingVertices.Add(new Vector3((float)(c0.X - bounds.MinX), fBuildingHeight * tile.DemMaxHeight, (float)(c0.Y - bounds.MinY)));

                            buildingTriangles.Add(iVertexStart);
                            buildingTriangles.Add(iVertexStart + 1);
                            buildingTriangles.Add(iVertexStart + 2);
                            buildingTriangles.Add(iVertexStart);
                            buildingTriangles.Add(iVertexStart + 2);
                            buildingTriangles.Add(iVertexStart + 3);
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
                            (float)(fBuildingHeight * tile.DemMaxHeight), (float)(c0.Y - bounds.MinY)));
                        buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX),
                            (float)(fBuildingHeight * tile.DemMaxHeight), (float)(c1.Y - bounds.MinY)));
                        buildingVertices.Add(new Vector3((float)(c2.X - bounds.MinX),
                            (float)(fBuildingHeight * tile.DemMaxHeight), (float)(c2.Y - bounds.MinY)));

                        buildingTriangles.Add(iVertexStart);
                        buildingTriangles.Add(iVertexStart + 1);
                        buildingTriangles.Add(iVertexStart + 2);
                    }
                }

                if (iStartingTrinagleIndexForWalls > 0)
                {
                    tile.BuildingVertices.Add(buildingVertices.ToArray());
                    tile.BuildingTriangles.Add(buildingTriangles.ToArray());
                    tile.BuildingSubmeshSeparator.Add(iStartingTrinagleIndexForWalls);
                }
            }

            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildRoadRaster(Tile tile)
        {
            string sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameRoads);

            tile.Roads = HeightMap.CreateFromAscii(sFullFilename);

            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildDemAndDsmPointCloud(Tile tile)
        {
            string sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameGrid);

            tile.TerrainGrid = VoxelGrid.Deserialize(sFullFilename);

            _1kmDemDsmDone.TryAdd(tile.Name, true);
            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildTerrainTypeRaster(Tile tile)
        {
            string sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameTerrainType);

            // Load terrain type raster

            tile.TerrainType = HeightMap.CreateFromAscii(sFullFilename);

            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void SetIntermediateDirectory(string sDirectory)
        {
            DirectoryIntermediate = sDirectory;
        }

        public void SetOriginalDirectory(string sDirectory)
        {
            throw new System.NotImplementedException();
        }

        public void BuildTrees(Tile tile)
        {
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameTrees);

            tile.Trees = new();

            string[] sTrees = File.ReadAllText(sFullFilename).Split("Point");

            foreach (string sTree in sTrees)
            {
                string[] sCordinates = sTree.Split("[", StringSplitOptions.RemoveEmptyEntries);

                foreach (string sCoordinate in sCordinates)
                {
                    if (!char.IsDigit(sCoordinate[0]))
                        continue;

                    var coords = sCoordinate.Split(",", StringSplitOptions.RemoveEmptyEntries);

                    // Delete the last character which is a closing bracket and everyting after it
                    coords[2] = coords[2].Substring(0, coords[2].IndexOf(']'));

                    tile.Trees.Add(new(
                        (double.Parse(coords[0]) - bounds.MinX) / Tile.EdgeLength,
                        (double.Parse(coords[1]) - bounds.MinY) / Tile.EdgeLength,
                        double.Parse(coords[2])));
                }
            }

            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildWaterAreas(Tile tile)
        {
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameWaterAreas);

            tile.WaterAreas = new();

            string[] sAreas = File.ReadAllText(sFullFilename).Split("Polygon");

            foreach (string sArea in sAreas)
            {
                string[] sCordinates = sArea.Split("[", StringSplitOptions.RemoveEmptyEntries);

                List<CoordinateZ> coordinates = new();

                foreach (string sCoordinate in sCordinates)
                {
                    if (!char.IsDigit(sCoordinate[0]))
                        continue;

                    var coords = sCoordinate.Split(",", StringSplitOptions.RemoveEmptyEntries);

                    // Delete the last character which is a closing bracket and everyting after it
                    coords[2] = coords[2].Substring(0, coords[2].IndexOf(']'));

                    coordinates.Add(new(
                        double.Parse(coords[0]),
                        double.Parse(coords[1]),
                        double.Parse(coords[2])));
                }

                if (coordinates.Count > 0)
                {
                    tile.WaterAreas.Add(new Polygon(new LinearRing(coordinates.ToArray())));
                }
            }

            Interlocked.Increment(ref tile.CompletedCount);
        }
    }
}
