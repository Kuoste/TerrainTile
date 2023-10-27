using LasUtility.Common;
using LasUtility.VoxelGrid;
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

        public void BuildBuildingVertices(Tile tile)
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

            tile.TerrainType = HeightMap.CreateFromAscii(sFullFilename);

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
