//using LasUtility.Common;
//using LasUtility.Nls;
//using LasUtility.VoxelGrid;
//using NetTopologySuite.Geometries;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.IO;
//using System.Threading;
//using UnityEngine;

//namespace Kuoste.LidarWorld.Tile
//{
//    public class TileReader : ITileBuilder
//    {
//        public string DirectoryIntermediate { get; set; }
//        public string DirectoryOriginal { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

//        public ConcurrentDictionary<string, bool> DemDsmDone => _1kmDemDsmDone;

//        /// <summary>
//        /// For saving the status of the 1x1 km2 tiles so that they are available for the Geometry service
//        /// </summary>
//        private readonly ConcurrentDictionary<string, bool> _1kmDemDsmDone = new();

//        /// <summary>
//        /// For detecting when we should stop building tiles.
//        /// </summary>
//        private CancellationToken _token;

//        public void SetCancellationToken(CancellationToken token)
//        {
//            _token = token;
//        }

//        public void BuildBuildings(Tile tile)
//        {
  
//        }

//        public void BuildRoadRaster(Tile tile)
//        {

//        }

//        public void BuildDemAndDsmPointCloud(Tile tile)
//        {

//        }

//        public void BuildTerrainTypeRaster(Tile tile)
//        {

//        }

//        public void SetIntermediateDirectory(string sDirectory)
//        {
//            DirectoryIntermediate = sDirectory;
//        }

//        public void SetOriginalDirectory(string sDirectory)
//        {
//            throw new System.NotImplementedException();
//        }
     


//    }
//}
