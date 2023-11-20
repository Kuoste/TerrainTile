using Kuoste.LidarWorld.Tile;
using LasUtility.Common;
using LasUtility.Nls;
using LasUtility.ShapefileRasteriser;
using NetTopologySuite.Geometries;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Tile
{
    public class RoadRasterCreator : IRoadRasterBuilder
    {
        /// <summary>
        /// Keep track of the roads shapefiles so that we don't try to process the same file multiple times.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _12kmRoadsDone = new();

        public IRaster Build(Tile tile)
        {
            if (tile.Token.IsCancellationRequested)
                return new ByteRaster();

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
                    Debug.Log($"RoadRaster {s12km12kmMapTileName} for {tile.Name} was already completed.");
                    return ByteRaster.CreateFromAscii(Path.Combine(tile.DirectoryIntermediate, IRoadRasterBuilder.Filename(tile.Name, tile.Version)));
                }
                else
                {
                    Debug.Log($"RoadRaster {s12km12kmMapTileName} for {tile.Name} is under work.");
                    return null;
                }
            }

            _12kmRoadsDone.TryAdd(s12km12kmMapTileName, false);

            Stopwatch sw = Stopwatch.StartNew();

            RasteriserEvenOdd rasteriser = new();
            rasteriser.SetCancellationToken(tile.Token);

            int iRowAndColCount = TopographicDb.iMapTileEdgeLengthInMeters / Tile.EdgeLength * tile.AlphamapResolution;
            rasteriser.InitializeRaster(iRowAndColCount, iRowAndColCount, bounds12km);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.RoadLineClassesToRasterValues);
            rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.BuildingPolygonClassesToRasterValues);

            string sFullFilename = Path.Combine(tile.DirectoryOriginal, TopographicDb.sPrefixForRoads + s12km12kmMapTileName + TopographicDb.sPostfixForLine + ".shp");
            rasteriser.RasteriseShapefile(sFullFilename);
            sFullFilename = Path.Combine(tile.DirectoryOriginal, TopographicDb.sPrefixForBuildings + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");
            rasteriser.RasteriseShapefile(sFullFilename);

            for (int x = (int)bounds12km.MinX; x < (int)bounds12km.MaxX; x += Tile.EdgeLength)
            {
                for (int y = (int)bounds12km.MinY; y < (int)bounds12km.MaxY; y += Tile.EdgeLength)
                {
                    if (tile.Token.IsCancellationRequested)
                        return new ByteRaster();

                    string sTileName = TileNamer.Encode(x, y, Tile.EdgeLength);

                    // Save to filesystem
                    rasteriser.WriteAsAscii(Path.Combine(tile.DirectoryIntermediate, IRoadRasterBuilder.Filename(sTileName, tile.Version))
                        ,x, y, x + Tile.EdgeLength, y + Tile.EdgeLength);
                }
            }

            _12kmRoadsDone.TryUpdate(s12km12kmMapTileName, true, false);

            Debug.Log($"Rasterising terrain types for 12x12 km2 tile {s12km12kmMapTileName} took {sw.Elapsed.TotalSeconds} s.");

            return rasteriser.Crop((int)bounds.MinX, (int)bounds.MinY, (int)bounds.MinX + Tile.EdgeLength, (int)bounds.MinY + Tile.EdgeLength);
        }
    }
}
