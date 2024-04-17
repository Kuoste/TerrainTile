using Kuoste.LidarWorld.Tile;
using LasUtility.Nls;
using NetTopologySuite.Geometries;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Kuoste.LidarWorld.Tile
{
    public class TreeReader : Builder, ITreeBuilder
    {
        public List<Point> Build(Tile tile)
        {
            List<Point> trees = new();

            if (CancellationToken.IsCancellationRequested)
                return trees;

            TileNamer.Decode(tile.Name, out Envelope bounds);
            string sFullFilename = Path.Combine(tile.DirectoryIntermediate, ITreeBuilder.Filename(tile.Name, tile.Version));

            string[] sTrees = File.ReadAllText(sFullFilename).Split("Point");

            foreach (string sTree in sTrees)
            {
                if (CancellationToken.IsCancellationRequested)
                    return trees;

                string[] sCordinates = sTree.Split("[", StringSplitOptions.RemoveEmptyEntries);

                foreach (string sCoordinate in sCordinates)
                {
                    if (!char.IsDigit(sCoordinate[0]))
                        continue;

                    var coords = sCoordinate.Split(",", StringSplitOptions.RemoveEmptyEntries);

                    // Delete the last character which is a closing bracket and everyting after it
                    coords[2] = coords[2][..coords[2].IndexOf(']')];

                    trees.Add(new(
                        (double.Parse(coords[0]) - bounds.MinX) / Tile.EdgeLength,
                        (double.Parse(coords[1]) - bounds.MinY) / Tile.EdgeLength,
                        double.Parse(coords[2])));
                }
            }

            return trees;

        }
    }

}
