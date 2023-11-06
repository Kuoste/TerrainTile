using LasUtility.Common;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Kuoste.LidarWorld.Tile
{
    public class TileReader : ITileBuilder
    {
        public string DirectoryIntermediate { get; set; }
        public string DirectoryOriginal { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public ConcurrentDictionary<string, bool> DemDsmDone => _1kmDemDsmDone;
        public ConcurrentDictionary<string, bool> _1kmDemDsmDone = new();

        public void BuildGeometries(Tile tile)
        {
            throw new System.NotImplementedException();
        }

        public void BuildRoadRaster(Tile tile)
        {
            string sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameRoads);

            tile.Roads = HeightMap.CreateFromAscii(sFullFilename);

            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildDemAndDsmPointCloud(Tile tile)
        {
            string sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameGrid);

            tile.TerrainGrid = VoxelGrid.Deserialize(sFullFilename);

            _1kmDemDsmDone.TryAdd(tile.Name, true);
            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildTerrainTypeRaster(Tile tile)
        {
            string sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameTerrainType);

            // Load terrain type raster

            tile.TerrainType = HeightMap.CreateFromAscii(sFullFilename);


            // Load water areas

            sFullFilename = Path.Combine(DirectoryIntermediate, tile.FilenameWaterAreas);
            tile.WaterAreas = new();

            using StreamReader sr = new(sFullFilename);

            while (sr.Peek() >= 0)
            {
                string[] sValues = sr.ReadLine().Split(' ');

                if (sValues.Length != 4)
                    throw new System.Exception("Invalid water area file format");

                Envelope e = new(
                    int.Parse(sValues[0]),
                    int.Parse(sValues[2]),
                    int.Parse(sValues[1]),
                    int.Parse(sValues[3]));

                tile.WaterAreas.Add(e);
            }

            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void SetIntermediateDirectory(string sDirectory)
        {
            DirectoryIntermediate = sDirectory;
        }

        public void SetOriginalDirectory(string sDirectory)
        {
            throw new System.NotImplementedException();
        }
    }
}
