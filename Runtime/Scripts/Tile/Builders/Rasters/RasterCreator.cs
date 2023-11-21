using Kuoste.LidarWorld.Tile;
using LasUtility.Common;
using LasUtility.Nls;
using LasUtility.ShapefileRasteriser;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri.Shapefiles.Readers;
using NetTopologySuite.IO.Esri;
using System.IO;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Collections.Generic;

namespace Kuoste.LidarWorld.Tile
{
    public class RasterCreator : IRasterBuilder
    {
        private string _sRasterFilenameSpecifier;
        private string[] _sShpFilenames;

        private Dictionary<int, byte> _nlsClassesToRasterValues = new();

        public void SetRasterizedClassesWithRasterValues(Dictionary<int, byte> classesToRasterValues)
        {
            _nlsClassesToRasterValues = classesToRasterValues;
        }

        public IRaster Build(Tile tile)
        {
            if (tile.Token.IsCancellationRequested)
                return new ByteRaster();

            // Get topographic db tile name
            TileNamer.Decode(tile.Name, out Envelope bounds);
            string s12km12kmMapTileName = TileNamer.Encode((int)bounds.MinX, (int)bounds.MinY, TopographicDb.iMapTileEdgeLengthInMeters);
            TileNamer.Decode(s12km12kmMapTileName, out Envelope bounds12km);

            string sFullFilename = Path.Combine(tile.DirectoryIntermediate, IRasterBuilder.Filename(tile.Name, _sRasterFilenameSpecifier, tile.Version));

            // Check if the tile is already being processed and add it to the dictionary if not.
            if (true == File.Exists(sFullFilename))
            {
                // Shapefile is already processed, so just update the tile.
                Debug.Log($"TerrainTypeRaster {s12km12kmMapTileName} for {tile.Name} was already completed.");
                return ByteRaster.CreateFromAscii(sFullFilename);
            }

            Stopwatch sw = Stopwatch.StartNew();

            RasteriserEvenOdd rasteriser = new();
            rasteriser.SetCancellationToken(tile.Token);

            Envelope rasterBounds = new(bounds12km);
            foreach (string sFilename in _sShpFilenames)
            {
                using ShapefileReader reader = Shapefile.OpenRead(Path.Combine(tile.DirectoryOriginal, sFilename));
                rasterBounds.ExpandToInclude(reader.BoundingBox);
            }

            int iRowAndColCount = TopographicDb.iMapTileEdgeLengthInMeters / Tile.EdgeLength * tile.AlphamapResolution;
            rasteriser.InitializeRaster(iRowAndColCount, iRowAndColCount, rasterBounds);

            rasteriser.AddRasterizedClassesWithRasterValues(_nlsClassesToRasterValues);

            foreach (string sFilename in _sShpFilenames)
            {
                rasteriser.RasteriseShapefile(Path.Combine(tile.DirectoryOriginal, sFilename));
            }

            for (int x = (int)bounds12km.MinX; x < (int)bounds12km.MaxX; x += Tile.EdgeLength)
            {
                for (int y = (int)bounds12km.MinY; y < (int)bounds12km.MaxY; y += Tile.EdgeLength)
                {
                    if (tile.Token.IsCancellationRequested)
                        return new ByteRaster();

                    // Save to filesystem
                    string sTileName = TileNamer.Encode(x, y, Tile.EdgeLength);
                    rasteriser.WriteAsAscii(
                        Path.Combine(tile.DirectoryIntermediate, IRasterBuilder.Filename(sTileName, _sRasterFilenameSpecifier, tile.Version)),
                        x, y, x + Tile.EdgeLength, y + Tile.EdgeLength);
                }
            }

            //rasteriser.WriteAsAscii(Path.Combine(tile.DirectoryIntermediate, s12km12kmMapTileName + "_full.asc"));

            Debug.Log($"Rasterising {_sRasterFilenameSpecifier} for 12x12 km2 tile {s12km12kmMapTileName} took {sw.Elapsed.TotalSeconds} s.");

            return rasteriser.Crop((int)bounds.MinX, (int)bounds.MinY,
                (int)bounds.MinX + Tile.EdgeLength, (int)bounds.MinY + Tile.EdgeLength);
        }

        public void SetShpFilenames(string[] inputFilenames)
        {
            _sShpFilenames = inputFilenames;
        }

        public void SetRasterSpecifier(string sSpecifier)
        {
            _sRasterFilenameSpecifier = sSpecifier;
        }

        public void RemoveRasterizedClassesWithRasterValues()
        {
            _nlsClassesToRasterValues.Clear();
        }
    }
}
