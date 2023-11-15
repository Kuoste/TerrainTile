using Kuoste.LidarWorld.Tile;
using LasUtility.Common;
using LasUtility.Nls;
using LasUtility.ShapefileRasteriser;
using NetTopologySuite.Geometries;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

public class TerrainTypeCreator : ITerrainTypeBuilder
{
    /// <summary>
    /// Keep track of the terrain type shapefiles so that we don't try to process the same file multiple times.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _12kmTerrainTypesDone = new();

    public IRaster Build(Tile tile)
    {
        if (tile.Token.IsCancellationRequested)
            return new HeightMap();

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
                Debug.Log($"TerrainTypeRaster {s12km12kmMapTileName} for {tile.Name} was already completed.");
                return HeightMap.CreateFromAscii(Path.Combine(tile.DirectoryIntermediate, ITerrainTypeBuilder.Filename(tile.Name, tile.Version)));
            }
            else
            {
                Debug.Log($"TerrainTypeRaster {s12km12kmMapTileName} for {tile.Name} is under work.");
                return null;
            }
        }

        _12kmTerrainTypesDone.TryAdd(s12km12kmMapTileName, false);

        Rasteriser rasteriser = new();
        rasteriser.SetCancellationToken(tile.Token);

        int iRowAndColCount = TopographicDb.iMapTileEdgeLengthInMeters / Tile.EdgeLength * tile.AlphamapResolution;
        rasteriser.InitializeRaster(iRowAndColCount, iRowAndColCount, bounds12km);
        rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.WaterPolygonClassesToRasterValues);
        rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.WaterLineClassesToRasterValues);
        rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.SwampPolygonClassesToRasterValues);
        rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.RockPolygonClassesToRasterValues);
        rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.SandPolygonClassesToRasterValues);
        rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.FieldPolygonClassesToRasterValues);
        rasteriser.AddRasterizedClassesWithRasterValues(TopographicDb.RockLineClassesToRasterValues);

        string sFullFilename = Path.Combine(tile.DirectoryOriginal, TopographicDb.sPrefixForTerrainType + s12km12kmMapTileName + TopographicDb.sPostfixForPolygon + ".shp");
        rasteriser.AddShapefile(sFullFilename);

        for (int x = (int)bounds12km.MinX; x < (int)bounds12km.MaxX; x += Tile.EdgeLength)
        {
            for (int y = (int)bounds12km.MinY; y < (int)bounds12km.MaxY; y += Tile.EdgeLength)
            {
                if (tile.Token.IsCancellationRequested)
                    return new HeightMap();

                string sTileName = TileNamer.Encode(x, y, Tile.EdgeLength);

                // Save to filesystem
                rasteriser.WriteAsAscii(Path.Combine(tile.DirectoryIntermediate, ITerrainTypeBuilder.Filename(sTileName, tile.Version)),
                    x, y, x + Tile.EdgeLength, y + Tile.EdgeLength);
            }
        }

        _12kmTerrainTypesDone.TryUpdate(s12km12kmMapTileName, true, false);

        return rasteriser.Crop((int)bounds.MinX, (int)bounds.MinY, 
            (int)bounds.MinX + Tile.EdgeLength, (int)bounds.MinY + Tile.EdgeLength);
    }
}
