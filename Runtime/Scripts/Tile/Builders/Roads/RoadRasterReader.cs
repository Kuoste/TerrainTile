using LasUtility.Common;
using System.IO;
using System.Threading;

namespace Kuoste.LidarWorld.Tile
{
    public class RoadRasterReader : IRoadRasterBuilder
    {
        public IRaster Build(Tile tile)
        {
            if (tile.Token.IsCancellationRequested)
                return new HeightMap();

            string sFullFilename = Path.Combine(tile.DirectoryIntermediate, IRoadRasterBuilder.Filename(tile.Name, tile.Version));

            return HeightMap.CreateFromAscii(sFullFilename);
        }
    }
}
