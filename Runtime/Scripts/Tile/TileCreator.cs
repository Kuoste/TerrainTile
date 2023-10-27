using LasUtility.Common;
using LasUtility.DEM;
using LasUtility.LAS;
using LasUtility.Nls;
using LasUtility.ShapefileRasteriser;
using LasUtility.VoxelGrid;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using NetTopologySuite.IO.Esri.Shapefiles.Readers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Tile
{
    public class TileCreator : ITileBuilder
    {
        /// <summary>
        /// Currently only the 3x3 km2 las tiles are supported.
        /// </summary>
        const int m_iSupportedInputTileWidth = 3000;

        /// <summary>
        /// Unity supports heightmap resolutions as (2^n + 1) so 1025 is the closest to 1 km
        /// </summary>
        const int m_iUnityHeightmapResolution = 1025;

        const int m_iUnityAlphamapResolution = 1024;

        /// <summary>
        /// Use some overlap in triangulations or else the triangulations won't be complete on edges
        /// </summary>
        const int m_iOverlap = 50;

        /// <summary>
        /// Total triangulation edge length
        /// </summary>
        const int m_iTotalEdgeLength = Tile.EdgeLength + 2 * m_iOverlap;

        public string DirectoryIntermediate { get; set; }
        public string DirectoryOriginal { get; set; }

        public ConcurrentDictionary<string, bool> DemDsmDone => _1kmDemDsmDone;

        /// <summary>
        /// Keep track of the las files so that we don't try to process the same tile multiple times.
        /// </summary>
        private ConcurrentDictionary<string, bool> _1kmDemDsmDone = new();

        /// <summary>
        /// Keep track of the roads shapefiles so that we don't try to process the same file multiple times.
        /// </summary>
        private ConcurrentDictionary<string, bool> _12kmRoadsDone = new();

        /// <summary>
        /// Keep track of the buildings shapefiles so that we don't try to process the same tile multiple times.
        /// </summary>
        private ConcurrentDictionary<string, bool> _1kmBuildingsDone = new();

        /// <summary>
        /// Keep track of the terrain type shapefiles so that we don't try to process the same file multiple times.
        /// </summary>
        private ConcurrentDictionary<string, bool> _12kmTerrainTypesDone = new();

        ///// <summary>
        ///// Keep track of the received tiles so that we can update the tile features when the tile is ready.
        ///// </summary>
        //private ConcurrentDictionary<string, Tile> _tilesReceived = new();

        public void BuildDemAndDsmPointCloud(Tile tile)
        {
            // Check if the tile is already being processed
            if (true == _1kmDemDsmDone.TryGetValue(tile.Name, out bool bIsCompleted))
            {
                if (bIsCompleted)
                {
                    // Las file is already processed, so just update the tile.
                    tile.TerrainGrid = VoxelGrid.Deserialize(Path.Combine(DirectoryIntermediate, tile.FilenameGrid));
                    Interlocked.Increment(ref tile.CompletedCount);

                    Debug.Log($"DemAndDsmPointCloud for {tile.Name} is already completed.");
                }
                else
                {
                    Debug.Log($"DemAndDsmPointCloud for {tile.Name} is under work.");
                }

                return;
            }

            // Add 1km2 tile names to the dictionary
            TileNamer.Decode(tile.Name, out Envelope bounds1km);
            string s3km3kmTileName = TileNamer.Encode((int)bounds1km.MinX, (int)bounds1km.MinY, m_iSupportedInputTileWidth);
            TileNamer.Decode(s3km3kmTileName, out Envelope bounds3km);

            for (double x = bounds3km.MinX; x < bounds3km.MaxX; x += Tile.EdgeLength)
            {
                for (double y = bounds3km.MinY; y < bounds3km.MaxY; y += Tile.EdgeLength)
                {
                    string s1km1kmTilename = TileNamer.Encode((int)x, (int)y, Tile.EdgeLength);
                    _1kmDemDsmDone.TryAdd(s1km1kmTilename, false);
                }
            }

            ILasFileReader reader = new LasZipFileReader();

            string sFilename = Path.Combine(DirectoryOriginal, s3km3kmTileName + ".laz");

            reader.ReadHeader(sFilename);

            Stopwatch sw = Stopwatch.StartNew();

            reader.OpenReader(sFilename);

            double dMaxGroundHeight = double.MinValue;
            LasPoint p;

            int iSubmeshesPerEdge = (int)Math.Round((reader.MaxX - reader.MinX) / Tile.EdgeLength);
            int iSubmeshCount = (int)Math.Pow(iSubmeshesPerEdge, 2);

            SurfaceTriangulation[] triangulations = new SurfaceTriangulation[iSubmeshCount];
            VoxelGrid[] grids = new VoxelGrid[iSubmeshCount];

            for (int i = 0; i < iSubmeshCount; i++)
            {
                // Create the NLS (Maanmittauslaitos) style name of a 1x1 km2 tile in order to get the coordinates.
                string sSubmeshName = s3km3kmTileName + "_" + (i + 1).ToString();
                TileNamer.Decode(sSubmeshName, out Envelope extent);

                grids[i] = VoxelGrid.CreateGrid(m_iUnityHeightmapResolution, m_iUnityHeightmapResolution, extent);

                triangulations[i] = new SurfaceTriangulation(m_iTotalEdgeLength, m_iTotalEdgeLength,
                    extent.MinX - m_iOverlap, extent.MinY - m_iOverlap,
                    extent.MaxX + m_iOverlap, extent.MaxY + m_iOverlap);
            }

            while ((p = reader.ReadPoint()) != null)
            {
                p.z /= tile.DemMaxHeight;

                double x = p.x;
                double y = p.y;
                double z = p.z;

                // Get submesh indices
                x -= reader.MinX;
                y -= reader.MinY;
                int ix = (int)x / Tile.EdgeLength;
                int iy = (int)y / Tile.EdgeLength;
                int iSubmeshIndex = ix * iSubmeshesPerEdge + iy;

                // Classifications from
                // https://www.maanmittauslaitos.fi/kartat-ja-paikkatieto/asiantuntevalle-kayttajalle/tuotekuvaukset/laserkeilausaineisto-05-p
                if (p.classification == (byte)PointCloud05p.Classes.Ground)
                {
                    //dMinGroundHeight = Math.Min(p.y, dMinGroundHeight);
                    dMaxGroundHeight = Math.Max(p.y, dMaxGroundHeight);

                    // Index sanity check
                    if (ix < 0 || ix >= iSubmeshesPerEdge || iy < 0 || iy >= iSubmeshesPerEdge)
                    {
                        Debug.LogFormat("Coordinates of a point (x={0}, y={1} are outside the area defined in the file {2} header ", x, y, sFilename);
                        continue;
                    }

                    // Height sanity check
                    if (z < 0 || z > short.MaxValue)
                    {
                        Debug.Log("Point has invalid height " + z);
                        continue;
                    }

                    triangulations[iSubmeshIndex].AddPoint(p);

                    // Also add the ground point to the grid, so we don't have to query heights to cells where we already have a height.
                    grids[iSubmeshIndex].AddPoint(p.x, p.y, (float)p.z, p.classification, true);


                    // Look if point is part of another submesh overlap area.
                    // Overlap is needed because otherwise adjacent triangulated surfaces have a gap in between.

                    int iWholeMeshEdgeLength = Tile.EdgeLength * iSubmeshesPerEdge;
                    int iOverlapInMeters = m_iOverlap;
                    float dOverlapPercentageLowBound = (float)iOverlapInMeters / Tile.EdgeLength;
                    float dOverlapPercentageHighBound = 1 - dOverlapPercentageLowBound;

                    float dPercentageX = (float)x / Tile.EdgeLength - ix;
                    float dPercentageY = (float)y / Tile.EdgeLength - iy;

                    if (dPercentageX < dOverlapPercentageLowBound || dPercentageX > dOverlapPercentageHighBound ||
                        dPercentageY < dOverlapPercentageLowBound || dPercentageY > dOverlapPercentageHighBound)
                    {
                        // This point belongs to an extended area of one or more other submesh.

                        if (x < iOverlapInMeters || x > (iWholeMeshEdgeLength - iOverlapInMeters) ||
                            y < iOverlapInMeters || y > (iWholeMeshEdgeLength - iOverlapInMeters))
                        {
                            // Part of another file. Todo: Save these points to four separate files
                            // so they can be read when adjacent laz files are processed.
                            continue;
                        }

                        if (dPercentageX < dOverlapPercentageLowBound)
                        {
                            ix = (int)(x - iOverlapInMeters) / Tile.EdgeLength;
                            iy = (int)y / Tile.EdgeLength;

                            if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                            {
                                int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                triangulations[iOverlapSubmeshIndex].AddPoint(p);
                            }

                            if (dPercentageY < dOverlapPercentageLowBound)
                            {
                                //ix = (int)(x - iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)(y - iOverlapInMeters) / Tile.EdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                ix = (int)x / Tile.EdgeLength;
                                //iy = (int)(y - iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }

                            if (dPercentageY > dOverlapPercentageHighBound)
                            {
                                //ix = (int)(x - iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)(y + iOverlapInMeters) / Tile.EdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                ix = (int)x / Tile.EdgeLength;
                                //iy = (int)(y + iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }
                        }


                        if (dPercentageX > dOverlapPercentageHighBound)
                        {

                            ix = (int)(x + iOverlapInMeters) / Tile.EdgeLength;
                            iy = (int)y / Tile.EdgeLength;

                            if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                            {
                                int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                triangulations[iOverlapSubmeshIndex].AddPoint(p);
                            }

                            if (dPercentageY < dOverlapPercentageLowBound)
                            {
                                //ix = (int)(x + iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)(y - iOverlapInMeters) / Tile.EdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                ix = (int)x / Tile.EdgeLength;
                                //iy = (int)(y - iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }

                            if (dPercentageY > dOverlapPercentageHighBound)
                            {
                                //ix = (int)(x + iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)(y + iOverlapInMeters) / Tile.EdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                ix = (int)x / Tile.EdgeLength;
                                //iy = (int)(y + iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }
                        }


                        if (dPercentageY < dOverlapPercentageLowBound)
                        {
                            ix = (int)x / Tile.EdgeLength;
                            iy = (int)(y - iOverlapInMeters) / Tile.EdgeLength;

                            if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                            {
                                int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                triangulations[iOverlapSubmeshIndex].AddPoint(p);
                            }

                            if (dPercentageX < dOverlapPercentageLowBound)
                            {
                                ix = (int)(x - iOverlapInMeters) / Tile.EdgeLength;
                                //iy = (int)(y - iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                //ix = (int)(x - iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)y / Tile.EdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }

                            if (dPercentageX > dOverlapPercentageHighBound)
                            {
                                ix = (int)(x + iOverlapInMeters) / Tile.EdgeLength;
                                //iy = (int)(y - iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                //ix = (int)(x + iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)y / Tile.EdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }
                        }

                        if (dPercentageY > dOverlapPercentageHighBound)
                        {
                            ix = (int)x / Tile.EdgeLength;
                            iy = (int)(y + iOverlapInMeters) / Tile.EdgeLength;

                            if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                            {
                                int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                triangulations[iOverlapSubmeshIndex].AddPoint(p);
                            }

                            if (dPercentageX < dOverlapPercentageLowBound)
                            {
                                ix = (int)(x - iOverlapInMeters) / Tile.EdgeLength;
                                //iy = (int)(y + iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                //ix = (int)(x - iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)y / Tile.EdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }

                            if (dPercentageX > dOverlapPercentageHighBound)
                            {
                                ix = (int)(x + iOverlapInMeters) / Tile.EdgeLength;
                                //iy = (int)(y + iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                //ix = (int)(x + iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)y / Tile.EdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }
                        }
                    }
                }
                else if (p.classification == (byte)PointCloud05p.Classes.LowVegetation ||
                    p.classification == (byte)PointCloud05p.Classes.MedVegetation ||
                    p.classification == (byte)PointCloud05p.Classes.HighVegetation)
                {
                    grids[iSubmeshIndex].AddPoint(p.x, p.y, (float)p.z, p.classification, false);
                }
            }

            reader.CloseReader();

            for (int i = 0; i < iSubmeshCount; i++)
            {
                Stopwatch sw2 = Stopwatch.StartNew();

                SurfaceTriangulation tri = triangulations[i];
                VoxelGrid grid = grids[i];

                grid.SortAndTrim();

                tri.Create();

                // Use the name of a 1x1 km2 tile to get the coordinates
                string sSubmeshName = s3km3kmTileName + "_" + (i + 1).ToString();
                TileNamer.Decode(sSubmeshName, out Envelope env);

                grid.SetMissingHeightsFromTriangulation(tri,
                    (int)env.MinX, (int)env.MinY, (int)env.MaxX, (int)env.MaxY,
                    out int iMissBefore, out int iMissAfter);

                // Free triangulation asap so we dont run out of memory.
                tri.Clear();

                sw2.Stop();
                Debug.Log($"Triangulation {i} took {sw2.Elapsed} ms. Empty cells before {iMissBefore}, after {iMissAfter}.");
            }

            for (int i = 0; i < iSubmeshCount; i++)
            {
                string s1km1kmTilename = s3km3kmTileName + "_" + (i + 1).ToString();

                Tile t = new() { Name = s1km1kmTilename, Version = tile.Version };

                // Save grid to filesystem for future use
                grids[i].Serialize(Path.Combine(DirectoryIntermediate, t.FilenameGrid));
                
                _1kmDemDsmDone.TryUpdate(s1km1kmTilename, true, false);
            }

            Interlocked.Increment(ref tile.CompletedCount);

            Debug.Log($"Las processing finished! Total time for tile {s3km3kmTileName} was {sw.Elapsed.TotalSeconds} seconds.");
        }

        public void BuildTerrainTypeRaster(Tile tile)
        {
            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);
            TileNamer.Decode(s12km12kmMapTileName, out Envelope bounds12km);

            // Check if the tile is already being processed and add it to the dictionary if not.
            if (true == _12kmTerrainTypesDone.TryGetValue(s12km12kmMapTileName, out bool bIsCompleted))
            {
                if (bIsCompleted)
                {
                    // Shapefile is already processed, so just update the tile.
                    tile.TerrainType = HeightMap.CreateFromAscii(Path.Combine(DirectoryIntermediate, tile.FilenameTerrainType));
                    Interlocked.Increment(ref tile.CompletedCount);

                    Debug.Log($"TerrainTypeRaster {s12km12kmMapTileName} for {tile.Name} was already completed.");
                }
                else
                {
                    Debug.Log($"TerrainTypeRaster {s12km12kmMapTileName} for {tile.Name} is under work.");
                }

                return;
            }

            _12kmTerrainTypesDone.TryAdd(s12km12kmMapTileName, false);

            Rasteriser rasteriser = new();
            int iRowAndColCount = TopographicDb.iMapTileEdgeLengthInMeters / Tile.EdgeLength * m_iUnityAlphamapResolution;
            rasteriser.InitializeRaster(iRowAndColCount, iRowAndColCount, bounds12km);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.WaterPolygonClassesToRasterValues);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.WaterLineClassesToRasterValues);

            string sFullFilename = Path.Combine(DirectoryOriginal, TopographicDb.sPrefixForTerrainType + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");
            rasteriser.AddShapefile(sFullFilename);


            for (int x = (int)bounds12km.MinX; x < (int)bounds12km.MaxX; x += Tile.EdgeLength)
            {
                for (int y = (int)bounds12km.MinY; y < (int)bounds12km.MaxY; y += Tile.EdgeLength)
                {
                    Tile t = new() { Name = TileNamer.Encode(x, y, Tile.EdgeLength), Version = tile.Version };

                    // Save to filesystem
                    rasteriser.WriteAsAscii(Path.Combine(DirectoryIntermediate, t.FilenameTerrainType), x, y, x + Tile.EdgeLength, y + Tile.EdgeLength);
                }
            }

            _12kmTerrainTypesDone.TryUpdate(s12km12kmMapTileName, true, false);
            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildRoadRaster(Tile tile)
        {
            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);
            TileNamer.Decode(s12km12kmMapTileName, out Envelope bounds12km);

            // Check if the tile is already being processed and add it to the dictionary if not.
            if (true == _12kmRoadsDone.TryGetValue(s12km12kmMapTileName, out bool bIsCompleted))
            {
                if (bIsCompleted)
                {
                    // Shapefile is already processed, so just update the tile.
                    tile.Roads = HeightMap.CreateFromAscii(Path.Combine(DirectoryIntermediate, tile.FilenameRoads));
                    Interlocked.Increment(ref tile.CompletedCount);

                    Debug.Log($"RoadRaster {s12km12kmMapTileName} for {tile.Name} was already completed.");
                }
                else
                {
                    Debug.Log($"RoadRaster {s12km12kmMapTileName} for {tile.Name} is under work.");
                }

                return;
            }

            _12kmRoadsDone.TryAdd(s12km12kmMapTileName, false);

            Rasteriser rasteriser = new();
            int iRowAndColCount = TopographicDb.iMapTileEdgeLengthInMeters / Tile.EdgeLength * m_iUnityAlphamapResolution;
            rasteriser.InitializeRaster(iRowAndColCount, iRowAndColCount, bounds12km);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.RoadLineClassesToRasterValues);

            string sFullFilename = Path.Combine(DirectoryOriginal, TopographicDb.sPrefixForRoads + s12km12kmMapTileName + TopographicDb.sPostfixForLine + ".shp");
            rasteriser.AddShapefile(sFullFilename);

            for (int x = (int)bounds12km.MinX; x < (int)bounds12km.MaxX; x += Tile.EdgeLength)
            {
                for (int y = (int)bounds12km.MinY; y < (int)bounds12km.MaxY; y += Tile.EdgeLength)
                {
                    Tile t = new() { Name = TileNamer.Encode(x, y, Tile.EdgeLength), Version = tile.Version };

                    // Save to filesystem
                    rasteriser.WriteAsAscii(Path.Combine(DirectoryIntermediate, t.FilenameRoads), x, y, x + Tile.EdgeLength, y + Tile.EdgeLength);
                }
            }

            _12kmRoadsDone.TryUpdate(s12km12kmMapTileName, true, false);
            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildBuildingVertices(Tile tile)
        {

            // Check if the tile is already being processed and add it to the dictionary if not.
            if (true == _1kmBuildingsDone.TryGetValue(tile.Name, out bool bIsCompleted))
            {
                if (bIsCompleted)
                {
                    //// Shapefile is already processed, so just update the tile.
                    //_ = tile.Buildings;
                    //Interlocked.Increment(ref tile.CompletedCount);

                    Debug.Log($"Buildings for {tile.Name} are already completed.");
                }
                else
                {
                    Debug.Log($"Buildings for {tile.Name} are already under work.");
                }

                return;
            }

            _1kmBuildingsDone.TryAdd(tile.Name, false);


            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);

            string sFullFilename = Path.Combine(DirectoryOriginal, TopographicDb.sPrefixForBuildings + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");

            //Feature[] features = Shapefile.ReadAllFeatures(sFullFilename, new ShapefileReaderOptions() { MbrFilter = bounds} );
            Feature[] features = Shapefile.ReadAllFeatures(sFullFilename);

            tile.BuildingTriangles = new();
            tile.BuildingVertices = new();

            foreach (Feature f in features)
            {
                // Make sure f is inside bounds
                if (false == bounds.Contains(f.Geometry.EnvelopeInternal))
                {
                    continue;
                }

                int classification = (int)(long)f.Attributes["LUOKKA"];

                if (false == TopographicDb.BuildingPolygonClassesToRasterValues.ContainsKey(classification))
                {
                    continue;
                }

                MultiPolygon mp = (MultiPolygon)f.Geometry;

                List<Vector3> buildingVertices = new();
                List<int> buildingTriangles = new();

                // Go through polygons in the multipolygon
                for (int j = 0; j < mp.NumGeometries; j++)
                {
                    Polygon buildingPolygon = (Polygon)mp.GetGeometryN(j);
                    LineString buildingExterior = buildingPolygon.ExteriorRing;

                    // Try to find the building height from the corners of the polygon
                    float fBuildingHeights = 0f;
                    int iBuildingHeightCount = 0;
                    for (int i = 0; i < buildingExterior.NumPoints; i++)
                    {
                        Coordinate c = buildingExterior.GetCoordinateN(i);

                        // Get building height at the coordinate
                        tile.TerrainGrid.GetGridIndexes(c.X, c.Y, out int iX, out int iY);
                        BinPoint bp = tile.TerrainGrid.GetHighestPointInClassRange(iX, iY, 0, byte.MaxValue);

                        if (bp != null)
                        {
                            fBuildingHeights += bp.Z;
                            iBuildingHeightCount++;
                        }
                    }

                    // Create roof triangulation

                    // Move bounds to next integer coordinates
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

                    for (int x = (int)buildingBounds.MinX; x <= buildingBounds.MaxX ; x++)
                    {
                        for (int y = (int)buildingBounds.MinY; y <= buildingBounds.MaxY; y++)
                        {
                            tile.TerrainGrid.GetGridIndexes(x, y, out int iRow, out int jCol);
                            BinPoint bp = tile.TerrainGrid.GetHighestPointInClassRange(iRow, jCol, 0, byte.MaxValue);

                            if (bp != null && buildingPolygon.Contains(new Point(x, y)))
                            {
                                tri.AddPoint(new LasPoint() { x = x - buildingBounds.MinX, y = y - buildingBounds.MinY, z = bp.Z  });

                                fBuildingHeights += bp.Z;
                                iBuildingHeightCount++;
                            }
                        }
                    }

                    float fBuildingHeight = fBuildingHeights / iBuildingHeightCount;

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

                    // Add roof vertices
                    int iTriangleCount = tri.GetTriangleCount();
                    for (int i = 0; i < iTriangleCount; i++)
                    {
                        tri.GetTriangle(i, out Coordinate c0, out Coordinate c1, out Coordinate c2);

                        // Skip extra segments on concave corners
                        Point center = new((c0.X + c1.X + c2.X) / 3 + buildingBounds.MinX, (c0.Y + c1.Y + c2.Y) / 3 + buildingBounds.MinY);
                        if (false == buildingPolygon.Contains(center))
                        {
                            continue;
                        }

                        int iVertexStart = buildingVertices.Count;
                        buildingVertices.Add(new Vector3((float)(c0.X + buildingBounds.MinX - bounds.MinX),
                            (float)(fBuildingHeight * tile.DemMaxHeight), (float)(c0.Y + buildingBounds.MinY - bounds.MinY)));
                        buildingVertices.Add(new Vector3((float)(c1.X + buildingBounds.MinX - bounds.MinX),
                            (float)(fBuildingHeight * tile.DemMaxHeight), (float)(c1.Y + buildingBounds.MinY - bounds.MinY)));
                        buildingVertices.Add(new Vector3((float)(c2.X + buildingBounds.MinX - bounds.MinX),
                            (float)(fBuildingHeight * tile.DemMaxHeight), (float)(c2.Y + buildingBounds.MinY - bounds.MinY)));
                        //buildingVertices.Add(new Vector3((float)(c0.X + buildingBounds.MinX - bounds.MinX), 
                        //    (float)(c0.Z * tile.DemMaxHeight), (float)(c0.Y + buildingBounds.MinY - bounds.MinY)));
                        //buildingVertices.Add(new Vector3((float)(c1.X + buildingBounds.MinX - bounds.MinX), 
                        //    (float)(c1.Z * tile.DemMaxHeight), (float)(c1.Y + buildingBounds.MinY - bounds.MinY)));
                        //buildingVertices.Add(new Vector3((float)(c2.X + buildingBounds.MinX - bounds.MinX), 
                        //    (float)(c2.Z * tile.DemMaxHeight), (float)(c2.Y + buildingBounds.MinY - bounds.MinY)));

                        buildingTriangles.Add(iVertexStart);
                        buildingTriangles.Add(iVertexStart + 1);
                        buildingTriangles.Add(iVertexStart + 2);
                    }


                    // Add wall vertices
                    for (int i = 1; i < buildingExterior.NumPoints; i++)
                    {
                        Coordinate c0 = buildingExterior.GetCoordinateN(i - 1);
                        Coordinate c1 = buildingExterior.GetCoordinateN(i);

                        // Get ground height at the coordinate
                        float fHeight0 = (float)tile.TerrainGrid.GetHeight(c0.X, c0.Y);
                        float fHeight1 = (float)tile.TerrainGrid.GetHeight(c1.X, c1.Y);

                        // Create a quad between the two points
                        int iVertexStart = buildingVertices.Count;
                        buildingVertices.Add(new Vector3((float)(c0.X - bounds.MinX), fHeight0 * tile.DemMaxHeight, (float)(c0.Y - bounds.MinY)));
                        buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX), fHeight1 * tile.DemMaxHeight, (float)(c1.Y - bounds.MinY)));
                        buildingVertices.Add(new Vector3((float)(c1.X - bounds.MinX), fBuildingHeight * tile.DemMaxHeight, (float)(c1.Y - bounds.MinY)));
                        buildingVertices.Add(new Vector3((float)(c0.X - bounds.MinX), fBuildingHeight * tile.DemMaxHeight, (float)(c0.Y - bounds.MinY)));

                        buildingTriangles.Add(iVertexStart);
                        buildingTriangles.Add(iVertexStart + 1);
                        buildingTriangles.Add(iVertexStart + 2);
                        buildingTriangles.Add(iVertexStart);
                        buildingTriangles.Add(iVertexStart + 2);
                        buildingTriangles.Add(iVertexStart + 3);
                    }


                    tile.BuildingVertices.Add(buildingVertices.ToArray());
                    tile.BuildingTriangles.Add(buildingTriangles.ToArray());
                }
            }

            _1kmBuildingsDone.TryUpdate(s12km12kmMapTileName, true, false);
            Interlocked.Increment(ref tile.CompletedCount);
        }
    }
}
