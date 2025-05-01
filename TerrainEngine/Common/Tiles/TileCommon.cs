using System.Collections;
using System.Collections.Generic;

namespace Kuoste.TerrainEngine.Common.Tiles
{
    public class TileCommon
    {
        /// <summary>
        /// Unity handles heightmap values as coefficients between 0.0 and 1.0. 
        /// This value should be the same as Terrain Height so that heights are scaled correctly.
        /// </summary>
        public int DemMaxHeight { get; }

        public int AlphamapResolution { get; }

        /// <summary>
        /// Tile edge length in meters
        /// </summary>
        public const int EdgeLength = 1000;
        public string DirectoryIntermediate { get; }
        public string DirectoryOriginal { get; }

        public string Version { get; }

        //public GameObject WaterPlane { get; }
        //public Material BuildingWall { get; }
        //public Material BuildingRoof { get; }

        public TileCommon(int demMaxHeight, int alphamapResolution, string directoryIntermediate, string directoryOriginal,
            string version)
        // string version, GameObject waterPlane, Material buildingWall, Material buildingRoof)
        {
            DemMaxHeight = demMaxHeight;
            AlphamapResolution = alphamapResolution;
            DirectoryIntermediate = directoryIntermediate;
            DirectoryOriginal = directoryOriginal;
            Version = version;

            //WaterPlane = waterPlane;
            //BuildingWall = buildingWall;
            //BuildingRoof = buildingRoof;
        }
    }
}
