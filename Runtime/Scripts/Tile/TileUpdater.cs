using LasUtility.Nls;
using NetTopologySuite.Geometries;
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
            Stopwatch sw = Stopwatch.StartNew();

            TerrainData terrainData = GetComponent<Terrain>().terrainData;

            SetAlphaMaps(terrainData);

            sw.Stop();
            Debug.Log($"Setting alphamaps for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            sw.Restart();

            AddWaterPlanes();

            sw.Stop();
            Debug.Log($"Setting water planes for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            sw.Restart();

            AddTrees(terrainData);

            sw.Stop();
            Debug.Log($"Setting trees for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            sw.Restart();

            // Scale terrain height
            for (int x = 0; x < terrainData.heightmapResolution; x++)
            {
                for (int y = 0; y < terrainData.heightmapResolution; y++)
                {
                    _tile.TerrainGrid.Dem[x, y] /= _tile.DemMaxHeight;
                }
            }

            terrainData.SetHeights(0, 0, _tile.TerrainGrid.Dem);

            //terrainData.SyncHeightmap();
            //Terrain.activeTerrain.Flush();
            //terrain.GetComponent<Terrain>().Flush();

            sw.Stop();
            Debug.Log($"Setting DEM for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            sw.Restart();

            AddBuildings();

            sw.Stop();
            Debug.Log($"Setting buildings for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            sw.Restart();
        }

        private void AddBuildings()
        {
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

        private void SetAlphaMaps(TerrainData terrainData)
        {
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
                            // Reduce terrain height inside water areas
                            _tile.TerrainGrid.Dem[x, y] -= 1.5f;

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
        }

        private void AddTrees(TerrainData terrainData)
        {
            TreeInstance[] trees = new TreeInstance[_tile.Trees.Count];

            for (int t = 0; t < _tile.Trees.Count; t++)
            {
                float fScale = (float)_tile.Trees[t].Z / 40;

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
        }

        private void AddWaterPlanes()
        {
            TileNamer.Decode(_tile.Name, out Envelope bounds);

            GameObject goPlane = Resources.Load<GameObject>("Prefabs/WaterPlane");

            Bounds goPlaneBounds = goPlane.GetComponent<MeshFilter>().sharedMesh.bounds;
            float fGoPlaneMeshWidth = goPlaneBounds.size.x;
            float fGoPlaneMeshHeight = goPlaneBounds.size.z;

            foreach (Polygon p in _tile.WaterAreas)
            {
                // Read water surface height. All points have the same height
                float fWaterHeight = (float)p.Coordinates[0].Z;

                Envelope area = p.EnvelopeInternal;

                // Expand area for better coverage
                area.ExpandBy(5);

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
        }

        private static void SetAlphamapLayerToMax(int iLayerCount, float[,,] alphamaps, int x, int y, int iLayer)
        {
            for (int a = 0; a < iLayerCount; a++)
            {
                alphamaps[x, y, a] = 0;
            }

            alphamaps[x, y, iLayer] = 1.0f;
        }
    }
}
