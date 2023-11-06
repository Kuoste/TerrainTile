using LasUtility.Nls;
using LasUtility.VoxelGrid;
using NetTopologySuite.Geometries;
using NetTopologySuite.Noding;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
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
            Stopwatch sw = Stopwatch.StartNew();

            TileNamer.Decode(_tile.Name, out Envelope bounds);

            TerrainData terrainData = GetComponent<Terrain>().terrainData;

            float[,,] alphamaps = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);

            for (int x = 0; x < terrainData.alphamapWidth; x++)
            {
                for (int y = 0; y < terrainData.alphamapHeight; y++)
                {
                    int iLayerToAlter = -1;
                    bool bExpand = false;

                    if (_tile.Roads.Raster[x][y] > 0)
                    {
                        iLayerToAlter = 6;
                        bExpand = true;
                    }
                    else if (_tile.TerrainType.Raster[x][y] > 0)
                    {
                        byte bTerrainType = _tile.TerrainType.Raster[x][y];

                        if (TopographicDb.WaterPolygonClassesToRasterValues.ContainsValue(bTerrainType))
                        {
                            // Reduce terrain height for water areas
                            _tile.TerrainGrid.Dem[x, y] -= 1.5f / _tile.DemMaxHeight;

                            iLayerToAlter = 4;
                        }
                        else if (TopographicDb.FieldPolygonClassesToRasterValues.ContainsValue(bTerrainType))
                        {
                            //terrainData.GetDetailLayer(0, 0, terrainData.detailWidth, terrainData.detailHeight, 0)[x, y] = 1;
                            iLayerToAlter = 2;
                        }
                        else if (TopographicDb.SwampPolygonClassesToRasterValues.ContainsValue(bTerrainType))
                        {
                            iLayerToAlter = 0;
                        }
                        else if (TopographicDb.RockPolygonClassesToRasterValues.ContainsValue(bTerrainType))
                        {
                            iLayerToAlter = 5;
                        }
                        else if (TopographicDb.SandPolygonClassesToRasterValues.ContainsValue(bTerrainType))
                        {
                            iLayerToAlter = 4;
                        }
                        else if (TopographicDb.RockLineClassesToRasterValues.ContainsValue(bTerrainType))
                        {
                            iLayerToAlter = 4;
                            bExpand = true;
                        }

                    }

                    if (iLayerToAlter >= 0)
                    {
                        int iLayerCnt = terrainData.alphamapLayers;
                        SetAlphamapLayerToMax(iLayerCnt, alphamaps, x, y, iLayerToAlter);

                        if (true == bExpand)
                        {
                            if (x > 0)
                            {
                                SetAlphamapLayerToMax(iLayerCnt, alphamaps, x - 1, y, iLayerToAlter);
                            }
                            if (x < terrainData.alphamapWidth - 1)
                            {
                                SetAlphamapLayerToMax(iLayerCnt, alphamaps, x + 1, y, iLayerToAlter);
                            }
                            if (y > 0)
                            {
                                SetAlphamapLayerToMax(iLayerCnt, alphamaps, x, y - 1, iLayerToAlter);
                            }
                            if (y < terrainData.alphamapHeight - 1)
                            {
                                SetAlphamapLayerToMax(iLayerCnt, alphamaps, x, y + 1, iLayerToAlter);
                            }
                        }
                    }
                }
            }

            terrainData.SetAlphamaps(0, 0, alphamaps);

            // Add water planes

            GameObject goPlane = Resources.Load<GameObject>("Prefabs/WaterPlane");

            // Find size of the goPlane
            Bounds goPlaneBounds = goPlane.GetComponent<MeshFilter>().sharedMesh.bounds;
            float fGoPlaneMeshWidth = goPlaneBounds.size.x;
            float fGoPlaneMeshHeight = goPlaneBounds.size.z;

            foreach (Envelope area in _tile.WaterAreas)
            {
                // Read water surface height
                // todo wont work when centre is outside the lake
                float fWaterHeight = (float)_tile.TerrainGrid.GetHeight(area.Centre.X, area.Centre.Y) * _tile.DemMaxHeight + 1;

                GameObject go = Instantiate(goPlane);
                go.transform.parent = transform;
                go.transform.localScale = new(
                    (float)area.Width / fGoPlaneMeshWidth, 
                    1, 
                    (float)area.Height / fGoPlaneMeshHeight);
                
                Vector3 position = new(
                    (float)(area.Centre.X - bounds.MinX), 
                    fWaterHeight, 
                    (float)(area.Centre.Y - bounds.MinY));

                go.transform.SetLocalPositionAndRotation(position, Quaternion.identity);
            }

            sw.Stop();
            Debug.Log($"Setting alphamaps for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            sw.Restart();

            TreeInstance[] trees = new TreeInstance[_tile.Trees.Count];

            for (int t = 0; t < _tile.Trees.Count; t++)
            {
                float fScale = 1f;//  (float)_tile.Trees[t].Z / fMaxHeightForTile;

                trees[t] = new()
                {
                    position = new Vector3((float)_tile.Trees[t].X, 0, (float)_tile.Trees[t].Y),
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
                    triangles = _tile.BuildingTriangles[i],
                    subMeshCount = 2
                };

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

        private static void SetAlphamapLayerToMax(int iLayerCount, float[,,] alphamaps, int x, int y, int iLayer)
        {
            for (int a = 0; a < iLayerCount; a++)
            {
                alphamaps[x, y, a] = 0;
            }

            alphamaps[x, y, iLayer] = 1.0f;
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
