using LasUtility.DEM;
using LasUtility.LAS;
using LasUtility.NlsTileName;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.VisualScripting;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Terrain
{
    public class TileCreator : ITileProvider
    {
        /// <summary>
        /// Currently only the 3x3 km2 las tiles are supported.
        /// </summary>
        const int m_iSupportedInputTileWidth = 3000;

        /// <summary>
        /// 3x3 km2 las tiles are divided into 9 parts for triangulation. This makes the length of the edge 1 km.
        /// </summary>
        const int m_iEdgeLength = 1000;

        /// <summary>
        /// Unity supports heightmap resolutions as (2^n + 1) so 1025 is the closest to 1 km
        /// </summary>
        const int m_iUnityHeightmapResolution = 1025;

        /// <summary>
        /// Use some overlap in triangulations or else the triangulations won't be complete on edges
        /// </summary>
        const int m_iOverlap = 50;

        /// <summary>
        /// Total triangulation edge length
        /// </summary>
        const int m_iTotalEdgeLength = m_iEdgeLength + 2 * m_iOverlap;

        /// <summary>
        /// Unity handles heightmap values as coefficients between 0.0 and 1.0. Use this divider to make sure heights are in that range.
        /// </summary>
        const int m_iHeightDivider = 1000;

        public Dictionary<string, VoxelGrid> GetTerrain(string sDirectory, string s3km3kmTileName, string sVersion)
        {
            NlsTileNamer.Decode(s3km3kmTileName, out Envelope area);

            if (area == null || area.Width != m_iSupportedInputTileWidth)
            {
                throw new Exception("Only 3 km x 3 km laz tiles are supported.");
            }

            ILasFileReader reader = new LasZipFileReader();

            string sFilename = Path.Combine(sDirectory, s3km3kmTileName + ".laz");

            reader.ReadHeader(sFilename);

            Stopwatch swTotal = Stopwatch.StartNew();

            reader.OpenReader(sFilename);

            double dMaxGroundHeight = double.MinValue;
            LasPoint p;

            int iSubmeshesPerEdge = (int)Math.Round((reader.MaxX - reader.MinX) / m_iEdgeLength);
            int iSubmeshCount = (int)Math.Pow(iSubmeshesPerEdge, 2);

            SurfaceTriangulation[] triangulations = new SurfaceTriangulation[iSubmeshCount];
            VoxelGrid[] grids = new VoxelGrid[iSubmeshCount];

            for (int i = 0; i < iSubmeshCount; i++)
            {
                // Create the NLS (Maanmittauslaitos) style name of a 1x1 km2 tile in order to get the coordinates.
                string sSubmeshName = s3km3kmTileName + "_" + (i + 1).ToString();
                NlsTileNamer.Decode(sSubmeshName, out Envelope extent);

                grids[i] = VoxelGrid.CreateGrid(sSubmeshName, m_iUnityHeightmapResolution, m_iUnityHeightmapResolution, extent);
                grids[i].Version = sVersion;

                triangulations[i] = new SurfaceTriangulation(m_iTotalEdgeLength, m_iTotalEdgeLength,
                    extent.MinX - m_iOverlap, extent.MinY - m_iOverlap,
                    extent.MaxX + m_iOverlap, extent.MaxY + m_iOverlap);
            }

            while ((p = reader.ReadPoint()) != null)
            {
                p.z /= m_iHeightDivider;

                double x = p.x;
                double y = p.y;
                double z = p.z;

                // Get submesh indices
                x -= reader.MinX;
                y -= reader.MinY;
                int ix = (int)x / m_iEdgeLength;
                int iy = (int)y / m_iEdgeLength;
                int iSubmeshIndex = ix * iSubmeshesPerEdge + iy;

                // Classifications from
                // https://www.maanmittauslaitos.fi/kartat-ja-paikkatieto/asiantuntevalle-kayttajalle/tuotekuvaukset/laserkeilausaineisto-05-p
                if (p.classification == (byte)NlsClasses.PointCloud05p.Ground)
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

                    int iWholeMeshEdgeLength = m_iEdgeLength * iSubmeshesPerEdge;
                    int iOverlapInMeters = m_iOverlap;
                    float dOverlapPercentageLowBound = (float)iOverlapInMeters / m_iEdgeLength;
                    float dOverlapPercentageHighBound = 1 - dOverlapPercentageLowBound;

                    float dPercentageX = (float)x / m_iEdgeLength - ix;
                    float dPercentageY = (float)y / m_iEdgeLength - iy;

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
                            ix = (int)(x - iOverlapInMeters) / m_iEdgeLength;
                            iy = (int)y / m_iEdgeLength;

                            if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                            {
                                int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                triangulations[iOverlapSubmeshIndex].AddPoint(p);
                            }

                            if (dPercentageY < dOverlapPercentageLowBound)
                            {
                                //ix = (int)(x - iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)(y - iOverlapInMeters) / m_iEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                ix = (int)x / m_iEdgeLength;
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
                                iy = (int)(y + iOverlapInMeters) / m_iEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                ix = (int)x / m_iEdgeLength;
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

                            ix = (int)(x + iOverlapInMeters) / m_iEdgeLength;
                            iy = (int)y / m_iEdgeLength;

                            if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                            {
                                int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                triangulations[iOverlapSubmeshIndex].AddPoint(p);
                            }

                            if (dPercentageY < dOverlapPercentageLowBound)
                            {
                                //ix = (int)(x + iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)(y - iOverlapInMeters) / m_iEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                ix = (int)x / m_iEdgeLength;
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
                                iy = (int)(y + iOverlapInMeters) / m_iEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                ix = (int)x / m_iEdgeLength;
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
                            ix = (int)x / m_iEdgeLength;
                            iy = (int)(y - iOverlapInMeters) / m_iEdgeLength;

                            if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                            {
                                int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                triangulations[iOverlapSubmeshIndex].AddPoint(p);
                            }

                            if (dPercentageX < dOverlapPercentageLowBound)
                            {
                                ix = (int)(x - iOverlapInMeters) / m_iEdgeLength;
                                //iy = (int)(y - iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                //ix = (int)(x - iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)y / m_iEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }

                            if (dPercentageX > dOverlapPercentageHighBound)
                            {
                                ix = (int)(x + iOverlapInMeters) / m_iEdgeLength;
                                //iy = (int)(y - iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                //ix = (int)(x + iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)y / m_iEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }
                        }

                        if (dPercentageY > dOverlapPercentageHighBound)
                        {
                            ix = (int)x / m_iEdgeLength;
                            iy = (int)(y + iOverlapInMeters) / m_iEdgeLength;

                            if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                            {
                                int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                triangulations[iOverlapSubmeshIndex].AddPoint(p);
                            }

                            if (dPercentageX < dOverlapPercentageLowBound)
                            {
                                ix = (int)(x - iOverlapInMeters) / m_iEdgeLength;
                                //iy = (int)(y + iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                //ix = (int)(x - iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)y / m_iEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }

                            if (dPercentageX > dOverlapPercentageHighBound)
                            {
                                ix = (int)(x + iOverlapInMeters) / m_iEdgeLength;
                                //iy = (int)(y + iOverlapInMeters) / m_iSubmeshEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }

                                //ix = (int)(x + iOverlapInMeters) / m_iSubmeshEdgeLength;
                                iy = (int)y / m_iEdgeLength;

                                if (ix >= 0 && ix < iSubmeshesPerEdge && iy >= 0 && iy < iSubmeshesPerEdge)
                                {
                                    int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;
                                    triangulations[iOverlapSubmeshIndex].AddPoint(p);
                                }
                            }
                        }
                    }
                }
                else if (p.classification == (byte)NlsClasses.PointCloud05p.LowVegetation ||
                    p.classification == (byte)NlsClasses.PointCloud05p.MedVegetation ||
                    p.classification == (byte)NlsClasses.PointCloud05p.HighVegetation)
                {
                    grids[iSubmeshIndex].AddPoint(p.x, p.y, (float)p.z, p.classification, false);
                }
            }

            reader.CloseReader();

            Debug.Log("Reading LAZ file took " + swTotal.Elapsed + " ms");

            for (int i = 0; i < iSubmeshCount; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();

                SurfaceTriangulation tri = triangulations[i];
                VoxelGrid grid = grids[i];

                grid.SortAndTrim();

                tri.Create();

                // Use the name of a 1x1 km2 tile to get the coordinates
                string sSubmeshName = s3km3kmTileName + "_" + (i + 1).ToString();
                NlsTileNamer.Decode(sSubmeshName, out Envelope env);

                double dEspsilon = 0.00001;
                grid.SetMissingHeightsFromTriangulation(tri,
                    env.MinX, env.MinY,
                    env.MaxX - dEspsilon, env.MaxY - dEspsilon,
                    out int iMissBefore, out int iMissAfter);

                // Free triangulation asap so we dont run out of memory.
                tri.Clear();

                Debug.Log($"Triangulation {i} took {sw.Elapsed} ms. Empty cells before {iMissBefore}, after {iMissAfter}.");
            }

            Dictionary<string, VoxelGrid> gridsByName = new();

            foreach (VoxelGrid grid in grids)
            {
                gridsByName.Add(grid.Name, grid);
            }

            Debug.Log($"Finished! Total preprocessing time for tile {s3km3kmTileName} was {swTotal.Elapsed.TotalSeconds} seconds.");

            return gridsByName;
        }
    }
}
