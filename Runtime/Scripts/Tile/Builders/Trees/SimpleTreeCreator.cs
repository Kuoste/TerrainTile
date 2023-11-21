using Kuoste.LidarWorld.Tile;
using LasUtility.Nls;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;

public class SimpleTreeCreator : ITreeBuilder
{
    const int _iSearchRadiusBuildingsRoads = 3;
    const int _iSearchRadiusHighVegetation = 2;
    const int _iRequiredHighVegetationCountAroundTree = 5;

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

                if (AreBuildingsRoadsNearby(tile, iRow, jCol, _iSearchRadiusBuildingsRoads))
                    continue;

                int iHighVegetationCount = 0;

                List<BinPoint> centerPoints = tile.DemDsm.GetPoints(iRow, jCol);

                if (centerPoints.Count == 0 || centerPoints[0].Class != (byte)PointCloud05p.Classes.HighVegetation)
                {
                    continue;
                }

                float fTreeHeight = float.MinValue;

                for (int ii = iRow - _iSearchRadiusHighVegetation; ii <= iRow + _iSearchRadiusHighVegetation; ii++)
                {
                    for (int jj = jCol - _iSearchRadiusHighVegetation; jj <= jCol + _iSearchRadiusHighVegetation; jj++)
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
                if (iHighVegetationCount < _iRequiredHighVegetationCountAroundTree || fTreeHeight > centerPoints[0].Z)
                {
                    continue;
                }

                fTreeHeight -= (float)tile.DemDsm.GetValue(iRow, jCol);

                // Ground height is not always available (e.g. triangulation on corners of the tile)
                if (float.IsNaN(fTreeHeight))
                {
                    continue;
                }

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

    private bool AreBuildingsRoadsNearby(Tile tile, int iRow, int jCol, int iRadius)
    {
        if (AreBuildingsRoadsOnCell(tile, iRow, jCol))
            return true;

        if (iRow - iRadius > 0)
        {
            if (AreBuildingsRoadsOnCell(tile, iRow - iRadius, jCol))
                return true;
        }

        if (iRow + iRadius < tile.DemDsm.Bounds.RowCount)
        {
            if (AreBuildingsRoadsOnCell(tile, iRow + iRadius, jCol))
                return true;
        }

        if (jCol - iRadius > 0)
        {
            if (AreBuildingsRoadsOnCell(tile, iRow, jCol - iRadius))
                return true;
        }

        if (jCol + iRadius < tile.DemDsm.Bounds.ColumnCount)
        {
            if (AreBuildingsRoadsOnCell(tile, iRow, jCol + iRadius))
                return true;
        }

        return false;
    }

    private bool AreBuildingsRoadsOnCell(Tile tile, int iRow, int jCol)
    {
        tile.DemDsm.GetGridCoordinates(iRow, jCol, out double x, out double y);

        if (tile.BuildingsRoads.GetValue(new Coordinate(x, y)) > 0)
            return true;

        return false;
    }
}
