using LasUtility.NlsTileName;
using NetTopologySuite.Geometries;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Terrain
{
    public class GridManager
    {
        public enum TileStatus
        {
            NOT_FOUND,
            READY,
            QUEUED
        }

        private readonly List<string> _lasFilesNotFound = new();
        private readonly List<string> _lasFilesQueued = new();

        private readonly string _sDirectoryOriginal;
        private readonly string _sDirectoryIntermediate;
        private readonly string _sVersion;

        readonly ConcurrentQueue<string> _lasQueue = new();

        readonly Thread _consumingThread;

        public GridManager(string sDirectoryOriginal, string sDirectoryIntermediate, string sVersion)
        {
            _sDirectoryOriginal = sDirectoryOriginal;
            _sDirectoryIntermediate = sDirectoryIntermediate;
            _sVersion = sVersion;

            _consumingThread = new Thread(() =>
             {
                 ITerrainProvider tp = new TerrainCreator();

                 if (!Directory.Exists(sDirectoryIntermediate))
                     Directory.CreateDirectory(sDirectoryIntermediate);

                 while (true)
                 {
                     if (_lasQueue.Count > 0 && _lasQueue.TryDequeue(out string s3km3kmTileName))
                     {
                         Debug.Log($"New job found. Starting to process {s3km3kmTileName}.");
                         Stopwatch sw = Stopwatch.StartNew();

                         // Create grids for the whole las file
                         var grids = tp.GetTerrain(sDirectoryOriginal, s3km3kmTileName, sVersion);

                         // Save results
                         foreach (var g in grids)
                             g.Value.Serialize(sDirectoryIntermediate);

                         Debug.Log($"Background thread finished in {sw.Elapsed.TotalSeconds} seconds");
                     }

                     Thread.Sleep(1000);
                 }
             });

            _consumingThread.Start();
        }
        public TileStatus GetTile(string s1km1kmTileName)
        {
            // First look if we already have the wanted tile
            string sFilename = Path.Combine(_sDirectoryIntermediate, s1km1kmTileName + "_v" + _sVersion + ".obj");

            if (File.Exists(sFilename))
            {
                return TileStatus.READY;
            }

            // Use NlsTileNamer to get the upper level 3km x 3 km tile name
            const int iWantedSizeInMeters = 3000;
            NlsTileNamer.Decode(s1km1kmTileName, out Envelope extent);
            string s3km3kmTileName = NlsTileNamer.Encode((int)extent.MinX, (int)extent.MinY, iWantedSizeInMeters);

            // Check if we have the laz file
            if (_lasFilesNotFound.Contains(s3km3kmTileName))
            {
                return TileStatus.NOT_FOUND;
            }

            sFilename = Path.Combine(_sDirectoryOriginal, s3km3kmTileName + ".laz");
            if (!File.Exists(sFilename))
            {
                _lasFilesNotFound.Add(s3km3kmTileName);
                return TileStatus.NOT_FOUND;
            }

            // Look if the file is already in queue
            if (_lasFilesQueued.Contains(s3km3kmTileName))
            {
                return TileStatus.QUEUED;
            }

            // Add to queue
            _lasQueue.Enqueue(s3km3kmTileName);
            return TileStatus.QUEUED;
        }
    }
}
