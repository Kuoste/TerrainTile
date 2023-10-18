using LasUtility.Common;
using LasUtility.VoxelGrid;
using System.IO;
using System.Threading;

namespace Kuoste.LidarWorld.Tile
{
    public class TileReader : ITileBuilder
    {
        private string _sIntermediateDirectory;

        public void BuildBuildingPolygons(Tile tile)
        {
            throw new System.NotImplementedException();
        }

        public void BuildRoadRaster(Tile tile)
        {
            string sFullFilename = Path.Combine(_sIntermediateDirectory, tile.FilenameRoads);

            tile.Roads = HeightMap.CreateFromAscii(sFullFilename);

            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildDemAndDsmPointCloud(Tile tile)
        {
            string sFullFilename = Path.Combine(_sIntermediateDirectory, tile.FilenameGrid);

            tile.TerrainGrid = VoxelGrid.Deserialize(sFullFilename);

            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void BuildTerrainTypeRaster(Tile tile)
        {
            string sFullFilename = Path.Combine(_sIntermediateDirectory, tile.FilenameTerrainType);

            tile.TerrainType = HeightMap.CreateFromAscii(sFullFilename);

            Interlocked.Increment(ref tile.CompletedCount);
        }

        public void SetIntermediateDirectory(string sDirectory)
        {
            _sIntermediateDirectory = sDirectory;
        }

        public void SetOriginalDirectory(string sDirectory)
        {
            throw new System.NotImplementedException();
        }
    }
}
