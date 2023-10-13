using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kuoste.LidarWorld.Tile
{
    public class Tile
    {
        public string Name;
        public int Index;

        public int OffsetX;
        public int OffsetY;


        //public GameObject Terrain;
        public VoxelGrid TerrainGrid;

        public List<CoordinateZ> Trees = new();

        public int CompletedCount;

    }
}
