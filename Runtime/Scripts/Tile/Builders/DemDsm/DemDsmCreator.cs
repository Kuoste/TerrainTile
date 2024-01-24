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
        const int _iOverlapInMeters = (_iTotalEdgeLengthInMeters - Tile.EdgeLength) / 2;

        /// <summary>
        /// Total triangulation edge length
        /// </summary>
        const int _iTotalEdgeLengthInMeters = 1084;

        const int _iTotalEdgeLengthInPixels = 1110;

        const float _dOverlapPercentageLowBound = (float)_iOverlapInMeters / Tile.EdgeLength;
        const float _dOverlapPercentageHighBound = 1 - _dOverlapPercentageLowBound;

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
            TileNamer.Decode(s3km3kmTileName, out Envelope bounds3km);

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

                extent.ExpandBy(_iOverlapInMeters);

                grids[i] = VoxelGrid.CreateGrid(_iTotalEdgeLengthInPixels, _iTotalEdgeLengthInPixels, extent);

                triangulations[i] = new SurfaceTriangulation(_iTotalEdgeLengthInMeters, _iTotalEdgeLengthInMeters,
                    extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);
            }


            //int iCount = 0;

            while ((p = reader.ReadPoint()) != null)
            {
                if (tile.Token.IsCancellationRequested)
                    return new();

                //iCount++;
                //if (iCount % 2 == 0)
                //{
                //    continue;
                //}

                if (p.classification != (byte)PointCloud05p.Classes.LowVegetation &&
                    p.classification != (byte)PointCloud05p.Classes.MedVegetation &&
                    p.classification != (byte)PointCloud05p.Classes.HighVegetation &&
                    p.classification != (byte)PointCloud05p.Classes.Ground)
                {
                    continue;
                }

                // Get submesh indices
                int x = (int)(p.x - bounds3km.MinX);
                int y = (int)(p.y - bounds3km.MinY);
                int ix = x / Tile.EdgeLength;
                int iy = y / Tile.EdgeLength;

                AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                // Look if point is part of another submesh overlap area.
                // Overlap is needed because otherwise adjacent triangulated surfaces have a gap in between.

                float dPercentageX = (float)x / Tile.EdgeLength - ix;
                float dPercentageY = (float)y / Tile.EdgeLength - iy;

                if (dPercentageX < _dOverlapPercentageLowBound || dPercentageX > _dOverlapPercentageHighBound ||
                    dPercentageY < _dOverlapPercentageLowBound || dPercentageY > _dOverlapPercentageHighBound)
                {
                    // This point belongs to an extended area of one or more other submesh.

                    int iWholeMeshEdgeLength = Tile.EdgeLength * iSubmeshesPerEdge;

                    if (x < _iOverlapInMeters || x > (iWholeMeshEdgeLength - _iOverlapInMeters) ||
                        y < _iOverlapInMeters || y > (iWholeMeshEdgeLength - _iOverlapInMeters))
                    {
                        // Part of another file. Todo: Save these points to four separate files
                        // so they can be read when adjacent laz files are processed.
                        continue;
                    }

                    if (dPercentageX < _dOverlapPercentageLowBound)
                    {
                        ix = (x - _iOverlapInMeters) / Tile.EdgeLength;
                        iy = y / Tile.EdgeLength;

                        AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                        if (dPercentageY < _dOverlapPercentageLowBound)
                        {
                            //ix = (x - _iOverlapInMeters) / m_iSubmeshEdgeLength;
                            iy = (y - _iOverlapInMeters) / Tile.EdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                            ix = x / Tile.EdgeLength;
                            //iy = (y - _iOverlapInMeters) / m_iSubmeshEdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);
                        }

                        if (dPercentageY > _dOverlapPercentageHighBound)
                        {
                            //ix = (x - _iOverlapInMeters) / m_iSubmeshEdgeLength;
                            iy = (y + _iOverlapInMeters) / Tile.EdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                            ix = x / Tile.EdgeLength;
                            //iy = (y + _iOverlapInMeters) / m_iSubmeshEdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);
                        }
                    }

                    if (dPercentageX > _dOverlapPercentageHighBound)
                    {

                        ix = (x + _iOverlapInMeters) / Tile.EdgeLength;
                        iy = y / Tile.EdgeLength;

                        AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                        if (dPercentageY < _dOverlapPercentageLowBound)
                        {
                            //ix = (x + _iOverlapInMeters) / m_iSubmeshEdgeLength;
                            iy = (y - _iOverlapInMeters) / Tile.EdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                            ix = x / Tile.EdgeLength;
                            //iy = (y - _iOverlapInMeters) / m_iSubmeshEdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);
                        }

                        if (dPercentageY > _dOverlapPercentageHighBound)
                        {
                            //ix = (x + _iOverlapInMeters) / m_iSubmeshEdgeLength;
                            iy = (y + _iOverlapInMeters) / Tile.EdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                            ix = x / Tile.EdgeLength;
                            //iy = (y + _iOverlapInMeters) / m_iSubmeshEdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);
                        }
                    }

                    if (dPercentageY < _dOverlapPercentageLowBound)
                    {
                        ix = x / Tile.EdgeLength;
                        iy = (y - _iOverlapInMeters) / Tile.EdgeLength;

                        AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                        if (dPercentageX < _dOverlapPercentageLowBound)
                        {
                            ix = (x - _iOverlapInMeters) / Tile.EdgeLength;
                            //iy = (y - _iOverlapInMeters) / m_iSubmeshEdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                            //ix = (x - _iOverlapInMeters) / m_iSubmeshEdgeLength;
                            iy = y / Tile.EdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);
                        }

                        if (dPercentageX > _dOverlapPercentageHighBound)
                        {
                            ix = (x + _iOverlapInMeters) / Tile.EdgeLength;
                            //iy = (y - _iOverlapInMeters) / m_iSubmeshEdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                            //ix = (x + _iOverlapInMeters) / m_iSubmeshEdgeLength;
                            iy = y / Tile.EdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);
                        }
                    }

                    if (dPercentageY > _dOverlapPercentageHighBound)
                    {
                        ix = x / Tile.EdgeLength;
                        iy = (y + _iOverlapInMeters) / Tile.EdgeLength;

                        AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                        if (dPercentageX < _dOverlapPercentageLowBound)
                        {
                            ix = (x - _iOverlapInMeters) / Tile.EdgeLength;
                            //iy = (y + _iOverlapInMeters) / m_iSubmeshEdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                            //ix = (x - _iOverlapInMeters) / m_iSubmeshEdgeLength;
                            iy = y / Tile.EdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);
                        }

                        if (dPercentageX > _dOverlapPercentageHighBound)
                        {
                            ix = (x + _iOverlapInMeters) / Tile.EdgeLength;
                            //iy = (y + _iOverlapInMeters) / m_iSubmeshEdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);

                            //ix = (x + _iOverlapInMeters) / m_iSubmeshEdgeLength;
                            iy = y / Tile.EdgeLength;

                            AddPoint(p, iSubmeshesPerEdge, triangulations, grids, ix, iy);
                        }
                    }
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

                // Cannot use full overlap because triangulation is not complete on edges
                env.ExpandBy(_iOverlapInMeters / 2);

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

                //grids[i].WriteDemAsAscii(Path.Combine(tile.DirectoryIntermediate, s1km1kmTilename + ".asc"));

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

        private static void AddPoint(LasPoint p, int iSubmeshesPerEdge, SurfaceTriangulation[] triangulations, VoxelGrid[] grids, int ix, int iy)
        {
            int iOverlapSubmeshIndex = ix * iSubmeshesPerEdge + iy;

            if (ix < 0 || ix >= iSubmeshesPerEdge || iy < 0 || iy >= iSubmeshesPerEdge)
            {
                Debug.LogFormat("Coordinates of a point (x={0}, y={1} are out of bounds", p.x, p.y);
            }

            bool bIsGround = p.classification == (byte)PointCloud05p.Classes.Ground;

            grids[iOverlapSubmeshIndex].AddPoint(p.x, p.y, (float)p.z, p.classification, bIsGround);

            if (bIsGround)
                triangulations[iOverlapSubmeshIndex].AddPoint(p);
        }
    }
}
