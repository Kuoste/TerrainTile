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
        public CancellationToken Token;

        /// <summary>
        /// Unity handles heightmap values as coefficients between 0.0 and 1.0. 
        /// This value should be the same as Terrain Height so that heights are scaled correctly.
        /// </summary>
        public int DemMaxHeight;

        public int AlphamapResolution;
        public int HeightMapResolution;

        /// <summary>
        /// Tile edge length in meters
        /// </summary>
        public const int EdgeLength = 1000;

        public string Version;
        public string Name;

        public string DirectoryIntermediate;
        public string DirectoryOriginal;

        public VoxelGrid DemDsm;
        public IRaster Roads;
        public IRaster TerrainType;
        public List<Point> Trees;
        public List<Building> Buildings;
        public List<Polygon> WaterAreas;

        public struct Building
        {
            public Vector3[] Vertices;
            public int[] Triangles;
            public int iSubmeshSeparator; // Each building contains 2 submeshes: walls and roof
        }

        public long CompletedCountDemDsm;
        public long CompletedCountOther;

        // Everything is done when all 6 content types i.e. terraingrid, roads, terrainfeatures, buildings, trees, water areas are built
        public bool IsCompleted => Interlocked.Read(ref CompletedCountDemDsm) > 0 && Interlocked.Read(ref CompletedCountOther) >= 5;


        //public string FilenameGrid => Name + "_v" + Version + ".grid";
        //public string FilenameRoads => Name + "_roads_v" + Version + ".asp";
        //public string FilenameTerrainType => Name + "_terraintype_v" + Version + ".asp";
        //public string FilenameWaterAreas => Name + "_waterareas_v" + Version + ".geojson";
        //public string FilenameBuildings => Name + "_buildings_v" + Version + ".geojson";
        //public string FilenameTrees => Name + "_trees_v" + Version + ".geojson";

        public void Clear()
        {
            Buildings.Clear();
            Trees.Clear();
            WaterAreas.Clear();
            DemDsm = null;
            Roads = null;
            TerrainType = null;
        }
    }
}
