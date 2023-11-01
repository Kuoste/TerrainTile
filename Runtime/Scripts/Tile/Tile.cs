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
        /// <summary>
        /// Unity handles heightmap values as coefficients between 0.0 and 1.0. 
        /// This value should be the same as Terrain Height so that heights are scaled correctly.
        /// </summary>
        public float DemMaxHeight;

        /// <summary>
        /// Tile edge length in meters
        /// </summary>
        public const int EdgeLength = 1000;

        public string Version;
        public string Name;

        //public GameObject Terrain;
        public VoxelGrid TerrainGrid;

        public HeightMap Roads;
        public HeightMap TerrainType;

        public List<CoordinateZ> Trees;
        public List<Vector3[]> BuildingVertices;
        public List<int[]> BuildingTriangles;
        public List<int> BuildingSubmeshSeparator; // Each building contains 2 submeshes: walls and roof
        public List<Envelope> WaterAreas;

        public int CompletedCount;

        // Everything is done when all four content types i.e. terraingrid, roads, terrainfeatures and buildings are built
        public bool IsCompleted => Interlocked.Add(ref CompletedCount, 0) >= 4;

        public string FilenameGrid => Name + "_v" + Version + ".grid";
        public string FilenameRoads => Name + "_roads_v" + Version + ".asc";
        public string FilenameTerrainType => Name + "_terraintype_v" + Version + ".asc";
        public string FilenameWaterAreas => Name + "_waterareas_v" + Version + ".txt";
        public string FilenameBuildings => Name + "_buildings_v" + Version + ".obj";

    }
}
