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
        public int DemMaxHeight;

        public int AlphamapResolution;
        //public int HeightMapResolution;

        /// <summary>
        /// Tile edge length in meters
        /// </summary>
        public const int EdgeLength = 1000;

        public string Version;
        public string Name;

        public string DirectoryIntermediate;
        public string DirectoryOriginal;

        public VoxelGrid DemDsm;
        public IRaster BuildingsRoads;
        public IRaster TerrainType;
        public List<Point> Trees = new();
        public List<Building> Buildings = new();
        public List<Polygon> WaterAreas = new();

        public struct Building
        {
            public Vector3[] Vertices;
            public int[] Triangles;
            public int iSubmeshSeparator; // Each building contains 2 submeshes: walls and roof
        }

        public long CompletedCount;
        public long CompletedRequired => 3;

        // Everything is done when all 3 content types i.e. DemDsm, rasters, geometries are built
        public bool IsCompleted => Interlocked.Read(ref CompletedCount) >= CompletedRequired;

        public void Clear()
        {
            Buildings.Clear();
            Trees.Clear();
            WaterAreas.Clear();
            DemDsm = null;
            BuildingsRoads = null;
            TerrainType = null;
        }
    }
}
