//using LasUtility.Common;
//using LasUtility.DEM;
//using LasUtility.LAS;
//using LasUtility.Nls;
//using LasUtility.ShapefileRasteriser;
//using LasUtility.VoxelGrid;
//using NetTopologySuite.Features;
//using NetTopologySuite.Geometries;
//using NetTopologySuite.IO.Esri;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.IO;
//using System.Linq;
//using System.Threading;
//using Debug = UnityEngine.Debug;

//namespace Kuoste.LidarWorld.Tile
//{
//    public class TileCreator : ITileBuilder
//    {
//        /// <summary>
//        /// Currently only the 3x3 km2 las tiles are supported.
//        /// </summary>
//        const int m_iSupportedInputTileWidth = 3000;





//        public string DirectoryIntermediate { get; set; }
//        public string DirectoryOriginal { get; set; }

//        public ConcurrentDictionary<string, bool> DemDsmDone => _1kmDemDsmDone;

//        /// <summary>
//        /// For saving the status of the 1x1 km2 tiles so that they are available for the Geometry service
//        /// </summary>
//        private readonly ConcurrentDictionary<string, bool> _1kmDemDsmDone = new();

//        /// <summary>
//        /// Keep track of the las files so that we don't try to process the same tile multiple times.
//        /// </summary>
//        private readonly ConcurrentDictionary<string, bool> _3kmDemDsmDone = new();



//        ///// <summary>
//        ///// Keep track of the received tiles so that we can update the tile features when the tile is ready.
//        ///// </summary>
//        //private ConcurrentDictionary<string, Tile> _tilesReceived = new();

//        /// <summary>
//        /// For detecting when we should stop building tiles.
//        /// </summary>
//        private CancellationToken _token;

//        public void SetCancellationToken(CancellationToken token)
//        {
//            _token = token;
//        }

//        public void BuildDemAndDsmPointCloud(Tile tile)
//        {
 
//        }

//        public void BuildTerrainTypeRaster(Tile tile)
//        {

//        }

//        public void BuildRoadRaster(Tile tile)
//        {
 
//        }

//        public void BuildBuildings(Tile tile)
//        {

//        }

        

        
//    }
//}
