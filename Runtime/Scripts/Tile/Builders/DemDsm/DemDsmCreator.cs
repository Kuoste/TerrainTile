using LasUtility.DEM;
using LasUtility.LAS;
using LasUtility.Nls;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Tile
{
    public class DemDsmCreator : IDemDsmBuilder
    {
        /// <summary>
        /// Use some overlap in triangulations or else the triangulations won't be complete on edges
        /// </summary>
        const int _iOverlap = 50;

        /// <summary>
        /// Total triangulation edge length
        /// </summary>
        const int _iTotalEdgeLength = Tile.EdgeLength + 2 * _iOverlap;

        /// <summary>
        /// Keep track of the las files so that we don't try to process the same tile multiple times.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _3kmDemDsmDone = new();

        public VoxelGrid Build(Tile tile)
        {
            if (tile.Token.IsCancellationRequested)
                return new();

            TileNamer.Decode(tile.Name, out Envelope bounds1km);
            string s3km3kmTileName = TileNamer.Encode((int)bounds1km.MinX, (int)bounds1km.MinY, 3000);

            // Check if the tile is already being processed
            if (true == _3kmDemDsmDone.TryGetValue(s3km3kmTileName, out bool bIsCompleted))
            {
                if (bIsCompleted)
                {
                    // Las file is already processed, so just update the tile.
                    Debug.Log($"DemAndDsmPointCloud for {tile.Name} is already completed.");
                    return VoxelGrid.Deserialize(Path.Combine(tile.DirectoryIntermediate, IDemDsmBuilder.Filename(tile.Name, tile.Version)));
                }
                else
                {
                    Debug.Log($"DemAndDsmPointCloud for {tile.Name} is under work.");
                    return new();
                }

            }

            _3kmDemDsmDone.TryAdd(s3km3kmTileName, false);

            ILasFileReader reader = new LasZipFileReader();

            string sFilename = Path.Combine(tile.DirectoryOriginal, s3km3kmTileName + ".laz");

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

                grids[i] = VoxelGrid.CreateGrid(tile.HeightMapResolution, tile.HeightMapResolution, extent);

                triangulations[i] = new SurfaceTriangulation(_iTotalEdgeLength, _iTotalEdgeLength,
                    extent.MinX - _iOverlap, extent.MinY - _iOverlap,
                    extent.MaxX + _iOverlap, extent.MaxY + _iOverlap);
            }

            while ((p = reader.ReadPoint()) != null)
            {
                if (tile.Token.IsCancellationRequested)
                    return new();

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

                    triangulations[iSubmeshIndex].AddPoint(p);

                    // Also add the ground point to the grid, so we don't have to query heights to cells where we already have a height.
                    grids[iSubmeshIndex].AddPoint(p.x, p.y, (float)p.z, p.classification, true);


                    // Look if point is part of another submesh overlap area.
                    // Overlap is needed because otherwise adjacent triangulated surfaces have a gap in between.

                    int iWholeMeshEdgeLength = Tile.EdgeLength * iSubmeshesPerEdge;
                    int iOverlapInMeters = _iOverlap;
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

            sw.Stop();
            Debug.Log($"Tile {s3km3kmTileName} was read to grid and triangulations in {sw.Elapsed.TotalSeconds} seconds.");

            for (int i = 0; i < iSubmeshCount; i++)
            {
                if (tile.Token.IsCancellationRequested)
                    return new();

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
                Debug.Log($"Triangulation {i} took {sw2.Elapsed.TotalSeconds} s. Empty cells before {iMissBefore}, after {iMissAfter}.");
            }

            VoxelGrid output = new();

            for (int i = 0; i < iSubmeshCount; i++)
            {
                if (tile.Token.IsCancellationRequested)
                    return new();

                string s1km1kmTilename = s3km3kmTileName + "_" + (i + 1).ToString();

                // Save grid to filesystem for future use
                grids[i].Serialize(Path.Combine(tile.DirectoryIntermediate, IDemDsmBuilder.Filename(s1km1kmTilename, tile.Version)));

                if (tile.Name == s1km1kmTilename)
                {
                    output = grids[i];
                }
            }

            //sw.Stop();
            //Debug.Log($"Las processing finished! Total time for tile {s3km3kmTileName} was {sw.Elapsed.TotalSeconds} seconds.");

            _3kmDemDsmDone.TryUpdate(s3km3kmTileName, true, false);
            return output;

        }
    }
}
