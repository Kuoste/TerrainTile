using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using NUnit.Framework;
using UnityEngine.TestTools;
using System.Threading;
using Kuoste.LidarWorld.Tile;
using LasUtility.Nls;
using NetTopologySuite.Geometries;
using System;
using System.Xml.Schema;
using UnityEditor.SceneManagement;

public class DemBuildTests : MonoBehaviour
{
    private const int _iEdgeSkip = 100;
    private const int _MaxHeightDiffPerTileEdgeAvg = 10;

    //[OneTimeSetUp]
    //public void OneTimeSetup()
    //{
    //    EditorSceneManager.LoadSceneInPlayMode("Packages/fi.kuoste.terraintile/Samples/Helsinki9km2/Helsinki9km2.unity", new LoadSceneParameters(LoadSceneMode.Single));
    //}

    [UnityTest]
    public IEnumerator DemBuildTestsCompareEdgeHeights()
    {
        GameObject goTerrain = new()
        {
            name = "Terrain"
        };

        // Deactivate GameObject for a while so that we can setup the script without the Awake() being called.
        goTerrain.SetActive(false);

        TileManager tileManager = goTerrain.AddComponent<TileManager>();
        tileManager.DataDirectoryIntermediate = @"Packages\fi.kuoste.terraintile\Samples\Helsinki9km2\DataProcessed";
        tileManager.DataDirectoryOriginal = @"Packages\fi.kuoste.terraintile\Samples\Helsinki9km2\DataNlsFinland";
        tileManager.RenderedArea = "L4133B1";
        tileManager.TerrainTemplate = Resources.Load<GameObject>("Prefabs/Terrain/TerrainTemplate");
        tileManager.WaterPlane = Resources.Load<GameObject>("Prefabs/Water/WaterPlane");
        tileManager.BuildingRoof = Resources.Load<Material>("Materials/BuildingRoof");
        tileManager.BuildingWall = Resources.Load<Material>("Materials/BuildingWall");

        Assert.NotNull(tileManager.TerrainTemplate);
        Assert.NotNull(tileManager.WaterPlane);
        Assert.NotNull(tileManager.BuildingRoof);
        Assert.NotNull(tileManager.BuildingWall);

        goTerrain.SetActive(true);

        // Wait that all tiles are sent to be rendered
        while (tileManager.GetTilesInProcessCount() > 0)
            yield return null;

        TileNamer.Decode(tileManager.RenderedArea, out Envelope bounds);

        float fHeightDiffTotal = 0.0f;
        int iTileCount = 0;

        // Can cast to int because here the bounds are always integers
        for (int x = (int)bounds.MinX; x < (int)bounds.MaxX; x += TileCommon.EdgeLength)
        {
            for (int y = (int)bounds.MinY; y < (int)bounds.MaxY; y += TileCommon.EdgeLength)
            {
                TerrainData terrainData = GetTerrainDataByCoordinate(x, y);

                // Get next tile towards north, if available
                if (y + TileCommon.EdgeLength < bounds.MaxY)
                { 
                    TerrainData terrainDataNorth = GetTerrainDataByCoordinate(x, y + TileCommon.EdgeLength);

                    int xxStart = 0;
                    if (x == (int)bounds.MinX)
                        xxStart += _iEdgeSkip;

                    int xxStop = terrainData.heightmapResolution;
                    if (x == (int)bounds.MaxX - TileCommon.EdgeLength)
                        xxStop -= _iEdgeSkip;

                    float fHeightDiff = 0.0f;

                    // Measure the height difference on the shared edge
                    for (int xx = xxStart; xx < xxStop; xx++)
                    {
                        float h = terrainData.GetHeight(xx, terrainData.heightmapResolution - 1);
                        float hN = terrainDataNorth.GetHeight(xx, 0);

                        fHeightDiff += Math.Abs(h - hN);
                    }

                    iTileCount++;
                    fHeightDiffTotal += fHeightDiff;

                    Debug.Log("DemBuildTestsCompareEdgeHeights height diff y " + fHeightDiff);
                }

                // Get next tile towards east, if available
                if (x + TileCommon.EdgeLength < bounds.MaxX)
                {
                    TerrainData terrainDataEast = GetTerrainDataByCoordinate(x + TileCommon.EdgeLength, y);

                    int yyStart = 0;
                    if (y == (int)bounds.MinY)
                        yyStart += _iEdgeSkip;

                    int yyStop = terrainData.heightmapResolution;
                    if (y == (int)bounds.MaxY - TileCommon.EdgeLength)
                        yyStop -= _iEdgeSkip;

                    float fHeightDiff = 0.0f;

                    // Measure the height difference on the shared edge
                    for (int yy = yyStart; yy < yyStop; yy++)
                    {
                        float h = terrainData.GetHeight(terrainData.heightmapResolution - 1, yy);
                        float hN = terrainDataEast.GetHeight(0, yy);

                        fHeightDiff += Math.Abs(h - hN);
                    }

                    iTileCount++;
                    fHeightDiffTotal += fHeightDiff;

                    Debug.Log("DemBuildTestsCompareEdgeHeights height diff x " + fHeightDiff);
                }
            }
        }

        Assert.True(fHeightDiffTotal < iTileCount * _MaxHeightDiffPerTileEdgeAvg * 2);
    }

    private static TerrainData GetTerrainDataByCoordinate(int x, int y)
    {
        string sTileName1km = TileNamer.Encode(x, y, TileCommon.EdgeLength);
        GameObject goTile = GameObject.Find(sTileName1km);

        if (goTile == null)
            throw new Exception("Tile " + sTileName1km + " cannot be found");

        TerrainData terrainData = goTile.GetComponent<Terrain>().terrainData;

        if (terrainData == null)
            throw new Exception("No TerrainData for tile " + sTileName1km);

        return terrainData;
    }
}
