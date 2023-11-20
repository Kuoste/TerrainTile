using Kuoste.LidarWorld.Tile;
using LasUtility.Nls;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;


public class SimpleTreeCreator : ITreeBuilder
{
    public List<Point> Build(Tile tile)
    {
        TileNamer.Decode(tile.Name, out Envelope bounds);

        List<Point> trees = new();

        if (tile.Token.IsCancellationRequested)
            return trees;

        string sFullFilename = Path.Combine(tile.DirectoryIntermediate, ITreeBuilder.Filename(tile.Name, tile.Version));
        using StreamWriter streamWriter = new(sFullFilename);

        for (int iRow = 0; iRow < tile.DemDsm.Bounds.RowCount; iRow++)
        {
            for (int jCol = 0; jCol < tile.DemDsm.Bounds.ColumnCount; jCol++)
            {
                if (tile.Token.IsCancellationRequested)
                {
                    streamWriter.Close();
                    File.Delete(sFullFilename);
                    return trees;
                }

                //bool bIsPointInBuilding = false;
                //Coordinate c = tile.DemDsm.Bounds.CellBottomLeftToProj(iRow, jCol);
                //foreach (Tile.Building b in tile.Buildings)
                //{
                //    if (b.Bounds.Contains(c))
                //    {
                //        bIsPointInBuilding = true;
                //        break;
                //    }   
                //}

                //if (bIsPointInBuilding)
                //{
                //    continue;
                //}

                const int iRadius = 2;
                int iHighVegetationCount = 0;

                List<BinPoint> centerPoints = tile.DemDsm.GetPoints(iRow, jCol);

                if (centerPoints.Count == 0 || centerPoints[0].Class != (byte)PointCloud05p.Classes.HighVegetation)
                {
                    continue;
                }

                float fTreeHeight = float.MinValue;

                for (int ii = iRow - iRadius; ii <= iRow + iRadius; ii++)
                {
                    for (int jj = jCol - iRadius; jj <= jCol + iRadius; jj++)
                    {
                        if (ii < 0 || ii > tile.DemDsm.Bounds.RowCount - 1 ||
                            jj < 0 || jj > tile.DemDsm.Bounds.ColumnCount - 1)
                        {
                            continue;
                        }

                        List<BinPoint> neighborhoodPoints = tile.DemDsm.GetPoints(ii, jj);

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

                fTreeHeight -= (float)tile.DemDsm.GetValue(iRow, jCol);

                // Ground height is not always available (e.g. triangulation on corners of the tile)
                if (float.IsNaN(fTreeHeight))
                {
                    continue;
                }

                //fMaxTreeHeight = Math.Max(fMaxTreeHeight, fMaxHeight);

                // Write Point
                streamWriter.Write("{\"type\":\"Point\",\"coordinates\":");
                tile.DemDsm.GetGridCoordinates(iRow, jCol, out double x, out double y);
                // Write as int since the accuracy is in ~meters
                streamWriter.Write($"[{(int)x},{(int)y},{(int)fTreeHeight}]");
                streamWriter.WriteLine("}");

                trees.Add(new(((int)x - bounds.MinX) / Tile.EdgeLength, ((int)y - bounds.MinY) / Tile.EdgeLength, (int)fTreeHeight));
            }
        }

        //sw.Stop();
        //Debug.Log($"Tile {_tile.Name}: {_tile.Trees.Count} trees determined in {sw.ElapsedMilliseconds} ms.");
        //sw.Restart();

        return trees;
    }
}
