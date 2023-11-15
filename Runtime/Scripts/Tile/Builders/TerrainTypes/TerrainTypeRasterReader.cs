using Kuoste.LidarWorld.Tile;
using LasUtility.Common;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

public class TerrainTypeReader : ITerrainTypeBuilder
{
    public IRaster Build(Tile tile)
    {
        if (tile.Token.IsCancellationRequested)
            return new HeightMap();

        string sFullFilename = Path.Combine(tile.DirectoryIntermediate, ITerrainTypeBuilder.Filename(tile.Name, tile.Version));

        return HeightMap.CreateFromAscii(sFullFilename);
    }
}
