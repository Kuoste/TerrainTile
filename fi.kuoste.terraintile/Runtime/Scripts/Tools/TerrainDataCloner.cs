using System.Diagnostics;
using UnityEngine;

namespace Kuoste.TerrainTile.Tools
{
    // From https://gist.github.com/zsoi/c965ff38938cd126f00d

    /// <summary>
    /// Provides means to deep-copy a TerrainData object because Unitys' built-in "Instantiate" method
    /// will miss some things and the resulting copy still shares data with the original.
    /// </summary>
    public class TerrainDataCloner
    {
        /// <summary>
        /// Creates a real deep-copy of a TerrainData
        /// </summary>
        /// <param name="original">TerrainData to duplicate</param>
        /// <returns>New terrain data instance</returns>
        public static TerrainData Clone(TerrainData original)
        {
            //Stopwatch sw = Stopwatch.StartNew();

            TerrainData dup = new();

            dup.alphamapResolution = original.alphamapResolution;
            dup.baseMapResolution = original.baseMapResolution;

            dup.detailPrototypes = CloneDetailPrototypes(original.detailPrototypes);

            //sw.Stop();
            //UnityEngine.Debug.Log($"TerrainData.Clone CloneDetailPrototypes took {sw.ElapsedMilliseconds} ms");
            //sw.Restart();

            dup.SetDetailResolution(original.detailResolution, original.detailResolutionPerPatch);

            dup.heightmapResolution = original.heightmapResolution;
            dup.size = original.size;

            //dup.splatPrototypes = CloneSplatPrototypes(original.splatPrototypes);
            dup.terrainLayers = original.terrainLayers;

            //dup.thickness = original.thickness;
            dup.wavingGrassAmount = original.wavingGrassAmount;
            dup.wavingGrassSpeed = original.wavingGrassSpeed;
            dup.wavingGrassStrength = original.wavingGrassStrength;
            dup.wavingGrassTint = original.wavingGrassTint;

            dup.SetAlphamaps(0, 0, original.GetAlphamaps(0, 0, original.alphamapWidth, original.alphamapHeight));

            //sw.Stop();
            //UnityEngine.Debug.Log($"TerrainData.Clone SetAlphamaps took {sw.ElapsedMilliseconds} ms");
            //sw.Restart();

            //dup.SetHeights(0, 0, original.GetHeights(0, 0, original.heightmapResolution, original.heightmapResolution));

            //for (int n = 0; n < original.detailPrototypes.Length; n++)
            //{
            //    dup.SetDetailLayer(0, 0, n, original.GetDetailLayer(0, 0, original.detailWidth, original.detailHeight, n));
            //}

            dup.treePrototypes = CloneTreePrototypes(original.treePrototypes);

            //sw.Stop();
            //UnityEngine.Debug.Log($"TerrainData.Clone CloneTreePrototypes took {sw.ElapsedMilliseconds} ms");
            //sw.Restart();

            //dup.treeInstances = CloneTreeInstances(original.treeInstances);

            //sw.Stop();
            //UnityEngine.Debug.Log($"TerrainData.Clone took {sw.ElapsedMilliseconds} ms");

            return dup;
        }

        /// <summary>
        /// Deep-copies an array of detail prototype instances
        /// </summary>
        /// <param name="original">Prototypes to clone</param>
        /// <returns>Cloned array</returns>
        private static DetailPrototype[] CloneDetailPrototypes(DetailPrototype[] original)
        {
            DetailPrototype[] protoDuplicate = new DetailPrototype[original.Length];

            for (int n = 0; n < original.Length; n++)
            {
                protoDuplicate[n] = new DetailPrototype
                {
                    //bendFactor = original[n].bendFactor,
                    dryColor = original[n].dryColor,
                    healthyColor = original[n].healthyColor,
                    maxHeight = original[n].maxHeight,
                    maxWidth = original[n].maxWidth,
                    minHeight = original[n].minHeight,
                    minWidth = original[n].minWidth,
                    noiseSpread = original[n].noiseSpread,
                    prototype = original[n].prototype,
                    prototypeTexture = original[n].prototypeTexture,
                    renderMode = original[n].renderMode,
                    usePrototypeMesh = original[n].usePrototypeMesh,
                    useInstancing = original[n].useInstancing,
                };
            }

            return protoDuplicate;
        }

        /// <summary>
        /// Deep-copies an array of tree prototype instances
        /// </summary>
        /// <param name="original">Prototypes to clone</param>
        /// <returns>Cloned array</returns>
        private static TreePrototype[] CloneTreePrototypes(TreePrototype[] original)
        {
            TreePrototype[] protoDuplicate = new TreePrototype[original.Length];

            for (int n = 0; n < original.Length; n++)
            {
                protoDuplicate[n] = new TreePrototype
                {
                    bendFactor = original[n].bendFactor,
                    prefab = original[n].prefab,
                };
            }

            return protoDuplicate;
        }

        /// <summary>
        /// Deep-copies an array of tree instances
        /// </summary>
        /// <param name="original">Trees to clone</param>
        /// <returns>Cloned array</returns>
        private static TreeInstance[] CloneTreeInstances(TreeInstance[] original)
        {
            TreeInstance[] treeInst = new TreeInstance[original.Length];

            System.Array.Copy(original, treeInst, original.Length);

            return treeInst;
        }
    }
}