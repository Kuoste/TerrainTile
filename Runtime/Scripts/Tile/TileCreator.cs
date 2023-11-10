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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
        /// For saving the status of the 1x1 km2 tiles so that they are available for the Geometry service
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _1kmDemDsmDone = new();

        /// <summary>
        /// Keep track of the las files so that we don't try to process the same tile multiple times.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _3kmDemDsmDone = new();

        /// <summary>
        /// Keep track of the roads shapefiles so that we don't try to process the same file multiple times.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _12kmRoadsDone = new();

        /// <summary>
        /// Keep track of the terrain type shapefiles so that we don't try to process the same file multiple times.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _12kmTerrainTypesDone = new();

        ///// <summary>
        ///// Keep track of the received tiles so that we can update the tile features when the tile is ready.
        ///// </summary>
        //private ConcurrentDictionary<string, Tile> _tilesReceived = new();

        /// <summary>
        /// For detecting when we should stop building tiles.
        /// </summary>
        private CancellationToken _token;

        public void SetCancellationToken(CancellationToken token)
        {
            _token = token;
        }

        public void BuildDemAndDsmPointCloud(Tile tile)
        {
            if (_token.IsCancellationRequested)
                return;

            TileNamer.Decode(tile.Name, out Envelope bounds1km);
            string s3km3kmTileName = TileNamer.Encode((int)bounds1km.MinX, (int)bounds1km.MinY, m_iSupportedInputTileWidth);

            // Check if the tile is already being processed
            if (true == _3kmDemDsmDone.TryGetValue(s3km3kmTileName, out bool bIsCompleted))
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

            _3kmDemDsmDone.TryAdd(s3km3kmTileName, false);

            ILasFileReader reader = new LasZipFileReader();

            string sFilename = Path.Combine(DirectoryOriginal, s3km3kmTileName + ".laz");

            reader.ReadHeader(sFilename);

            //Stopwatch sw = Stopwatch.StartNew();

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
                if (_token.IsCancellationRequested)
                    return;

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
                if (_token.IsCancellationRequested)
                    return;

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
                if (_token.IsCancellationRequested)
                    return;

                string s1km1kmTilename = s3km3kmTileName + "_" + (i + 1).ToString();

                Tile t = new() { Name = s1km1kmTilename, Version = tile.Version };

                // Save grid to filesystem for future use
                grids[i].Serialize(Path.Combine(DirectoryIntermediate, t.FilenameGrid));

                if (tile.Name == s1km1kmTilename)
                {
                    // Mark the tile that was asked as completed
                    tile.TerrainGrid = grids[i];
                    _1kmDemDsmDone.TryAdd(tile.Name, true);
                    Interlocked.Increment(ref tile.CompletedCount);
                }
            }

            _3kmDemDsmDone.TryUpdate(s3km3kmTileName, true, false);

            //sw.Stop();
            //Debug.Log($"Las processing finished! Total time for tile {s3km3kmTileName} was {sw.Elapsed.TotalSeconds} seconds.");
        }

        public void BuildTerrainTypeRaster(Tile tile)
        {
            if (_token.IsCancellationRequested)
                return;

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
            rasteriser.SetCancellationToken(_token);

            int iRowAndColCount = TopographicDb.iMapTileEdgeLengthInMeters / Tile.EdgeLength * m_iUnityAlphamapResolution;
            rasteriser.InitializeRaster(iRowAndColCount, iRowAndColCount, bounds12km);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.WaterPolygonClassesToRasterValues);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.WaterLineClassesToRasterValues);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.SwampPolygonClassesToRasterValues);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.RockPolygonClassesToRasterValues);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.SandPolygonClassesToRasterValues);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.FieldPolygonClassesToRasterValues);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.RockLineClassesToRasterValues);

            string sFullFilename = Path.Combine(DirectoryOriginal, TopographicDb.sPrefixForTerrainType + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");
            rasteriser.AddShapefile(sFullFilename);

            for (int x = (int)bounds12km.MinX; x < (int)bounds12km.MaxX; x += Tile.EdgeLength)
            {
                for (int y = (int)bounds12km.MinY; y < (int)bounds12km.MaxY; y += Tile.EdgeLength)
                {
                    if (_token.IsCancellationRequested)
                        return;

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
            if (_token.IsCancellationRequested)
                return;

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
            rasteriser.SetCancellationToken(_token);

            int iRowAndColCount = TopographicDb.iMapTileEdgeLengthInMeters / Tile.EdgeLength * m_iUnityAlphamapResolution;
            rasteriser.InitializeRaster(iRowAndColCount, iRowAndColCount, bounds12km);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.RoadLineClassesToRasterValues);

            string sFullFilename = Path.Combine(DirectoryOriginal, TopographicDb.sPrefixForRoads + s12km12kmMapTileName + TopographicDb.sPostfixForLine + ".shp");
            rasteriser.AddShapefile(sFullFilename);

            for (int x = (int)bounds12km.MinX; x < (int)bounds12km.MaxX; x += Tile.EdgeLength)
            {
                for (int y = (int)bounds12km.MinY; y < (int)bounds12km.MaxY; y += Tile.EdgeLength)
                {
                    if (_token.IsCancellationRequested)
                        return;

                    Tile t = new() { Name = TileNamer.Encode(x, y, Tile.EdgeLength), Version = tile.Version };

                    // Save to filesystem
                    rasteriser.WriteAsAscii(Path.Combine(DirectoryIntermediate, t.FilenameRoads), x, y, x + Tile.EdgeLength, y + Tile.EdgeLength);
                }
            }

            _12kmRoadsDone.TryUpdate(s12km12kmMapTileName, true, false);
            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildBuildings(Tile tile)
        {
            if (_token.IsCancellationRequested)
                return;

            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);

            using StreamWriter streamWriter = new(Path.Combine(DirectoryIntermediate, tile.FilenameBuildings));

            string sFullFilename = Path.Combine(DirectoryOriginal, TopographicDb.sPrefixForBuildings + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");
            Feature[] features = Shapefile.ReadAllFeatures(sFullFilename);

            tile.BuildingTriangles = new();
            tile.BuildingVertices = new();
            tile.BuildingSubmeshSeparator = new();

            foreach (Feature f in features)
            {
                if (_token.IsCancellationRequested)
                {
                    streamWriter.Close();
                    File.Delete(Path.Combine(DirectoryIntermediate, tile.FilenameBuildings));
                    return;
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
                        tile.TerrainGrid.GetGridIndexes(c.X, c.Y, out int iX, out int iY);
                        BinPoint bp = tile.TerrainGrid.GetHighestPointInClassRange(iX, iY, 0, byte.MaxValue);

                        if (bp != null)
                        {
                            buildingHeights.Add(bp.Z);
                        }

                        double dGroundHeight = tile.TerrainGrid.GetHeight(c.X, c.Y);
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
                            tile.TerrainGrid.GetGridIndexes(x, y, out int iRow, out int jCol);
                            BinPoint bp = tile.TerrainGrid.GetHighestPointInClassRange(iRow, jCol, 0, byte.MaxValue);

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

                        double dGroundHeight = tile.TerrainGrid.GetHeight(c.X, c.Y);
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

        public void BuildTrees(Tile tile)
        {
            if (_token.IsCancellationRequested)
                return;

            using StreamWriter streamWriter = new(Path.Combine(DirectoryIntermediate, tile.FilenameTrees));

            for (int iRow = 0; iRow < tile.TerrainGrid.Bounds.RowCount; iRow++)
            {
                for (int jCol = 0; jCol < tile.TerrainGrid.Bounds.ColumnCount; jCol++)
                {
                    if (_token.IsCancellationRequested)
                    {
                        streamWriter.Close();
                        File.Delete(Path.Combine(DirectoryIntermediate, tile.FilenameTrees));
                        return;
                    }

                    const int iRadius = 2;
                    int iHighVegetationCount = 0;

                    List<BinPoint> centerPoints = tile.TerrainGrid.GetPoints(iRow, jCol);

                    if (centerPoints.Count == 0 || centerPoints[0].Class != (byte)PointCloud05p.Classes.HighVegetation)
                    {
                        continue;
                    }

                    float fTreeHeight = float.MinValue;

                    for (int ii = iRow - iRadius; ii <= iRow + iRadius; ii++)
                    {
                        for (int jj = jCol - iRadius; jj <= jCol + iRadius; jj++)
                        {
                            if (ii < 0 || ii > tile.TerrainGrid.Bounds.RowCount - 1 ||
                                jj < 0 || jj > tile.TerrainGrid.Bounds.ColumnCount - 1)
                            {
                                continue;
                            }

                            List<BinPoint> neighborhoodPoints = tile.TerrainGrid.GetPoints(ii, jj);

                            foreach (BinPoint p in neighborhoodPoints)
                            {
                                if (p.Class == (byte)PointCloud05p.Classes.HighVegetation)
                                {
                                    fTreeHeight = Math.Max(fTreeHeight, p.Z);

                                    iHighVegetationCount++;
                                }
                                else
                                {
                                    // Points are sorted by descending height so after high vegetation
                                    // there are no more high vegetation points
                                    break;
                                }
                            }
                        }
                    }

                    // There has to be enough high vegetation points in the neighborhood
                    // and the tree has to be the highest point.
                    if (iHighVegetationCount < 5 || fTreeHeight > centerPoints[0].Z)
                    {
                        continue;
                    }

                    fTreeHeight -= tile.TerrainGrid.GetGroundHeight(iRow, jCol);

                    // Ground height is not always available (e.g. triangulation on corners of the tile)
                    if (float.IsNaN(fTreeHeight))
                    {
                        continue;
                    }

                    //fMaxTreeHeight = Math.Max(fMaxTreeHeight, fMaxHeight);

                    // Write Point
                    streamWriter.Write("{\"type\":\"Point\",\"coordinates\":");
                    tile.TerrainGrid.GetGridCoordinates(iRow, jCol, out double x, out double y);
                    // Write as int since the accuracy is in ~meters
                    streamWriter.Write($"[{(int)x},{(int)y},{(int)fTreeHeight}]");
                    streamWriter.WriteLine("}");
                }

            }

            //sw.Stop();
            //Debug.Log($"Tile {_tile.Name}: {_tile.Trees.Count} trees determined in {sw.ElapsedMilliseconds} ms.");
            //sw.Restart();
        }

        public void BuildWaterAreas(Tile tile)
        {
            if (_token.IsCancellationRequested)
                return;

            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope envBounds);
            GeometryFactory factory = new();
            Geometry bounds = factory.ToGeometry(envBounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)envBounds.MinX, (int)envBounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);

            string sFullFilename = Path.Combine(DirectoryOriginal, TopographicDb.sPrefixForTerrainType + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");

            Feature[] features = Shapefile.ReadAllFeatures(sFullFilename);

            using StreamWriter streamWriter = new(Path.Combine(DirectoryIntermediate, tile.FilenameWaterAreas));

            foreach (Feature f in features)
            {
                if (_token.IsCancellationRequested)
                {
                    streamWriter.Close();
                    File.Delete(Path.Combine(DirectoryIntermediate, tile.FilenameWaterAreas));
                    return;
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
                            float fHeight = (float)Math.Round(tile.TerrainGrid.GetHeight(pointForLakeSurface.X, pointForLakeSurface.Y), 2);

                            streamWriter.Write("{ \"type\":\"Polygon\", \"coordinates\": ");
                            streamWriter.Write("[[");

                            for (int i = 0; i < p.ExteriorRing.Coordinates.Length; i++)
                            {
                                Coordinate c = p.ExteriorRing.Coordinates[i];
                                streamWriter.Write($"[{Math.Round(c.X, 2)},{Math.Round(c.Y, 2)},{fHeight}]");

                                if (i < p.ExteriorRing.Coordinates.Length - 1)
                                {
                                    streamWriter.Write(",");
                                }
                            }

                            // End polygon
                            streamWriter.Write("]]");
                            streamWriter.WriteLine("}");
                        }
                    }
                }
            }
        }
    }
}
