using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Tile
{
    public class TileGeometryService : TileService, ITileBuilderService
    {
        private readonly IBuildingsBuilder _buildingsReader, _buildingsCreator;
        private readonly ITreeBuilder _treeReader, _treeCreator;
        private readonly IWaterAreasBuilder _waterAreasReader, _waterAreasCreator;

        public TileGeometryService(IBuildingsBuilder buildingsReader, IBuildingsBuilder buildingsCreator,
            ITreeBuilder treeReader, ITreeBuilder treeCreator,
            IWaterAreasBuilder waterAreasReader, IWaterAreasBuilder waterAreasCreator, CancellationToken token)
        {
            _buildingsReader = buildingsReader;
            _buildingsCreator = buildingsCreator;

            _buildingsReader.SetCancellationToken(token);
            _buildingsCreator.SetCancellationToken(token);

            _treeReader = treeReader;
            _treeCreator = treeCreator;

            _treeReader.SetCancellationToken(token);
            _treeCreator.SetCancellationToken(token);

            _waterAreasReader = waterAreasReader;
            _waterAreasCreator = waterAreasCreator;

            _waterAreasReader.SetCancellationToken(token);
            _waterAreasCreator.SetCancellationToken(token);

            _token = token;
        }

        public void BuilderThread()
        {
            while (true)
            {
                if (_token.IsCancellationRequested)
                    return;

                if (_tileQueue.TryPeek(out Tile tile))
                {
                    // Geometries require that other components are ready
                    bool bAreOthersReady = Interlocked.Read(ref tile.CompletedCount) >= (tile.CompletedRequired - 1);

                    if (true == bAreOthersReady && _tileQueue.TryDequeue(out tile))
                    {
                        Stopwatch swCreate = new();
                        Stopwatch swRead = new();

                        string sFullFilename = Path.Combine(tile.DirectoryIntermediate, IBuildingsBuilder.Filename(tile.Name, tile.Version));
                        if (!File.Exists(sFullFilename))
                        {
                            // Create from shapefiles and DSM
                            swCreate.Start();
                            tile.Buildings = _buildingsCreator.Build(tile);
                            swCreate.Stop();
                        }
                        else
                        {
                            // Read from file
                            swRead.Start();
                            tile.Buildings = _buildingsReader.Build(tile);
                            swRead.Stop();
                        }

                        sFullFilename = Path.Combine(tile.DirectoryIntermediate, ITreeBuilder.Filename(tile.Name, tile.Version));
                        if (!File.Exists(sFullFilename))
                        {
                            // Create from DSM
                            swCreate.Start();
                            tile.Trees = _treeCreator.Build(tile);
                            swCreate.Stop();
                        }
                        else
                        {
                            // Read from file
                            swRead.Start();
                            tile.Trees = _treeReader.Build(tile);
                            swRead.Stop();
                        }

                        sFullFilename = Path.Combine(tile.DirectoryIntermediate, IWaterAreasBuilder.Filename(tile.Name, tile.Version));
                        if (!File.Exists(sFullFilename))
                        {
                            // Create from shapefiles
                            swCreate.Start();
                            tile.WaterAreas = _waterAreasCreator.Build(tile);
                            swCreate.Stop();
                        }
                        else
                        {
                            // Read from file
                            swRead.Start();
                            tile.WaterAreas = _waterAreasReader.Build(tile);
                            swRead.Stop();
                        }

                        Interlocked.Increment(ref tile.CompletedCount);

                        Debug.Log($"Tile {tile.Name} geometries created in {swCreate.Elapsed.TotalSeconds} s " +
                            $"and read in {swRead.Elapsed.TotalSeconds} s.");

                        Thread.Sleep(10);
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
                else
                {
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
