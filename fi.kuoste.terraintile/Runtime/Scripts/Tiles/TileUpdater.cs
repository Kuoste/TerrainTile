using Kuoste.TerrainEngine.Common.Tiles;
using LasUtility.Common;
using LasUtility.Nls;
using NetTopologySuite.Geometries;
using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Kuoste.TerrainTile.Tiles
{
    public class TileUpdater : MonoBehaviour
    {
        private Tile _tile;
        private Vector3 _heightmapScale;
        private GameObject _waterPlane;
        private Material _buildingRoof;
        private Material _buildingWall;

        /// <summary>
        /// Offset to adjust the height for the whole system
        /// Depending on Common.DemMaxHeight, if 800 then use 300 for Saana, since highest point 1050
        /// Use 10 or 50 for Helsinki, since some points are below sea level 
        /// </summary>
        private const int iHeightOffset = 50; // Lappland -300, Helsinki 50

        /// <summary>
        /// Used for scaling the trees. Use bigger divider to get smaller trees.
        /// </summary>
        private const int _iTreeHeightDivider = 30;

        /// <summary>
        /// Use to set the depth of the lakes and sea.
        /// </summary>
        private const float _fWaterDepth = 3f;

        public void SetTile(Tile tile)
        {
            _tile = tile;
        }

        public void SetUnityVariables(Vector3 heightmapScale, GameObject waterPlane, Material buildingRoof, Material buildingWall)
        {
            _heightmapScale = heightmapScale;
            _waterPlane = waterPlane;
            _buildingRoof = buildingRoof;
            _buildingWall = buildingWall;
        }

        // Start is called before the first frame update
        void Start()
        {
            Stopwatch swTotal = Stopwatch.StartNew();
            //Stopwatch sw = Stopwatch.StartNew();

            TileNamer.Decode(_tile.Name, out Envelope bounds);

            TerrainData terrainData = GetComponent<Terrain>().terrainData;

            SetAlphaMaps(terrainData);

            //sw.Stop();
            //Debug.Log($"Setting alphamaps for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            //sw.Restart();

            AddWaterPlanes();

            //sw.Stop();
            //Debug.Log($"Setting water planes for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            //sw.Restart();

            AddTrees(terrainData);

            //sw.Stop();
            //Debug.Log($"Setting trees for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            //sw.Restart();

            // Fill the heights for 1 km2 tile in a way that border node heigts are shared between 
            // adjacent tiles


            float[,] fHeights = new float[terrainData.heightmapResolution, terrainData.heightmapResolution];

            SetHeights(bounds, terrainData.heightmapResolution, fHeights);

            terrainData.SetHeights(0, 0, fHeights);

            //terrainData.SyncHeightmap();
            //Terrain.activeTerrain.Flush();
            //terrain.GetComponent<Terrain>().Flush();

            //sw.Stop();
            //Debug.Log($"Setting DEM for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            //sw.Restart();

            AddBuildings();

            //sw.Stop();
            //Debug.Log($"Setting buildings for tile {_tile.Name} took {sw.Elapsed.TotalSeconds} s");
            //sw.Restart();

            swTotal.Stop();
            Debug.Log($"Tile {_tile.Name} drawn in {swTotal.Elapsed.TotalSeconds} s");

            // Remove the tile updater component to save memory
            _tile.Clear();
            Destroy(this);
        }

        private void SetHeights(Envelope bounds, int iHeightmapResolution, float[,] fHeights)
        {
            float fOutOfBoundsLowest = 0, fOutOfBoundsHighest = 0;
            int iOutOfBoundsLowCount = 0, iOutOfBoundsHighCount = 0, iNanCount = 0;

            RcIndex rcStart = _tile.DemDsm.Bounds.ProjToCell(new Coordinate(bounds.MinX, bounds.MinY));

            for (int x = 0; x < iHeightmapResolution; x++)
            {
                for (int y = 0; y < iHeightmapResolution; y++)
                {
                    float h = _tile.DemDsm.Dem[rcStart.Column + x, rcStart.Row + y] + iHeightOffset;

                    if (float.IsNaN(h))
                    {
                        fHeights[x, y] = 0;
                        iNanCount++;
                        continue;
                    }

                    // iHeightMapResolution is one cell bigger that TerrainType (alphamap) resolution,
                    // because the heights need to connect to the next tile. Extend the last values terrain type values to cover the whole tile
                    int tx = x < _tile.Common.AlphamapResolution ? x : _tile.Common.AlphamapResolution - 1;
                    int ty = y < _tile.Common.AlphamapResolution ? y : _tile.Common.AlphamapResolution - 1;
                    byte bTerrainType = (byte)_tile.TerrainType.GetValue(tx, ty);
                    if (TopographicDb.WaterPolygonClassesToRasterValues.ContainsValue(bTerrainType))
                    {
                        // Reduce terrain height inside water areas
                        h -= _fWaterDepth;
                    }

                    if (h < 0.0f)
                    {
                        fOutOfBoundsLowest = Math.Min(fOutOfBoundsLowest, h);
                        iOutOfBoundsLowCount++;
                        h = 0.0f;
                    }
                    else if (h > _heightmapScale.y)
                    {
                        fOutOfBoundsHighest = Math.Max(fOutOfBoundsHighest, h);
                        iOutOfBoundsHighCount++;
                        h = _heightmapScale.y;
                    }

                    fHeights[x, y] = h / _heightmapScale.y;
                }
            }

            if (iOutOfBoundsLowCount > 0)
            {
                Debug.Log($"Found {iOutOfBoundsLowCount} negative DEM heights. Max was {fOutOfBoundsLowest}." +
                    $"iHeightOffset: {iHeightOffset}");
            }

            if (iOutOfBoundsHighCount > 0)
            {
                Debug.Log($"Found {iOutOfBoundsHighCount} DEM heights over {_heightmapScale.y}. Max was {fOutOfBoundsHighest}." +
                    $"iHeightOffset: {iHeightOffset}");
            }

            if (iNanCount > 0)
            {
                Debug.Log($"Dem unavailable on {iNanCount} cells");
            }
        }

        private void AddBuildings()
        {
            foreach (Tile.Building b in _tile.Buildings)
            {
                // Convert System.Numerics.Vector3[] to UnityEngine.Vector3[]. Swap Y and Z coordinates to get upwards Y axis.
                UnityEngine.Vector3[] unityVertices = Array.ConvertAll(b.Vertices, v => new UnityEngine.Vector3(v.X, v.Z, v.Y));

                Mesh mesh = new()
                {
                    vertices = unityVertices,
                    triangles = b.Triangles,
                    subMeshCount = 2
                };

                mesh.SetSubMesh(0, new SubMeshDescriptor(0, b.iSubmeshSeparator));
                mesh.SetSubMesh(1, new SubMeshDescriptor(b.iSubmeshSeparator, b.Triangles.Length - b.iSubmeshSeparator));

                GameObject go = new("Building");
                go.AddComponent<MeshFilter>().mesh = mesh;
                go.AddComponent<MeshRenderer>().materials = new Material[]
                {
                    _buildingRoof,
                    _buildingWall
                };
                go.AddComponent<MeshCollider>().sharedMesh = mesh;

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                go.transform.parent = transform;
                go.transform.SetLocalPositionAndRotation(new Vector3(0, iHeightOffset, 0), Quaternion.identity);
            }
        }

        private void SetAlphaMaps(TerrainData terrainData)
        {
            if (null == _tile.TerrainType || null == _tile.BuildingsRoads)
            {
                return;
            }

            float[,,] alphamaps = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);

            for (int x = 0; x < terrainData.alphamapWidth; x++)
            {
                for (int y = 0; y < terrainData.alphamapHeight; y++)
                {
                    int iLayerToAlter = -1;
                    bool bExpand = false;

                    if (_tile.BuildingsRoads.GetValue(x, y) > 0)
                    {
                        iLayerToAlter = 6;
                        bExpand = true;
                    }
                    else if (_tile.TerrainType.GetValue(x, y) > 0)
                    {
                        byte bTerrainType = (byte)_tile.TerrainType.GetValue(x, y);

                        if (TopographicDb.WaterPolygonClassesToRasterValues.ContainsValue(bTerrainType))
                        {
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
                float fScale = (float)_tile.Trees[t].Z / _iTreeHeightDivider;

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

            //GameObject goPlane = Resources.Load<GameObject>("Prefabs/WaterPlane");
            GameObject goPlane = _waterPlane;

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
                    fWaterHeight + iHeightOffset,
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
