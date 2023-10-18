using LasUtility.Common;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Kuoste.LidarWorld.Tile
{
    public class Tile
    {
        public const int EdgeLength = 1000;

        public string Version;
        public string Name;

        //public GameObject Terrain;
        public VoxelGrid TerrainGrid;

        public HeightMap Roads;
        public HeightMap TerrainType;

        public List<CoordinateZ> Trees;
        public List<Polygon> Buildings;

        public int CompletedCount;

        // Everything is done when all four content types i.e. terraingrid, roads, terrainfeatures and buildings are built
        public bool IsCompleted => Interlocked.Add(ref CompletedCount, 0) >= 3;

        public string FilenameGrid => Name + "_v" + Version + ".grid";
        public string FilenameRoads => Name + "_roads_v" + Version + ".asc";
        public string FilenameTerrainType => Name + "_terraintype_v" + Version + ".asc";

    }
}
