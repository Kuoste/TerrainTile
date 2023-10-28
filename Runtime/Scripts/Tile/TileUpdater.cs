using LasUtility.Nls;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Kuoste.LidarWorld.Tile
{
    public class TileUpdater : MonoBehaviour
    {
        private Tile _tile;

        public void SetTile(Tile tile)
        {
            _tile = tile;
        }

        // Start is called before the first frame update
        void Start()
        {
            TerrainData terrainData = GetComponent<Terrain>().terrainData;

            Stopwatch sw = Stopwatch.StartNew();

            float[,,] alphamaps = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);

            for (int x = 0; x < terrainData.alphamapWidth; x++)
            {
                for (int y = 0; y < terrainData.alphamapHeight; y++)
                {
                    //float fTotal = 0.0f;
                    //float fNoiseScale = 8f;

                    if (_tile.Roads.Raster[x][y] > 0)
                    {
                        for (int a = 0; a < terrainData.alphamapLayers; a++)
                        {
                            alphamaps[x, y, a] = 0;
                        }
                        alphamaps[x, y, 6] = 1.0f;

                    }
                    else if (_tile.TerrainType.Raster[x][y] > 0)
                    {
                        for (int a = 0; a < terrainData.alphamapLayers; a++)
                        {
                            alphamaps[x, y, a] = 0;
                        }
                        alphamaps[x, y, 1] = 1.0f;
                    }
                    //else
                    //{
                    //    for (int a = 0; a < terrainData.alphamapLayers; a++)
                    //    {
                    //        float v = alphamaps[x, y, a];
                    //        v += UnityEngine.Random.value * fNoiseScale;
                    //        fTotal += v;
                    //        alphamaps[x, y, a] = v;
                    //    }

                    //    for (int a = 0; a < terrainData.alphamapLayers; a++)
                    //    {
                    //        alphamaps[x, y, a] /= fTotal;
                    //    }
                    //}
                }
            }

            terrainData.SetAlphamaps(0, 0, alphamaps);

            sw.Stop();
            Debug.Log($"Setting alphamaps for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            sw.Restart();

            _tile.Trees = new();
            float fMaxHeightForTile = float.MinValue;

            for (int iRow = 0; iRow < _tile.TerrainGrid.Bounds.RowCount; iRow++)
            {
                for (int jCol = 0; jCol < _tile.TerrainGrid.Bounds.ColumnCount; jCol++)
                {
                    const int iRadius = 2;
                    int iHighVegetationCount = 0;

                    List<BinPoint> centerPoints = _tile.TerrainGrid.GetPoints(iRow, jCol);

                    if (centerPoints.Count == 0 || centerPoints[0].Class != (byte)PointCloud05p.Classes.HighVegetation)
                    {
                        continue;
                    }

                    float fMaxHeight = float.MinValue;

                    for (int ii = iRow - iRadius; ii <= iRow + iRadius; ii++)
                    {
                        for (int jj = jCol - iRadius; jj <= jCol + iRadius; jj++)
                        {
                            if (ii < 0 || ii > _tile.TerrainGrid.Bounds.RowCount - 1 ||
                                jj < 0 || jj > _tile.TerrainGrid.Bounds.ColumnCount - 1)
                            {
                                continue;
                            }

                            List<BinPoint> neighborhoodPoints = _tile.TerrainGrid.GetPoints(ii, jj);

                            foreach (BinPoint p in neighborhoodPoints)
                            {
                                if (p.Class == (byte)PointCloud05p.Classes.HighVegetation)
                                {
                                    fMaxHeight = Math.Max(fMaxHeight, p.Z);

                                    iHighVegetationCount++;
                                }
                                else
                                {
                                    // Points are sorted by descending height so after high vegetation
                                    // there are no more high vegetation points
                                    break;
                                }
                            }
                        }

                        if (iHighVegetationCount < 15 || fMaxHeight > centerPoints[0].Z)
                        {
                            continue;
                        }

                        fMaxHeight -= _tile.TerrainGrid.GetGroundHeight(iRow, jCol);

                        // Ground height is not always available (e.g. triangulation on corners of the tile)
                        if (float.IsNaN(fMaxHeight))
                        {
                            continue;
                        }

                        fMaxHeightForTile = Math.Max(fMaxHeightForTile, fMaxHeight);

                        _tile.Trees.Add(new(iRow, jCol, fMaxHeight));
                    }
                }
            }


            sw.Stop();
            Debug.Log($"Tile {_tile.Name}: {_tile.Trees.Count} trees determined in {sw.ElapsedMilliseconds} ms.");
            sw.Restart();

            TreeInstance[] trees = new TreeInstance[_tile.Trees.Count];

            for (int t = 0; t < _tile.Trees.Count; t++)
            {
                float fScale = (float)_tile.Trees[t].Z / fMaxHeightForTile;

                trees[t] = new()
                {
                    position = new Vector3((float)_tile.Trees[t].Y / _tile.TerrainGrid.Bounds.ColumnCount, 0,
                                           (float)_tile.Trees[t].X / _tile.TerrainGrid.Bounds.RowCount),
                    prototypeIndex = UnityEngine.Random.Range(0, terrainData.treePrototypes.Length),
                    widthScale = fScale,
                    heightScale = fScale,
                    rotation = UnityEngine.Random.Range(0f, 360f),
                    color = new Color(223, 223, 223, 255),
                    lightmapColor = new Color(255, 255, 255, 255),
                };
            }

            terrainData.SetTreeInstances(trees, true);

            sw.Stop();
            Debug.Log($"Setting trees for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");

            terrainData.SetHeights(0, 0, _tile.TerrainGrid.Dem);

            //terrainData.SyncHeightmap();
            //Terrain.activeTerrain.Flush();
            //terrain.GetComponent<Terrain>().Flush();


            // Instantiate buildings
            for (int i = 0; i < _tile.BuildingVertices.Count; i++)
            {
                Mesh mesh = new()
                {
                    vertices = _tile.BuildingVertices[i],
                    triangles = _tile.BuildingTriangles[i]
                };

                mesh.subMeshCount = 2;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, _tile.BuildingSubmeshSeparator[i]));
                mesh.SetSubMesh(1, new SubMeshDescriptor(_tile.BuildingSubmeshSeparator[i], _tile.BuildingTriangles[i].Length - _tile.BuildingSubmeshSeparator[i]));

                GameObject go = new("Building");
                go.AddComponent<MeshFilter>().mesh = mesh;
                go.AddComponent<MeshRenderer>().materials = new Material[]
                {
                    Resources.Load<Material>("Materials/BuildingWall_Mat"),
                    Resources.Load<Material>("Materials/BuildingRoof_Mat")
                };

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                go.transform.parent = transform;
                go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
