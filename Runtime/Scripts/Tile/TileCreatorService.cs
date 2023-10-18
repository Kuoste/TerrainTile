//using System.Collections;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Threading;
//using UnityEngine;

//namespace Kuoste.LidarWorld.Tile
//{
//    public class TileCreatorService
//    {
//        private readonly string _sDirectoryIntermediate;
//        private readonly string _sVersion;

//        private readonly List<Thread> _readerThreads = new();
//        private readonly ConcurrentQueue<Tile> _tileQueueDemAndDsmPointCloud = new();
//        //readonly ConcurrentQueue<Tile> _tilesPostphoned = new();

//        private readonly Thread DemAndDsmPointCloudThread;

//        private readonly Thread BuildRoadRasterThread;

//        private readonly Thread BuildTerrainTypeRasterThread;

//        private readonly Thread BuildBuildingPolygonsThread;

//        public void AddTerrain(Tile tile)
//        {
//            _tileQueue.Enqueue(tile);
//        }

//        public TileReaderService(string sDirectoryIntermediate, string sVersion, int iThreadCount)
//        {
//            _sDirectoryIntermediate = sDirectoryIntermediate;
//            _sVersion = sVersion;

//            for (int i = 0; i < iThreadCount; i++)
//            {
//                Thread t = new(() => LoaderThread());
//                t.Start();
//                _readerThreads.Add(t);
//            }
//        }
//    }
//}
